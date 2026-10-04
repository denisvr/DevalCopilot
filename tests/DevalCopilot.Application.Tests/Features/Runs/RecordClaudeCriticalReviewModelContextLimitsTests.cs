using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class RecordClaudeCriticalReviewResultCommandHandlerTests
{
    private async Task<(Guid RunId, Guid AttemptId)> SeedDispatchedReviewAsync()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt, inputMessage) = CreateClaimedCriticalReviewAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (run.Id, attempt.Id);
    }

    private async Task<Attempt> ReadAsync(Guid attemptId)
    {
        await using var verification = fixture.CreateContext();
        return await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
    }

    [Fact]
    public async Task HandleAsync_records_the_limits_atomically_with_a_semantic_success_and_independently_of_the_usage()
    {
        var (runId, attemptId) = await SeedDispatchedReviewAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, attemptId, AgentOutcome.Accepted, Fingerprint, NoArtifacts, AcceptanceReview(), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit, ModelContextLimits: TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await ReadAsync(attemptId);
        Assert.Equal(AttemptStatus.Completed, persisted.Status);
        Assert.Equal(AgentOutcome.Accepted, persisted.AgentOutcome);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
    }

    [Theory]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    public async Task HandleAsync_records_the_limits_for_an_unsuccessful_outcome_beside_the_usage(AgentOutcome outcome)
    {
        var (runId, attemptId) = await SeedDispatchedReviewAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, attemptId, outcome, null, NoArtifacts, null, null,
                ProcessEvidence: outcome == AgentOutcome.InvalidStructuredOutput ? TestProcessEvidence.ReportedCleanExit : null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await ReadAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persisted.Status);
        Assert.Equal(outcome, persisted.AgentOutcome);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_records_no_limits_when_none_were_reported_and_never_invents_them()
    {
        var (runId, attemptId) = await SeedDispatchedReviewAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, attemptId, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null, TokenUsage: TestTokenUsage.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var persisted = await ReadAsync(attemptId);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_rejects_an_unproven_limits_source_before_any_mutation()
    {
        var (runId, attemptId) = await SeedDispatchedReviewAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, attemptId, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: TestModelContextLimits.Unproven),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == attemptId));
    }

    [Theory]
    [MemberData(nameof(AgentModelContextLimitsBoundaryTests.MalformedValues), MemberType = typeof(AgentModelContextLimitsBoundaryTests))]
    public async Task HandleAsync_refuses_a_malformed_limits_value_with_the_fixed_error_and_leaves_the_attempt_unchanged(
        AgentModelContextLimits malformed)
    {
        var (runId, attemptId) = await SeedDispatchedReviewAsync();
        await using var dbContext = fixture.CreateContext();

        var result = await new RecordClaudeCriticalReviewResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, attemptId, AgentOutcome.ProviderInvocationFailed, null, NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: malformed),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == attemptId));
    }
}
