using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateChallengeResolutionAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_a_resolution_attempt_normally()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext);
        var (_, _, reviewAttempt, _) = SeedChallengedReview(dbContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_mode_change_before_the_claim_transaction_is_refused_without_persisting_or_leaving_a_manifest()
    {
        await using var innerContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(innerContext);
        var (_, _, reviewAttempt, _) = SeedChallengedReview(innerContext, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await innerContext.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredModeAsync(innerContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = _ => RunExecutionModeTestSupport.SetStoredModeAsync(
                _fixture, run.Id, (int)RunExecutionMode.Simulated),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateChallengeResolutionAttemptCommand(run.Id, reviewAttempt.Id), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == run.Id && attempt.AgentRole == AgentRole.Resolver));
        Assert.Empty(verify.AttemptInputMessages.Where(message => verify.Attempts.Any(attempt => attempt.Id == message.AttemptId && attempt.AgentRole == AgentRole.Resolver)));
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == run.Id && artifact.Purpose == ArtifactPurpose.AgentContextManifest));
    }
}
