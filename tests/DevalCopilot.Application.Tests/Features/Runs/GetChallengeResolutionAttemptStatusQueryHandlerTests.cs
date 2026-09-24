using DevalCopilot.Application.Features.Runs.Queries.GetChallengeResolutionAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Mirrors <c>GetClaudeCriticalReviewAttemptStatusQueryHandlerTests</c> exactly, adapted
/// for the Resolver filter and the additional ordered original-Proposal/Challenge-id projection
/// fields this status feed carries.</summary>
public sealed class GetChallengeResolutionAttemptStatusQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 19, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Attempt Attempt, AttemptInputMessage OriginalProposal, List<AttemptInputMessage> Challenges) ClaimChallengeResolutionAttempt(
        Guid runId, int attemptNumber, DateTimeOffset claimedAtUtc, Guid originalProposalId, IReadOnlyList<Guid> challengeIds)
    {
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, claimedAtUtc, attemptNumber);
        var originalProposal = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, originalProposalId, sequence: 0);
        var challenges = challengeIds
            .Select((id, index) => AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, id, sequence: index + 1))
            .ToList();
        return (attempt, originalProposal, challenges);
    }

    [Fact]
    public async Task HandleAsync_returns_an_explicit_no_attempt_result_for_a_run_with_no_resolution_attempt_yet()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No attempt yet", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetChallengeResolutionAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetChallengeResolutionAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.AttemptId);
        Assert.Null(result.Value.OriginalProposalMessageId);
        Assert.Empty(result.Value.ChallengeMessageIds);
        Assert.Empty(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_fails_with_a_safe_not_found_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new GetChallengeResolutionAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetChallengeResolutionAttemptStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_returns_the_attempts_bounded_status_ordered_input_ids_and_artifact_metadata()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve challenges", Now);
        run.Claim(Now);
        var originalProposalId = Guid.NewGuid();
        var challengeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (attempt, originalProposal, challenges) = ClaimChallengeResolutionAttempt(run.Id, 1, Now, originalProposalId, challengeIds);
        attempt.MarkAgentDispatched(Now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(originalProposal);
        dbContext.AttemptInputMessages.AddRange(challenges);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetChallengeResolutionAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetChallengeResolutionAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAttempt);
        Assert.Equal(attempt.Id, result.Value.AttemptId);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(originalProposalId, result.Value.OriginalProposalMessageId);
        Assert.Equal(challengeIds, result.Value.ChallengeMessageIds);
        Assert.Equal(AttemptStatus.Completed, result.Value.Status);
        Assert.Equal(AgentOutcome.Resolved, result.Value.Outcome);
        Assert.Single(result.Value.Artifacts);
    }

    [Fact]
    public async Task HandleAsync_returns_the_latest_resolution_attempt_when_more_than_one_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve challenges", Now);
        run.Claim(Now);

        var (firstAttempt, firstProposal, firstChallenges) =
            ClaimChallengeResolutionAttempt(run.Id, 1, Now, Guid.NewGuid(), [Guid.NewGuid()]);
        firstAttempt.MarkAgentDispatched(Now.AddSeconds(1));
        firstAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddSeconds(2));

        var (secondAttempt, secondProposal, secondChallenges) =
            ClaimChallengeResolutionAttempt(run.Id, 2, Now.AddMinutes(1), Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()]);
        secondAttempt.MarkAgentDispatched(Now.AddMinutes(1).AddSeconds(1));
        secondAttempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now.AddMinutes(1).AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.AddRange(firstAttempt, secondAttempt);
        dbContext.AttemptInputMessages.Add(firstProposal);
        dbContext.AttemptInputMessages.AddRange(firstChallenges);
        dbContext.AttemptInputMessages.Add(secondProposal);
        dbContext.AttemptInputMessages.AddRange(secondChallenges);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetChallengeResolutionAttemptStatusQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetChallengeResolutionAttemptStatusQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(secondAttempt.Id, result.Value.AttemptId);
        Assert.Equal(2, result.Value.AttemptNumber);
        Assert.Equal(AgentOutcome.Resolved, result.Value.Outcome);
    }
}
