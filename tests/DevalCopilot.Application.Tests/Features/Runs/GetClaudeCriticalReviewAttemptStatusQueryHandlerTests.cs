using DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
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

    private static (Attempt Attempt, AttemptInputMessage InputMessage) ClaimCriticalReviewAttempt(
        Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc, Guid? inputCollaborationMessageId = null)
    {
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, inputCollaborationMessageId ?? Guid.NewGuid(), sequence: 0);
        return (attempt, inputMessage);
    }

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
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
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
        var (attempt, inputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now, reviewedProposalId);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
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
        Assert.Equal("plan", result.Value.ConfiguredPermissionMode);
        Assert.Equal("Disabled", result.Value.ConfiguredSessionPersistence);
        Assert.Equal("None", result.Value.ConfiguredPermissionPrompts);
        Assert.Equal("Ineligible", result.Value.ConfiguredResumeEligibility);
        Assert.Equal("None", result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_permission_mode_for_a_valid_historical_nullable_assignment()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Historical critical review assignment", Now);
        run.Claim(Now);
        var (attempt, inputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE attempts SET AgentPermissionProfile = NULL, AgentAdapterContractVersion = NULL
            WHERE Id = {attempt.Id}
            """);

        await using var readContext = fixture.CreateContext();
        var result = await new GetClaudeCriticalReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_permission_mode_for_a_mismatched_adapter_contract_version()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Mismatched critical review adapter contract", Now);
        run.Claim(Now);
        var (attempt, inputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE attempts SET AgentAdapterContractVersion = 'claude-critical-review-v2' WHERE Id = {attempt.Id}
            """);

        await using var readContext = fixture.CreateContext();
        var result = await new GetClaudeCriticalReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_permission_mode_for_a_mismatched_response_contract()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Mismatched critical review response contract", Now);
        run.Claim(Now);
        var (attempt, inputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        // A valid, defined AgentResponseContract that is not CriticalReview — everything else
        // about this attempt's persisted assignment (role, provider, permission profile, adapter
        // contract version) otherwise remains exactly coherent, proving the response-contract
        // check alone withholds all five configured facts.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE attempts SET AgentResponseContract = 'ChallengeResolution' WHERE Id = {attempt.Id}
            """);

        await using var readContext = fixture.CreateContext();
        var result = await new GetClaudeCriticalReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_fails_closed_for_an_unparseable_persisted_permission_profile_without_mutating_it()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Corrupt critical review permission profile", Now);
        run.Claim(Now);
        var (attempt, inputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sentinel = "InvalidPermissionSentinel";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentPermissionProfile = {sentinel} WHERE Id = {attempt.Id}");

        await using var readContext = fixture.CreateContext();
        var result = await new GetClaudeCriticalReviewAttemptStatusQueryHandler(readContext)
            .HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(sentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = await readContext.Database
            .SqlQueryRaw<string>("SELECT AgentPermissionProfile AS Value FROM attempts WHERE Id = {0}", attempt.Id)
            .SingleAsync();
        Assert.Equal(sentinel, persistedValue);
    }

    [Fact]
    public async Task HandleAsync_returns_the_highest_numbered_critical_review_attempt_when_more_than_one_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Retried review", Now);
        run.Claim(Now);
        var (firstAttempt, firstInputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(1));
        var (secondAttempt, secondInputMessage) = ClaimCriticalReviewAttempt(run.Id, 2, Now.AddSeconds(2));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, secondAttempt);
        dbContext.AttemptInputMessages.AddRange(firstInputMessage, secondInputMessage);
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
        var (reviewAttempt, reviewInputMessage) = ClaimCriticalReviewAttempt(run.Id, 1, Now);
        // A later, higher-numbered Codex planning attempt on the same run must never be mistaken
        // for the Claude critical-review attempt this query reports on. The run-wide "one
        // Running attempt" invariant forbids two attempts Running at once regardless of kind, so
        // this one is completed to keep the seed legal while still proving the query never
        // mistakes it for the critical-review attempt.
        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now.AddSeconds(5), 2);
        planningAttempt.MarkAgentDispatched(Now.AddSeconds(6));
        planningAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(7));
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(reviewAttempt, planningAttempt);
        dbContext.AttemptInputMessages.Add(reviewInputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeCriticalReviewAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeCriticalReviewAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(reviewAttempt.Id, result.Value.AttemptId);
    }
}
