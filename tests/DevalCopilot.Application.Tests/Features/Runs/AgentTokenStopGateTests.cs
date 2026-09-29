using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TokenStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The claim gate against real SQLite-persisted Agent attempts built through the real Domain
/// factories, including the persisted-row shapes the Domain itself refuses to record. No provider
/// adapter exists on this path, so every test here is also a from-persisted-state replay.
/// </summary>
public sealed class AgentTokenStopGateTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedRunAsync(
        long? codexStop = null, long? claudeStop = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Ship the slice", Now);
        run.Claim(Now);
        if (codexStop is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.Codex, codexStop);
        }

        if (claudeStop is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.ClaudeCode, claudeStop);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        await context.SaveChangesAsync();
        return (run.Id, workspace.Id, checkpoint.Id);
    }

    private async Task<string?> CheckAsync(Guid runId, AgentProvider provider)
    {
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        return (await AgentTokenStopGate.CheckClaimAsync(context, run, provider, CancellationToken.None))?.Code;
    }

    [Fact]
    public async Task An_unconfigured_stop_permits_the_claim_and_reads_nothing_even_with_reached_looking_history()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(999_999, 999_999)));
        var counter = new ReaderCountingInterceptor();
        await using var context = _fixture.CreateContext(counter);
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        var readsBefore = counter.Count;

        var error = await AgentTokenStopGate.CheckClaimAsync(context, run, AgentProvider.Codex, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(readsBefore, counter.Count);
    }

    [Fact]
    public async Task A_configured_stop_with_no_dispatched_history_permits_the_first_claim()
    {
        var (runId, _, _) = await SeedRunAsync(codexStop: 1, claudeStop: 1);

        Assert.Null(await CheckAsync(runId, AgentProvider.Codex));
        Assert.Null(await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task Undispatched_attempts_consume_no_tokens_and_do_not_make_the_history_a_gap()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1);
        await AddAsync(
            _fixture,
            UndispatchedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex),
            UndispatchedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.Codex));

        Assert.Null(await CheckAsync(runId, AgentProvider.Codex));
    }

    [Theory]
    [InlineData(1200L, AgentTokenStopGate.ReachedCode)]
    [InlineData(1201L, null)]
    public async Task Codex_equality_at_the_threshold_blocks_and_one_more_permits(long threshold, string? expectedCode)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: threshold);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)));

        Assert.Equal(expectedCode, await CheckAsync(runId, AgentProvider.Codex));
    }

    [Theory]
    [InlineData(615L, AgentTokenStopGate.ReachedCode)]
    [InlineData(616L, null)]
    public async Task Claude_counts_input_cache_creation_cache_read_and_output(long threshold, string? expectedCode)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(claudeStop: threshold);
        await AddAsync(
            _fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.ClaudeCode, ClaudeUsage(500, 50, 5, 60)));

        Assert.Equal(expectedCode, await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task A_running_dispatched_attempt_makes_a_configured_provider_indeterminate()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000);
        await AddAsync(_fixture, RunningHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex));

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.Codex));
    }

    [Fact]
    public async Task A_concluded_dispatched_attempt_without_usage_is_indeterminate_not_zero()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(claudeStop: 1_000_000);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.ClaudeCode, usage: null));

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Theory]
    [InlineData("AgentInputTokens = -5")]
    [InlineData("AgentOutputTokens = NULL")]
    [InlineData("AgentTokenUsageSchemaVersion = 'claude-cli-usage-v9'")]
    [InlineData("AgentTokenUsageSchemaVersion = 'codex-cli-usage-v1'")]
    [InlineData("AgentTokenUsageSchemaVersion = NULL")]
    [InlineData("AgentCacheReadInputTokens = NULL")]
    public async Task Malformed_or_unsupported_persisted_claude_usage_is_indeterminate(string corruption)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(claudeStop: 1_000_000);
        var attempt = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.ClaudeCode, ClaudeUsage(5, 5, 5, 5));
        await AddAsync(_fixture, attempt);
        await CorruptAsync(_fixture, attempt.Id, corruption);

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task A_persisted_codex_row_carrying_a_cache_breakdown_is_indeterminate()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000);
        var attempt = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(5, 5));
        await AddAsync(_fixture, attempt);
        await CorruptAsync(_fixture, attempt.Id, "AgentCacheReadInputTokens = 3");

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.Codex));
    }

    [Fact]
    public async Task An_unattributed_dispatched_attempt_is_a_gap_for_both_configured_providers()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000, claudeStop: 1_000_000);
        var attempt = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(5, 5));
        await AddAsync(_fixture, attempt);
        await CorruptAsync(_fixture, attempt.Id, "AgentProvider = NULL");

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.Codex));
        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task A_known_count_at_the_threshold_is_reached_even_beside_a_gap()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 100);
        await AddAsync(
            _fixture,
            ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(60, 40)),
            ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.Codex, usage: null));

        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));
    }

    [Fact]
    public async Task Providers_are_separate_one_reached_does_not_stop_the_other_and_the_other_providers_gap_is_ignored()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1200, claudeStop: 1_000_000);
        await AddAsync(
            _fixture,
            ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)),
            ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.ClaudeCode, ClaudeUsage(1, 1, 1, 1)));

        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));
        Assert.Null(await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task A_provider_without_its_own_threshold_is_unaffected_by_the_other_providers_configured_stop()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(500, 500)));

        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));
        Assert.Null(await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Fact]
    public async Task The_advisory_warning_threshold_is_never_an_eligibility_input()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync();
        await using (var context = _fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetTokenWarningThreshold(AgentProvider.Codex, 1);
            await context.SaveChangesAsync();
        }

        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(500, 500)));

        Assert.Null(await CheckAsync(runId, AgentProvider.Codex));
    }

    [Fact]
    public async Task Raising_the_threshold_after_a_reached_state_re_evaluates_the_same_persisted_evidence()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1200);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)));
        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));

        await SetStopAsync(_fixture, runId, AgentProvider.Codex, 5000);
        Assert.Null(await CheckAsync(runId, AgentProvider.Codex));

        await SetStopAsync(_fixture, runId, AgentProvider.Codex, 1200);
        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));

        await SetStopAsync(_fixture, runId, AgentProvider.Codex, null);
        Assert.Null(await CheckAsync(runId, AgentProvider.Codex));
    }

    // ---- Untrusted persisted enum evidence ----------------------------------------------------

    public static TheoryData<string> UntrustedRowCorruptions => new()
    {
        "Status = 'not-a-status'",
        "Status = '99'",
        "Status = ''",
        "AgentProvider = 'not-a-provider'",
        "AgentProvider = 'ClaudeCode' , AgentRole = 'Planner'",
        "AgentRole = 'CriticalReviewer'",
        "AgentRole = 'not-a-role'",
    };

    [Theory]
    [MemberData(nameof(UntrustedRowCorruptions))]
    public async Task An_untrusted_persisted_row_is_a_fixed_indeterminate_gap_beside_a_healthy_attempt(string corruption)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000, claudeStop: 1_000_000);
        var healthy = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(10, 10));
        var corrupted = ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.Codex, CodexUsage(999_999, 999_999));
        await AddAsync(_fixture, healthy, corrupted);
        await CorruptAsync(_fixture, corrupted.Id, corruption);

        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.Codex));
        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.ClaudeCode));
    }

    [Theory]
    [MemberData(nameof(UntrustedRowCorruptions))]
    public async Task A_known_count_at_the_threshold_from_sound_rows_is_still_reached_beside_an_untrusted_row(string corruption)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 20);
        var healthy = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(10, 10));
        var corrupted = ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.Codex, CodexUsage(1, 1));
        await AddAsync(_fixture, healthy, corrupted);
        await CorruptAsync(_fixture, corrupted.Id, corruption);

        Assert.Equal(AgentTokenStopGate.ReachedCode, await CheckAsync(runId, AgentProvider.Codex));
    }

    [Fact]
    public async Task An_incoherent_role_provider_row_never_adds_its_usage_to_the_provider_it_names()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 100);
        var incoherent = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(500, 500));
        await AddAsync(_fixture, incoherent);
        await CorruptAsync(_fixture, incoherent.Id, "AgentRole = 'CriticalReviewer'");

        // Its 1,000 tokens are not attributed to Codex, so this is a gap and not a reached count.
        Assert.Equal(AgentTokenStopGate.EvidenceIndeterminateCode, await CheckAsync(runId, AgentProvider.Codex));
    }
}
