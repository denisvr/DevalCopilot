using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the verification-diagnosis claim is admitted only into an Idle workspace. A competing owner refuses it before any
/// Git capture or sealing, and an owner that wins in the window between the early check and the claim transaction's write lock refuses
/// it at the commit seam with nothing consumed.</summary>
public sealed class CreateVerificationDiagnosisAttemptWorkspaceExecutionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var reader = scene.Reader();
        var store = scene.Store;

        var result = await scene.ClaimAsync(reader: reader, store: store);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Calls);
        Assert.Empty(store.SealedAttemptIds);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_wins_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest(CompetingOwner owner)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner),
        };

        var result = await scene.ClaimAsync(faulting, store: store);

        Assert.True(result.IsFailure);
        // A verification execution that appears inside the window also changes the diagnosed evidence, which the claim's existing authority
        // re-read classifies first; an Agent attempt of another run is refused by the exclusion itself.
        Assert.Equal(
            owner == CompetingOwner.Verification ? VerificationDiagnosisEvidence.EvidenceRunningCode : WorkspaceExecutionRaceSupport.CodeOf(owner),
            Assert.Single(result.Errors).Code);
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
    }
}
