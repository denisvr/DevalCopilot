using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TokenStopTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The cockpit's token stop projection from real SQLite-persisted attempts. It is derived by the
/// same evaluator the claim gate uses, kept separate from the advisory warning, and re-derived from
/// persisted state on every read, so a restart or replay needs no provider call.
/// </summary>
public sealed class GetRunCockpitQueryHandlerTokenStopTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedRunAsync(
        long? codexStop = null, long? claudeStop = null, long? codexWarning = null)
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

        if (codexWarning is not null)
        {
            run.SetTokenWarningThreshold(AgentProvider.Codex, codexWarning);
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

    private async Task<GetRunCockpitQueryResult> ReadCockpitAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetRunCockpitQueryHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new GetRunCockpitQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AgentTokenStopEvaluation StopFor(GetRunCockpitQueryResult cockpit, AgentProvider provider) =>
        Assert.Single(cockpit.TokenStops!, entry => entry.Provider == provider);

    [Fact]
    public async Task A_run_without_stop_thresholds_reports_both_providers_as_not_configured_and_never_blocking()
    {
        var (runId, _, _) = await SeedRunAsync();

        var cockpit = await ReadCockpitAsync(runId);

        Assert.Equal(2, cockpit.TokenStops!.Count);
        Assert.Equal(AgentProvider.Codex, cockpit.TokenStops[0].Provider);
        Assert.Equal(AgentProvider.ClaudeCode, cockpit.TokenStops[1].Provider);
        Assert.All(cockpit.TokenStops, entry =>
        {
            Assert.Equal(AgentTokenStopState.NotConfigured, entry.State);
            Assert.Null(entry.ThresholdTokens);
            Assert.False(entry.BlocksClaim);
        });
    }

    [Fact]
    public async Task A_configured_stop_with_no_dispatched_history_is_distinct_from_a_measured_zero()
    {
        var (runId, _, _) = await SeedRunAsync(codexStop: 100);

        var codex = StopFor(await ReadCockpitAsync(runId), AgentProvider.Codex);

        Assert.Equal(AgentTokenStopState.NoDispatchedHistory, codex.State);
        Assert.Equal(100, codex.ThresholdTokens);
        Assert.False(codex.BlocksClaim);
    }

    [Fact]
    public async Task Persisted_evidence_uses_each_providers_formula_and_exact_equality_blocks()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1200, claudeStop: 616);
        await AddAsync(
            _fixture,
            ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)),
            ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.ClaudeCode, ClaudeUsage(500, 50, 5, 60)));

        var cockpit = await ReadCockpitAsync(runId);

        var codex = StopFor(cockpit, AgentProvider.Codex);
        Assert.Equal((AgentTokenStopState.ThresholdReached, 1200L, true), (codex.State, codex.KnownTokenCount, codex.BlocksClaim));
        var claude = StopFor(cockpit, AgentProvider.ClaudeCode);
        Assert.Equal((AgentTokenStopState.BelowThresholdComplete, 615L, false), (claude.State, claude.KnownTokenCount, claude.BlocksClaim));
    }

    [Fact]
    public async Task Running_undispatched_and_missing_evidence_are_reported_as_their_own_counts_and_block_when_unproven()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(claudeStop: 1_000_000);
        await AddAsync(
            _fixture,
            UndispatchedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.ClaudeCode),
            ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.ClaudeCode, usage: null),
            RunningHistory(runId, workspaceId, checkpointId, 3, AgentProvider.ClaudeCode));

        var claude = StopFor(await ReadCockpitAsync(runId), AgentProvider.ClaudeCode);

        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, claude.State);
        Assert.True(claude.BlocksClaim);
        Assert.Equal((0, 1, 1, 0), (claude.CountedAttempts, claude.InsufficientEvidenceAttempts, claude.PendingAttempts, claude.UnattributedAttempts));
    }

    [Fact]
    public async Task An_unattributed_attempt_is_reported_and_is_a_gap_for_both_providers()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000, claudeStop: 1_000_000);
        var attempt = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(5, 5));
        await AddAsync(_fixture, attempt);
        await CorruptAsync(_fixture, attempt.Id, "AgentProvider = NULL");

        var cockpit = await ReadCockpitAsync(runId);

        Assert.All(cockpit.TokenStops!, entry =>
        {
            Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, entry.State);
            Assert.Equal(1, entry.UnattributedAttempts);
        });
    }

    [Fact]
    public async Task The_stop_and_the_advisory_warning_are_independent_projections_with_their_own_thresholds()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 5000, codexWarning: 1000);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)));

        var cockpit = await ReadCockpitAsync(runId);

        var warning = Assert.Single(cockpit.TokenWarnings!, entry => entry.Provider == AgentProvider.Codex);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, warning.State);
        Assert.Equal(1000, warning.ThresholdTokens);
        var stop = StopFor(cockpit, AgentProvider.Codex);
        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, stop.State);
        Assert.Equal(5000, stop.ThresholdTokens);
        Assert.False(stop.BlocksClaim);
    }

    [Fact]
    public async Task A_stop_threshold_alone_never_creates_an_advisory_warning_and_the_warning_state_never_gates()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1200);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)));

        var cockpit = await ReadCockpitAsync(runId);

        Assert.Equal(
            RunCockpitTokenWarningState.NotConfigured,
            Assert.Single(cockpit.TokenWarnings!, entry => entry.Provider == AgentProvider.Codex).State);
        Assert.Equal(AgentTokenStopState.ThresholdReached, StopFor(cockpit, AgentProvider.Codex).State);
    }

    [Fact]
    public async Task Changing_the_threshold_re_derives_the_same_persisted_evidence_on_the_next_read_without_a_provider_call()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1200);
        await AddAsync(_fixture, ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(1000, 200)));
        Assert.Equal(AgentTokenStopState.ThresholdReached, StopFor(await ReadCockpitAsync(runId), AgentProvider.Codex).State);

        await SetStopAsync(_fixture, runId, AgentProvider.Codex, 5000);
        Assert.Equal(AgentTokenStopState.BelowThresholdComplete, StopFor(await ReadCockpitAsync(runId), AgentProvider.Codex).State);

        await SetStopAsync(_fixture, runId, AgentProvider.Codex, null);
        Assert.Equal(AgentTokenStopState.NotConfigured, StopFor(await ReadCockpitAsync(runId), AgentProvider.Codex).State);
    }

    [Fact]
    public async Task The_cockpit_and_the_claim_gate_agree_on_the_same_persisted_evidence()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 100, claudeStop: 100);
        await AddAsync(
            _fixture,
            ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(60, 40)),
            ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.ClaudeCode, usage: null));

        var cockpit = await ReadCockpitAsync(runId);

        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);
        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            var gate = await AgentTokenStopGate.CheckClaimAsync(context, run, provider, CancellationToken.None);
            Assert.Equal(AgentTokenStopGate.ToError(StopFor(cockpit, provider))?.Code, gate?.Code);
        }
    }

    [Theory]
    [InlineData("Status = 'not-a-status'")]
    [InlineData("Status = '99'")]
    [InlineData("AgentProvider = 'not-a-provider'")]
    [InlineData("AgentRole = 'CriticalReviewer'")]
    public async Task An_untrusted_persisted_row_does_not_fail_the_cockpit_and_is_an_unattributed_gap_beside_a_healthy_attempt(string corruption)
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexStop: 1_000_000, claudeStop: 1_000_000);
        var healthy = ConcludedHistory(runId, workspaceId, checkpointId, 1, AgentProvider.Codex, CodexUsage(10, 10));
        var corrupted = ConcludedHistory(runId, workspaceId, checkpointId, 2, AgentProvider.Codex, CodexUsage(999_999, 999_999));
        await AddAsync(_fixture, healthy, corrupted);
        await CorruptAsync(_fixture, corrupted.Id, corruption);

        var cockpit = await ReadCockpitAsync(runId);

        var codex = StopFor(cockpit, AgentProvider.Codex);
        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, codex.State);
        Assert.Equal((1, 20L, 1), (codex.CountedAttempts, codex.KnownTokenCount, codex.UnattributedAttempts));
        Assert.True(codex.BlocksClaim);
        Assert.Equal(AgentTokenStopState.EvidenceIndeterminate, StopFor(cockpit, AgentProvider.ClaudeCode).State);
    }
}
