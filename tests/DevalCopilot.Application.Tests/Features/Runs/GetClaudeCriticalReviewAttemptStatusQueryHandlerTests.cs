using DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>GetAgentAttemptStatusQueryHandlerTests</c> exactly, adapted for the
/// CriticalReviewer filter and the additional <c>ReviewedProposalMessageId</c> projection field
/// this status feed carries.</summary>
public sealed class GetClaudeCriticalReviewAttemptStatusQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 19, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static Attempt ClaimCriticalReviewAttempt(
        Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc, Guid? inputCollaborationMessageId = null) => Attempt.ClaimAgentCriticalReview(
        Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
        inputCollaborationMessageId ?? Guid.NewGuid(), Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc);

    [Fact]
    public async Task HandleAsync_returns_an_explicit_no_attempt_result_for_a_run_with_no_critical_review_attempt_yet()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No attempt yet", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.AttemptId);
        Assert.Null(result.Value.ReviewedProposalMessageId);
        Assert.Empty(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempts_bounded_status_reviewed_proposal_id_and_artifact_metadata()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var reviewedProposalId = Guid.NewGuid();
        var attempt = ClaimCriticalReviewAttempt(run.Id, 1, Now, reviewedProposalId);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(attempt.Id, result.Value.AttemptId);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(reviewedProposalId, result.Value.ReviewedProposalMessageId);
        Assert.Equal(AttemptStatus.Completed, result.Value.Status);
        Assert.Equal(AgentOutcome.Accepted, result.Value.Outcome);
        Assert.Equal(Now, result.Value.ClaimedAtUtc);
        Assert.Equal(Now.AddSeconds(1), result.Value.DispatchedAtUtc);
        Assert.Equal(Now.AddSeconds(2), result.Value.CompletedAtUtc);

        var artifact = Assert.Single(result.Value.Artifacts);
        Assert.Equal(ArtifactPurpose.AgentFinalResponse, artifact.Purpose);
        Assert.Equal(128, artifact.ByteLength);
        Assert.False(artifact.Truncated);
        Assert.Equal(ArtifactCaptureOutcome.Captured, artifact.CaptureOutcome);
    }

    [Fact]
    public async Task HandleAsync_returns_the_highest_numbered_critical_review_attempt_when_more_than_one_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Retried review", Now);
        run.Claim(Now);
        var firstAttempt = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(1));
        var secondAttempt = ClaimCriticalReviewAttempt(run.Id, 2, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, secondAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(secondAttempt.Id, result.Value.AttemptId);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(AttemptStatus.Running, result.Value.Status);
        Assert.Null(result.Value.Outcome);
    }

    [Fact]
    public async Task HandleAsync_ignores_a_codex_planning_attempt_on_the_same_run()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Mixed agent roles", Now);
        run.Claim(Now);
        var reviewAttempt = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        // A later, higher-numbered Codex planning attempt on the same run must never be mistaken
        // for the Claude critical-review attempt this query reports on. The run-wide "one
        // Running attempt" invariant forbids two attempts Running at once regardless of kind, so
        // this one is completed to keep the seed legal while still proving the query never
        // mistakes it for the critical-review attempt.
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now.AddSeconds(5));
        planningAttempt.MarkAgentDispatched(Now.AddSeconds(6));
        planningAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(7));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(reviewAttempt, planningAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(reviewAttempt.Id, result.Value.AttemptId);
    }
}
