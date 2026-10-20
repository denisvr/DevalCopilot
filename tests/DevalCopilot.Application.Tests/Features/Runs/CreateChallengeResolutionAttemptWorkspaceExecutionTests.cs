using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the Codex challenge-resolution claim is admitted only into an Idle workspace. A competing owner refuses it before
/// any Git capture or sealing, and an owner that wins in the window between the early check and the claim transaction's write lock
/// refuses it at the commit seam with nothing consumed.</summary>
public sealed partial class CreateChallengeResolutionAttemptCommandHandlerTests
{
    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner)
    {
        var (runId, reviewAttemptId) = await SeedManualResolutionScenarioAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var captures = 0;
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            handlerContext,
            new RaceInjectingEvidenceReader(
                new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
                _ =>
                {
                    captures++;
                    return Task.CompletedTask;
                }),
            artifactStore,
            new FixedTimeProvider(Now),
            DurabilityProbe);

        var result = await handler.HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_wins_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest(CompetingOwner owner)
    {
        var (runId, reviewAttemptId) = await SeedManualResolutionScenarioAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        await using var innerContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateChallengeResolutionAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }
}
