using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// End-to-end coverage of the provider-separated token-usage projection as
/// <c>GetRunCockpitQueryHandler</c> actually produces it: real SQLite-persisted Agent attempts
/// claimed and completed through the real Domain factories, not a hand-built projection input.
/// Confirms the existing run-wide <see cref="RunCockpitTokenUsageSummary"/> is unchanged by this
/// addition, and that Codex, Claude Code, and an unattributed dispatched attempt are reported in
/// separate, independently correct buckets.
/// </summary>
public sealed class GetRunCockpitQueryHandlerProviderTokenUsageTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static readonly AgentTokenUsageEvidence CodexUsage =
        AgentTokenUsageEvidence.Create(1000, 200, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    private static readonly AgentTokenUsageEvidence ClaudeUsage =
        AgentTokenUsageEvidence.Create(500, 50, 5, 60, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static (Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint) BuildScaffold()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Ship the slice", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        return (project, run, workspace, checkpoint);
    }

    private static Attempt ClaimAndCompleteCodex(
        Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber, AgentTokenUsageEvidence? usage)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, attemptNumber);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit, tokenUsage: usage);
        return attempt;
    }

    private static Attempt ClaimAndCompleteClaudeCode(
        Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber, AgentTokenUsageEvidence? usage)
    {
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, attemptNumber);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit, tokenUsage: usage);
        return attempt;
    }

    private static Attempt ClaimDispatchedButStillRunningCodex(
        Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, attemptNumber);
        attempt.MarkAgentDispatched(Now);
        return attempt;
    }

    [Fact]
    public async Task Codex_and_ClaudeCode_dispatched_attempts_are_reported_in_separate_buckets_and_the_run_wide_total_is_unchanged()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold();

        var codexAttempt = ClaimAndCompleteCodex(run.Id, workspace.Id, checkpoint.Id, 1, CodexUsage);
        var claudeAttempt = ClaimAndCompleteClaudeCode(run.Id, workspace.Id, checkpoint.Id, 2, ClaudeUsage);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.AddRange(codexAttempt, claudeAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The existing run-wide summary must still cover both attempts, unaffected by the new
        // per-provider projection living alongside it.
        Assert.Equal(RunTokenUsageCompleteness.Complete, result.Value.TokenUsageSummary.Completeness);
        Assert.Equal(2, result.Value.TokenUsageSummary.AttemptsWithKnownUsage);
        Assert.Equal(1500, result.Value.TokenUsageSummary.InputTokens);
        Assert.Equal(250, result.Value.TokenUsageSummary.OutputTokens);

        var byAttribution = result.Value.ProviderTokenUsageSummaries.ToDictionary(entry => entry.Attribution, entry => entry.Summary);
        Assert.Equal(3, byAttribution.Count);

        var codex = byAttribution[RunCockpitProviderTokenUsageAttribution.Codex];
        Assert.Equal(RunTokenUsageCompleteness.Complete, codex.Completeness);
        Assert.Equal(1, codex.AttemptsWithKnownUsage);
        Assert.Equal(1000, codex.InputTokens);
        Assert.Equal(200, codex.OutputTokens);

        var claude = byAttribution[RunCockpitProviderTokenUsageAttribution.ClaudeCode];
        Assert.Equal(RunTokenUsageCompleteness.Complete, claude.Completeness);
        Assert.Equal(1, claude.AttemptsWithKnownUsage);
        Assert.Equal(500, claude.InputTokens);
        Assert.Equal(50, claude.OutputTokens);
        Assert.Equal(5, claude.CacheCreationInputTokens);
        Assert.Equal(60, claude.CacheReadInputTokens);

        var unattributed = byAttribution[RunCockpitProviderTokenUsageAttribution.Unattributed];
        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, unattributed.Completeness);
    }

    [Fact]
    public async Task An_attempt_with_a_persisted_null_provider_is_reported_as_unattributed_never_dropped()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold();

        var attempt = ClaimAndCompleteCodex(run.Id, workspace.Id, checkpoint.Id, 1, CodexUsage);
        // Simulates a historical/malformed row whose provider was never recorded — never reachable
        // through a production claim factory, which always fixes exactly one provider per role.
        AttemptProviderSubstitution.SetProvider(attempt, (AgentProvider?)null);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byAttribution = result.Value.ProviderTokenUsageSummaries.ToDictionary(entry => entry.Attribution, entry => entry.Summary);

        // FromPersisted itself rejects usage recorded against a null provider (IsSupportedSource),
        // so this row's usage is unknown — the attempt still concluded, so it is a genuine terminal
        // gap, never silently dropped from every bucket.
        var unattributed = byAttribution[RunCockpitProviderTokenUsageAttribution.Unattributed];
        Assert.Equal(RunTokenUsageCompleteness.Partial, unattributed.Completeness);
        Assert.Equal(1, unattributed.TerminalAttemptsWithUnknownUsage);

        var codex = byAttribution[RunCockpitProviderTokenUsageAttribution.Codex];
        Assert.Equal(RunTokenUsageCompleteness.NoDispatchedAttempts, codex.Completeness);
    }

    [Fact]
    public async Task A_still_running_dispatched_attempt_is_pending_in_its_own_bucket_and_never_counted_as_known()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = BuildScaffold();

        var runningAttempt = ClaimDispatchedButStillRunningCodex(run.Id, workspace.Id, checkpoint.Id, 1);
        var completedClaude = ClaimAndCompleteClaudeCode(run.Id, workspace.Id, checkpoint.Id, 2, ClaudeUsage);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.Attempts.AddRange(runningAttempt, completedClaude);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetRunCockpitQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetRunCockpitQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byAttribution = result.Value.ProviderTokenUsageSummaries.ToDictionary(entry => entry.Attribution, entry => entry.Summary);

        var codex = byAttribution[RunCockpitProviderTokenUsageAttribution.Codex];
        Assert.Equal(RunTokenUsageCompleteness.PendingEvidence, codex.Completeness);
        Assert.Equal(0, codex.AttemptsWithKnownUsage);
        Assert.Equal(1, codex.PendingAttemptCount);

        var claude = byAttribution[RunCockpitProviderTokenUsageAttribution.ClaudeCode];
        Assert.Equal(RunTokenUsageCompleteness.Complete, claude.Completeness);
    }
}
