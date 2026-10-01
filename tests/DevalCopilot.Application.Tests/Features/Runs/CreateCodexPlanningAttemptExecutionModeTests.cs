using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_a_planning_attempt_normally()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claimRun: false);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(RunLifecycle.Running, (await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == run.Id)).Lifecycle);
        Assert.Equal(1, await verify.Attempts.CountAsync(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent));
    }

    // A change committed by another transaction strictly between the claim external work and its own transaction:
    // the tracked Run still says ManualAgent, so only the in-transaction confirmation can refuse it.
    [Fact]
    public async Task A_mode_change_before_the_claim_transaction_is_refused_without_persisting_or_leaving_a_manifest()
    {
        await using var innerContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(innerContext, claimRun: false);
        await RunExecutionModeTestSupport.SetStoredModeAsync(innerContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = _ => RunExecutionModeTestSupport.SetStoredModeAsync(
                _fixture, run.Id, (int)RunExecutionMode.Simulated),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateCodexPlanningAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal((int)RunExecutionMode.Simulated, await RunExecutionModeTestSupport.ReadStoredModeAsync(_fixture, run.Id));
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == run.Id));
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == run.Id));
        Assert.Equal(RunLifecycle.Created, verify.Runs.AsNoTracking().Single(candidate => candidate.Id == run.Id).Lifecycle);
    }
}
