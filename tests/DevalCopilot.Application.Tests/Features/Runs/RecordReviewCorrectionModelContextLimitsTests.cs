using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class RecordReviewCorrectionResultCommandHandlerTests
{
    private static ValidatedReviewCorrection Correction(Seed seed) => ValidatedReviewCorrection.Create(
        [
            new ValidatedRevisionResponse(seed.FirstFinding.Id, "Applied", "Fixed the null path.", "Updated src/Foo.cs."),
            new ValidatedRevisionResponse(seed.SecondFinding.Id, "Applied", "Added the missing guard.", "Updated src/Bar.cs."),
        ],
        ValidatedImplementationReport.Create(
            "Correction completed.", ["src/Foo.cs"], "Applied the requested fixes.", string.Empty, string.Empty, "Run the focused tests."));

    [Fact]
    public async Task HandleAsync_records_the_limits_atomically_with_a_CorrectionApplied_outcome_and_independently_of_the_usage()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);

        var result = await new RecordReviewCorrectionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, true, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], Correction(seed), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit, ModelContextLimits: TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.Equal(AgentOutcome.CorrectionApplied, result.Value.Outcome);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CorrectionAttempt.Id);
        Assert.Equal(AttemptStatus.Completed, persisted.Status);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_records_the_limits_for_a_provider_failure_beside_the_usage()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);

        var result = await new RecordReviewCorrectionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, false, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], null, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(6)),
                TestTokenUsage.Reported, TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CorrectionAttempt.Id);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persisted.AgentOutcome);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_records_no_limits_when_none_were_reported_and_never_invents_them()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);

        var result = await new RecordReviewCorrectionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, false, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], null, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(6)), TestTokenUsage.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CorrectionAttempt.Id);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_rejects_an_unproven_limits_source_before_any_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);

        var result = await new RecordReviewCorrectionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, false, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], null, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(6)),
                TestTokenUsage.Reported, TestModelContextLimits.Unproven),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CorrectionAttempt.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == seed.CorrectionAttempt.Id));
    }

    [Theory]
    [MemberData(nameof(AgentModelContextLimitsBoundaryTests.MalformedValues), MemberType = typeof(AgentModelContextLimitsBoundaryTests))]
    public async Task HandleAsync_refuses_a_malformed_limits_value_with_the_fixed_error_and_leaves_the_attempt_unchanged(
        AgentModelContextLimits malformed)
    {
        await using var dbContext = fixture.CreateContext();
        var seed = await SeedAsync(dbContext);

        var result = await new RecordReviewCorrectionResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                seed.Run.Id, seed.CorrectionAttempt.Id, false, StartingHead, CorrectionFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], null, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(6)),
                TestTokenUsage.Reported, malformed),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == seed.CorrectionAttempt.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == seed.CorrectionAttempt.Id));
    }
}
