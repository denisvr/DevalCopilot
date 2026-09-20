using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewInputAlreadyReviewed;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>RecordAgentAttemptWorkspaceIneligibleCommandHandlerTests</c>' shape, plus the one
/// behavior that command does not have: this handler never trusts the caller's own claim that a
/// competing review exists — it independently re-verifies that exact evidence itself, and every
/// test proving rejection also proves zero mutation.
/// </summary>
public sealed class RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 20, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, Attempt Attempt, AttemptInputMessage InputMessage) CreateClaimedCriticalReviewAttempt(
        Guid? reviewedProposalId = null, int attemptNumber = 1)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, reviewedProposalId ?? Guid.NewGuid(), sequence: 0);
        return (project, run, attempt, inputMessage);
    }

    [Fact]
    public async Task HandleAsync_records_input_already_reviewed_when_a_completed_successful_competing_review_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var reviewedProposalId = Guid.NewGuid();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt(reviewedProposalId, attemptNumber: 1);

        var competingReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingReview.MarkAgentDispatched(Now);
        competingReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now);
        var competingInputMessage = AttemptInputMessage.Record(Guid.NewGuid(), competingReview.Id, reviewedProposalId, sequence: 0);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, competingReview);
        dbContext.AttemptInputMessages.AddRange(inputMessage, competingInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.InputAlreadyReviewed, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(Now.AddSeconds(1), attempt.CompletedAtUtc);

        var journalEvent = Assert.Single(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
        Assert.Equal(RunEventType.AgentAttemptCompleted, journalEvent.EventType);
        Assert.Contains("InputAlreadyReviewed", journalEvent.PayloadJson);

        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_treats_a_challenged_competing_review_as_evidence_too()
    {
        await using var dbContext = fixture.CreateContext();
        var reviewedProposalId = Guid.NewGuid();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt(reviewedProposalId, attemptNumber: 1);

        var competingReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingReview.MarkAgentDispatched(Now);
        competingReview.CompleteAgent(AgentOutcome.Challenged, Fingerprint, Now);
        var competingInputMessage = AttemptInputMessage.Record(Guid.NewGuid(), competingReview.Id, reviewedProposalId, sequence: 0);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, competingReview);
        dbContext.AttemptInputMessages.AddRange(inputMessage, competingInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.InputAlreadyReviewed, attempt.AgentOutcome);
    }

    /// <summary>
    /// The core guarantee this command exists for: it never trusts a caller's claim on faith. A
    /// speculative or stale dispatch of this command — no completed successful competing review
    /// actually exists for this attempt's input proposal — is safely rejected and mutates
    /// nothing, rather than inventing the fact the caller expected to find.
    /// </summary>
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_no_competing_review_exists_at_all()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_a_competing_review_for_the_same_proposal_is_still_running()
    {
        await using var dbContext = fixture.CreateContext();
        var reviewedProposalId = Guid.NewGuid();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt(reviewedProposalId, attemptNumber: 1);

        // The run-wide "one Running attempt" invariant forbids a second Running attempt on the
        // very same run, so the still-running competing attempt is seeded on an entirely
        // separate run here purely to exercise this handler's own query in isolation — the query
        // itself never scopes by RunId (an input-message id is effectively globally unique), so
        // this still proves the real code path. Not yet a fact this command may act on: the
        // competing attempt has not itself completed successfully yet, so there is nothing to
        // supersede this attempt with.
        var (otherProject, otherRun, _, _) = CreateClaimedCriticalReviewAttempt();
        var stillRunningCompetingReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), otherRun.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        var stillRunningInputMessage = AttemptInputMessage.Record(Guid.NewGuid(), stillRunningCompetingReview.Id, reviewedProposalId, sequence: 0);

        dbContext.Projects.AddRange(project, otherProject);
        dbContext.Runs.AddRange(run, otherRun);
        dbContext.Attempts.AddRange(attempt, stillRunningCompetingReview);
        dbContext.AttemptInputMessages.AddRange(inputMessage, stillRunningInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_a_completed_competing_attempt_for_the_same_proposal_did_not_succeed()
    {
        await using var dbContext = fixture.CreateContext();
        var reviewedProposalId = Guid.NewGuid();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt(reviewedProposalId, attemptNumber: 1);

        // Completed, but never a successful review — a failure outcome for the same proposal is
        // never evidence that this attempt was superseded.
        var failedCompetingAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        failedCompetingAttempt.MarkAgentDispatched(Now);
        failedCompetingAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        var failedInputMessage = AttemptInputMessage.Record(Guid.NewGuid(), failedCompetingAttempt.Id, reviewedProposalId, sequence: 0);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, failedCompetingAttempt);
        dbContext.AttemptInputMessages.AddRange(inputMessage, failedInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_the_completed_successful_review_is_for_a_different_proposal()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt(Guid.NewGuid(), attemptNumber: 1);

        // A real, successful review — but of a different Proposal entirely. Never evidence for
        // this attempt's own input proposal.
        var unrelatedSuccessfulReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now);
        unrelatedSuccessfulReview.MarkAgentDispatched(Now);
        unrelatedSuccessfulReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now);
        var unrelatedInputMessage = AttemptInputMessage.Record(Guid.NewGuid(), unrelatedSuccessfulReview.Id, Guid.NewGuid(), sequence: 0);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(attempt, unrelatedSuccessfulReview);
        dbContext.AttemptInputMessages.AddRange(inputMessage, unrelatedInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_review_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (targetProject, targetRun, _, _) = CreateClaimedCriticalReviewAttempt();
        var (otherProject, otherRun, otherAttempt, _) = CreateClaimedCriticalReviewAttempt();
        dbContext.Projects.AddRange(targetProject, otherProject);
        dbContext.Runs.AddRange(targetRun, otherRun);
        dbContext.Attempts.Add(otherAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(targetRun.Id, otherAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, otherAttempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_codex_planning_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_critical_review", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    // Fail-closed regression: a genuinely malformed persisted attempt — the correct AgentRole but
    // a response contract that does not cohere with AgentAttemptContract.For(role) — must be
    // rejected by the same safe "not this attempt shape" failure, never treated as a valid
    // critical-review attempt just because its role happens to match.
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_attempt_with_a_mismatched_response_contract()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, _) = CreateClaimedCriticalReviewAttempt();
        var responseContractProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))!;
        responseContractProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [AgentResponseContract.Proposal]);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_critical_review", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, _) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, _) = CreateClaimedCriticalReviewAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordClaudeCriticalReviewInputAlreadyReviewedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Assert.Single(result.Errors).Code);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
    }
}
