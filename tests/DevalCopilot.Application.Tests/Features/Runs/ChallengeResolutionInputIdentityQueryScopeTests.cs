using System.Data.Common;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionInputAlreadyResolved;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Regression coverage for the query-scoping correction to
/// <c>ChallengeResolutionInputIdentity.HasCompetingExactResolutionAsync</c>: candidate attempts
/// must be scoped to the current run and its exact original Proposal, and the number of database
/// round trips issued must never grow with the number of unrelated completed Resolver attempts
/// that exist. Each test owns its own <see cref="SqliteDatabaseFixture"/> rather than sharing one
/// via <see cref="IClassFixture{TFixture}"/>: the query-count test needs a dedicated,
/// interceptor-attached <see cref="DevalCopilotDbContext"/> per call, which a shared fixture's
/// single connection string cannot cleanly provide alongside other tests' own contexts.
/// </summary>
public sealed class ChallengeResolutionInputIdentityQueryScopeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>Counts every SELECT-shaped command EF Core actually sends to SQLite — the same
    /// signal a production APM/query-count assertion would use. Deterministic: driven by the
    /// real query plan the handler issues, never by wall-clock timing.</summary>
    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        private int _commandCount;

        public int CommandCount => Volatile.Read(ref _commandCount);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _commandCount);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commandCount);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static (Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint, RepositoryMutationLease Lease) BuildEligibleRunScaffold(
        string objective)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, objective, Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        return (project, run, workspace, checkpoint, lease);
    }

    private static Attempt ClaimResolvedCompetingAttempt(
        Guid runId, Guid workspaceId, Guid checkpointId, int attemptNumber, DateTimeOffset now)
    {
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, attemptNumber);
        attempt.MarkAgentDispatched(now);
        attempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        return attempt;
    }

    private static List<AttemptInputMessage> OrderedInputMessages(Guid attemptId, IReadOnlyList<Guid> collaborationMessageIds) =>
        collaborationMessageIds
            .Select((id, index) => AttemptInputMessage.Record(Guid.NewGuid(), attemptId, id, sequence: index))
            .ToList();

    /// <summary>
    /// The exact defect this correction fixes: before it, candidate attempts were never scoped
    /// to a run at all, so a genuinely unrelated attempt on a different run with the identical
    /// ordered input identity (a coincidence this test deliberately constructs) would have
    /// falsely superseded this attempt. After the fix, only same-run candidates are ever
    /// considered, so dispatch proceeds normally.
    /// </summary>
    [Fact]
    public async Task An_exact_ordered_input_identity_belonging_to_a_different_run_never_blocks_dispatch()
    {
        await using var dbContext = _fixture.CreateContext();

        var (ownProject, ownRun, ownWorkspace, ownCheckpoint, ownLease) = BuildEligibleRunScaffold("Resolve the challenged proposal");
        var ownAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), ownRun.Id, 1, ownWorkspace.Id, ownCheckpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);

        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();

        var (otherProject, otherRun, otherWorkspace, otherCheckpoint, otherLease) = BuildEligibleRunScaffold("An unrelated run");
        var foreignRunCompetingAttempt = ClaimResolvedCompetingAttempt(otherRun.Id, otherWorkspace.Id, otherCheckpoint.Id, 1, Now);

        dbContext.Projects.AddRange(ownProject, otherProject);
        dbContext.Runs.AddRange(ownRun, otherRun);
        dbContext.GitWorkspaces.AddRange(ownWorkspace, otherWorkspace);
        dbContext.GitCheckpoints.AddRange(ownCheckpoint, otherCheckpoint);
        dbContext.RepositoryMutationLeases.AddRange(ownLease, otherLease);
        dbContext.Attempts.AddRange(ownAttempt, foreignRunCompetingAttempt);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(ownAttempt.Id, [proposalId, challenge1Id, challenge2Id]));
        // The identical ordered identity — same Proposal id, same two Challenge ids, same order —
        // but recorded against a completely different run.
        dbContext.AttemptInputMessages.AddRange(
            OrderedInputMessages(foreignRunCompetingAttempt.Id, [proposalId, challenge1Id, challenge2Id]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(ownRun.Id, ownAttempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, ownAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// Same-run exact identity still correctly supersedes — the fix narrows the query's scope,
    /// it must never also narrow (or widen) which identities count as a genuine match.
    /// </summary>
    [Fact]
    public async Task A_same_run_exact_ordered_input_identity_still_produces_input_already_resolved()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint, lease) = BuildEligibleRunScaffold("Resolve the challenged proposal");
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);

        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var competing = ClaimResolvedCompetingAttempt(run.Id, workspace.Id, checkpoint.Id, 2, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competing);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, [proposalId, challenge1Id, challenge2Id]));
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(competing.Id, [proposalId, challenge1Id, challenge2Id]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var recordHandler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var recordResult = await recordHandler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(recordResult.IsSuccess);
        Assert.Equal(AgentOutcome.InputAlreadyResolved, attempt.AgentOutcome);
    }

    /// <summary>
    /// Discriminating regression for Slice B.1: the competing attempt's provider is substituted to
    /// the alternate one via reflection (a test-only helper, never a production path) — before this
    /// correction, the dedup query's own <c>AgentProvider == AgentProvider.Codex</c> prefilter
    /// would have silently excluded this genuinely identical competing resolution, letting a
    /// caller dispatch a duplicate. The match must be found by role/contract/outcome/exact input
    /// identity alone.
    /// </summary>
    [Fact]
    public async Task A_competing_resolution_from_the_alternate_provider_still_produces_input_already_resolved()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint, lease) = BuildEligibleRunScaffold("Resolve the challenged proposal");
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);

        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var competing = ClaimResolvedCompetingAttempt(run.Id, workspace.Id, checkpoint.Id, 2, Now);
        AttemptProviderSubstitution.SetProvider(competing, AgentProvider.ClaudeCode);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competing);
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, [proposalId, challenge1Id, challenge2Id]));
        dbContext.AttemptInputMessages.AddRange(OrderedInputMessages(competing.Id, [proposalId, challenge1Id, challenge2Id]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var recordHandler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var recordResult = await recordHandler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(recordResult.IsSuccess);
        Assert.Equal(AgentOutcome.InputAlreadyResolved, attempt.AgentOutcome);
    }

    /// <summary>
    /// The core defect this correction fixes: before it, every additional unrelated candidate
    /// attempt cost one additional database round trip (a classic N+1). Seeds a fixed real
    /// attempt plus either a small or a large number of same-run, same-original-Proposal but
    /// Challenge-mismatched "unrelated" completed Resolver attempts — each passes the narrowing
    /// SQL prefilter (same run, same role/provider/contract/outcome, same Proposal at sequence
    /// 0) but fails the final exact ordered comparison, so every one of them must genuinely be
    /// considered and rejected — this is not a case the fix could get away with skipping
    /// entirely. The command count the real handler issues for a small candidate set and a
    /// thirty-times-larger one, seeded and measured identically except for that count, must be
    /// exactly equal — a deterministic, execution-driven signal, never wall-clock timing.
    /// </summary>
    [Fact]
    public async Task Database_command_count_is_independent_of_the_number_of_unrelated_candidate_attempts()
    {
        var fewCandidatesCount = await MeasureCommandCountAsync(unrelatedCandidateCount: 3);
        var manyCandidatesCount = await MeasureCommandCountAsync(unrelatedCandidateCount: 30);

        // Exactly 4 SELECT commands in both cases: the attempt lookup, this attempt's own
        // ordered-input-ids query, the candidate-ids query, and the one bulk candidate-messages
        // query — never one query per unrelated candidate.
        Assert.Equal(fewCandidatesCount, manyCandidatesCount);
        Assert.Equal(4, fewCandidatesCount);
    }

    private async Task<int> MeasureCommandCountAsync(int unrelatedCandidateCount)
    {
        Guid runId;
        Guid attemptId;

        // Seeding happens on its own, uninstrumented DbContext: SaveChangesAsync's own INSERT
        // batches are routed through the same relational-command execution path EF Core uses for
        // queries, so counting them here would conflate seeding cost with the handler's actual
        // query cost — the one thing this test means to measure in isolation.
        await using (var seedContext = _fixture.CreateContext())
        {
            var (project, run, workspace, checkpoint, lease) = BuildEligibleRunScaffold("Resolve the challenged proposal");
            var attempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);

            var proposalId = Guid.NewGuid();
            var challenge1Id = Guid.NewGuid();
            var challenge2Id = Guid.NewGuid();

            seedContext.Projects.Add(project);
            seedContext.Runs.Add(run);
            seedContext.GitWorkspaces.Add(workspace);
            seedContext.GitCheckpoints.Add(checkpoint);
            seedContext.RepositoryMutationLeases.Add(lease);
            seedContext.Attempts.Add(attempt);
            seedContext.AttemptInputMessages.AddRange(OrderedInputMessages(attempt.Id, [proposalId, challenge1Id, challenge2Id]));

            for (var index = 0; index < unrelatedCandidateCount; index++)
            {
                var unrelated = ClaimResolvedCompetingAttempt(run.Id, workspace.Id, checkpoint.Id, index + 2, Now);
                seedContext.Attempts.Add(unrelated);
                // Same run, same original Proposal at sequence 0 (passes the SQL prefilter), but
                // a foreign Challenge at sequence 1 — never the same exact ordered identity.
                seedContext.AttemptInputMessages.AddRange(OrderedInputMessages(unrelated.Id, [proposalId, Guid.NewGuid()]));
            }

            await seedContext.SaveChangesAsync(CancellationToken.None);
            runId = run.Id;
            attemptId = attempt.Id;
        }

        var interceptor = new CommandCountingInterceptor();
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var handler = new RecordChallengeResolutionInputAlreadyResolvedCommandHandler(handlerContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordChallengeResolutionInputAlreadyResolvedCommand(runId, attemptId), CancellationToken.None);

        // None of the unrelated candidates is a genuine match, regardless of how many exist —
        // confirms every one of them was actually considered, not skipped.
        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_resolution_found", Assert.Single(result.Errors).Code);

        return interceptor.CommandCount;
    }
}
