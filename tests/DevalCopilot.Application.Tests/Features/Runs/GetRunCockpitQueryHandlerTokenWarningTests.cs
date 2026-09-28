using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// End-to-end coverage of the advisory token-activity warnings as <c>GetRunCockpitQueryHandler</c>
/// produces them from real SQLite-persisted Agent attempts (claimed and completed through the real
/// Domain factories), including the persisted-row edge cases the pure projection tests cannot reach.
/// </summary>
public sealed class GetRunCockpitQueryHandlerTokenWarningTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static AgentTokenUsageEvidence CodexUsage(int input, int output) =>
        AgentTokenUsageEvidence.Create(input, output, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    private static AgentTokenUsageEvidence ClaudeUsage(int input, int output, int? creation, int? read) =>
        AgentTokenUsageEvidence.Create(input, output, creation, read, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

    private static Attempt Codex(Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentTokenUsageEvidence? usage, bool dispatch = true, bool conclude = true)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        if (!dispatch)
        {
            // Failed before any provider invocation; also keeps at most one Running attempt per run.
            attempt.Fail(Now);
            return attempt;
        }

        attempt.MarkAgentDispatched(Now);

        if (conclude)
        {
            attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit, tokenUsage: usage);
        }

        return attempt;
    }

    private static Attempt Claude(Guid runId, Guid workspaceId, Guid checkpointId, int number, AgentTokenUsageEvidence? usage)
    {
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit, tokenUsage: usage);
        return attempt;
    }

    private async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedRunAsync(
        long? codexThreshold = null, long? claudeThreshold = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Ship the slice", Now);
        if (codexThreshold is not null)
        {
            run.SetTokenWarningThreshold(AgentProvider.Codex, codexThreshold);
        }

        if (claudeThreshold is not null)
        {
            run.SetTokenWarningThreshold(AgentProvider.ClaudeCode, claudeThreshold);
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

    private async Task AddAsync(params Attempt[] attempts)
    {
        await using var context = _fixture.CreateContext();
        context.Attempts.AddRange(attempts);
        await context.SaveChangesAsync();
    }

    private async Task<GetRunCockpitQueryResult> ReadCockpitAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetRunCockpitQueryHandler(context, new FixedTimeProvider(Now))
            .HandleAsync(new GetRunCockpitQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static RunCockpitTokenWarningEntry For(GetRunCockpitQueryResult cockpit, AgentProvider provider) =>
        Assert.Single(cockpit.TokenWarnings!, entry => entry.Provider == provider);

    [Fact]
    public async Task A_run_without_thresholds_or_attempts_is_neutral_for_both_providers()
    {
        var (runId, _, _) = await SeedRunAsync();

        var cockpit = await ReadCockpitAsync(runId);

        Assert.Equal(2, cockpit.TokenWarnings!.Count);
        Assert.All(cockpit.TokenWarnings, entry =>
        {
            Assert.Equal(RunCockpitTokenWarningState.NotConfigured, entry.State);
            Assert.Null(entry.ThresholdTokens);
        });
    }

    [Fact]
    public async Task Persisted_evidence_uses_each_providers_own_formula_and_exact_equality_warns()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 1200, claudeThreshold: 616);
        await AddAsync(
            Codex(runId, workspaceId, checkpointId, 1, CodexUsage(1000, 200)),
            Claude(runId, workspaceId, checkpointId, 2, ClaudeUsage(500, 50, 5, 60)));

        var cockpit = await ReadCockpitAsync(runId);

        var codex = For(cockpit, AgentProvider.Codex);
        Assert.Equal(1200, codex.KnownTokenCount);
        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, codex.State);
        var claude = For(cockpit, AgentProvider.ClaudeCode);
        Assert.Equal(615, claude.KnownTokenCount);
        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, claude.State);
    }

    [Fact]
    public async Task A_persisted_claude_row_without_cache_counts_stays_visible_in_the_raw_view_but_is_insufficient_for_the_warning()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(claudeThreshold: 10);
        await AddAsync(Claude(runId, workspaceId, checkpointId, 1, ClaudeUsage(500, 50, null, null)));

        var cockpit = await ReadCockpitAsync(runId);

        // The existing raw usage projection is unchanged: it still counts this row's input/output.
        Assert.Equal(1, cockpit.TokenUsageSummary.AttemptsWithKnownUsage);
        Assert.Equal(500, cockpit.TokenUsageSummary.InputTokens);
        var claude = For(cockpit, AgentProvider.ClaudeCode);
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, claude.State);
        Assert.Equal(0, claude.KnownTokenCount);
        Assert.Equal(1, claude.InsufficientEvidenceAttempts);
    }

    [Fact]
    public async Task A_provider_schema_mismatch_is_insufficient_and_never_a_false_warning_or_false_attribution()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 1, claudeThreshold: 1);
        var mismatched = Claude(runId, workspaceId, checkpointId, 1, ClaudeUsage(500, 50, 5, 60));
        AttemptProviderSubstitution.SetProvider(mismatched, AgentProvider.Codex);
        await AddAsync(mismatched);

        var cockpit = await ReadCockpitAsync(runId);

        var codex = For(cockpit, AgentProvider.Codex);
        Assert.Equal(0, codex.KnownTokenCount);
        Assert.Equal(1, codex.InsufficientEvidenceAttempts);
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, codex.State);
        Assert.Equal(0, For(cockpit, AgentProvider.ClaudeCode).KnownTokenCount);
    }

    [Fact]
    public async Task An_unattributed_dispatched_attempt_is_a_gap_for_both_providers_and_never_assigned()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 1_000_000, claudeThreshold: 1_000_000);
        var unattributed = Codex(runId, workspaceId, checkpointId, 1, CodexUsage(999_999, 999_999));
        AttemptProviderSubstitution.SetProvider(unattributed, (AgentProvider?)null);
        await AddAsync(unattributed);

        var cockpit = await ReadCockpitAsync(runId);

        foreach (var provider in new[] { AgentProvider.Codex, AgentProvider.ClaudeCode })
        {
            var entry = For(cockpit, provider);
            Assert.Equal(0, entry.KnownTokenCount);
            Assert.Equal(1, entry.UnattributedAttempts);
            Assert.Equal(RunCockpitTokenWarningState.Indeterminate, entry.State);
        }
    }

    [Fact]
    public async Task An_undispatched_attempt_is_excluded_and_a_running_one_is_never_counted()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 100);
        await AddAsync(
            Codex(runId, workspaceId, checkpointId, 1, usage: null, dispatch: false),
            Codex(runId, workspaceId, checkpointId, 2, usage: null, dispatch: true, conclude: false));

        var cockpit = await ReadCockpitAsync(runId);

        var codex = For(cockpit, AgentProvider.Codex);
        Assert.Equal(0, codex.KnownTokenCount);
        Assert.Equal(1, codex.PendingAttempts);
        Assert.Equal(0, codex.InsufficientEvidenceAttempts);
        Assert.Equal(RunCockpitTokenWarningState.Indeterminate, codex.State);
    }

    [Fact]
    public async Task Only_an_undispatched_attempt_leaves_a_configured_threshold_with_no_evidence()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 100);
        await AddAsync(Codex(runId, workspaceId, checkpointId, 1, usage: null, dispatch: false));

        var codex = For(await ReadCockpitAsync(runId), AgentProvider.Codex);

        Assert.Equal(RunCockpitTokenWarningState.NoEvidence, codex.State);
    }

    [Fact]
    public async Task Changing_a_threshold_re_evaluates_recorded_evidence_without_touching_any_attempt()
    {
        var (runId, workspaceId, checkpointId) = await SeedRunAsync(codexThreshold: 5_000);
        await AddAsync(Codex(runId, workspaceId, checkpointId, 1, CodexUsage(1000, 200)));
        Assert.Equal(RunCockpitTokenWarningState.BelowThresholdComplete, For(await ReadCockpitAsync(runId), AgentProvider.Codex).State);

        await using (var context = _fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetTokenWarningThreshold(AgentProvider.Codex, 1_200);
            await context.SaveChangesAsync();
        }

        Assert.Equal(RunCockpitTokenWarningState.ThresholdReached, For(await ReadCockpitAsync(runId), AgentProvider.Codex).State);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(attempt => attempt.RunId == runId));
    }
}
