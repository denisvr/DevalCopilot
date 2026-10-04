using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class RecordImplementationResultCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_records_the_limits_atomically_with_an_Implemented_outcome_and_independently_of_the_usage()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, _, attempt) = await SeedAndDispatchAsync(dbContext);

        var result = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], NoArtifacts, Report(["src/Foo.cs"]), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit, ModelContextLimits: TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.Implemented, result.Value.Outcome);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Completed, persisted.Status);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_records_the_limits_for_a_provider_failure_beside_the_usage()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, _, attempt) = await SeedAndDispatchAsync(dbContext);

        var result = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, StartingFingerprint, [], NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: TestModelContextLimits.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, result.Value.Outcome);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Failed, persisted.Status);
        Assert.Equal(TestModelContextLimits.Snapshot, persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_records_no_limits_when_none_were_reported_and_never_invents_them()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, _, attempt) = await SeedAndDispatchAsync(dbContext);

        var result = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, StartingFingerprint, [], NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Equal(TestTokenUsage.Evidence, persisted.GetAgentTokenUsageEvidence());
    }

    [Fact]
    public async Task HandleAsync_rejects_an_unproven_limits_source_before_any_mutation()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var result = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: TestModelContextLimits.Unproven),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == attempt.Id));
        Assert.Equal(WorkspaceStatus.Ready, (await verification.GitWorkspaces.AsNoTracking().SingleAsync(item => item.Id == workspace.Id)).Status);
    }

    [Theory]
    [MemberData(nameof(AgentModelContextLimitsBoundaryTests.MalformedValues), MemberType = typeof(AgentModelContextLimitsBoundaryTests))]
    public async Task HandleAsync_refuses_a_malformed_limits_value_with_the_fixed_error_and_leaves_the_attempt_unchanged(
        AgentModelContextLimits malformed)
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var result = await new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, ChangedFingerprint,
                [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], NoArtifacts, null, null,
                TokenUsage: TestTokenUsage.Reported, ModelContextLimits: malformed),
            CancellationToken.None);

        Assert.Equal(AgentModelContextLimitsRecording.InvalidEvidenceCode, Assert.Single(result.Errors).Code);
        await using var verification = fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.CompletedAtUtc);
        Assert.Null(persisted.AgentModelContextLimitsSnapshot);
        Assert.Null(persisted.GetAgentTokenUsageEvidence());
        Assert.Empty(verification.Events.Where(runEvent => runEvent.AttemptId == attempt.Id));
        Assert.Equal(WorkspaceStatus.Ready, (await verification.GitWorkspaces.AsNoTracking().SingleAsync(item => item.Id == workspace.Id)).Status);
    }
}
