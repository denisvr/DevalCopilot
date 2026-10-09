using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the Codex challenge-resolution claim. Its guards are
/// compare-and-update statements inside the short claim transaction and none of them reads the lifecycle, so the in-transaction
/// lifecycle read is what stops it.</summary>
public sealed partial class CreateChallengeResolutionAttemptCommandHandlerTests
{
    private async Task<(Guid RunId, Guid ReviewAttemptId)> SeedManualResolutionScenarioAsync()
    {
        await using var seed = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(seed);
        var (_, _, reviewAttempt, _) = SeedChallengedReview(seed, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
        await seed.SaveChangesAsync(CancellationToken.None);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
        return (run.Id, reviewAttempt.Id);
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest()
    {
        var (runId, reviewAttemptId) = await SeedManualResolutionScenarioAsync();
        await using var innerContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, runId)).IsSuccess),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentRunLifecycle.NotRunningCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId && attempt.AgentRole == AgentRole.Resolver));
        Assert.Empty(verify.AttemptInputMessages.Where(message => verify.Attempts.Any(
            attempt => attempt.Id == message.AttemptId && attempt.AgentRole == AgentRole.Resolver)));
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == runId && artifact.Purpose == ArtifactPurpose.AgentContextManifest));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(candidate => candidate.Id == runId).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned));
    }

    [Fact]
    public async Task A_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (runId, reviewAttemptId) = await SeedManualResolutionScenarioAsync();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, runId, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateChallengeResolutionAttemptCommandHandler(
                    claimContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(),
                    new FixedTimeProvider(Now), DurabilityProbe)
                .HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, runId, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.Resolver && a.Status == AttemptStatus.Running));
    }
}
