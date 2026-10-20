using DevalCopilot.Domain.Features.Runs;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0058: the diagnosis-origin correction claim is one of at most two recorded potentially mutating owners across the
/// database. A full database refuses it before any Git capture or sealing, an owner that takes the last place before the claim
/// transaction refuses it at the commit seam, and a failed or cancelled final capacity read settles only its own manifest; nothing is
/// consumed (no attempt, input, artifact, escalation or claim number).</summary>
public sealed class CreateDiagnosisCorrectionAttemptGlobalAdmissionTests : IAsyncLifetime
{
    private const string LimitReached = "execution_capacity.limit_reached";
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        return (scene, diagnosis);
    }

    [Fact]
    public async Task A_database_at_capacity_refuses_the_claim_before_any_external_work_and_consumes_nothing()
    {
        var (scene, diagnosis) = await SceneAsync();
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification, OwnerFamily.LocalCommit);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var reader = scene.Reader();
        var store = scene.Store;

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, reader: reader, store: store);

        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        Assert.Equal(0, reader.Calls);
        Assert.Empty(store.SealedAttemptIds);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    public async Task The_last_place_taken_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest(OwnerFamily late)
    {
        var (scene, diagnosis) = await SceneAsync();
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => ExecutionCapacitySeed.AddAsync(_fixture, late),
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store);

        Assert.True(result.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(result.Errors).Code);
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_capacity_read_settles_only_the_operations_own_manifest(FenceReadFault fault)
    {
        var (scene, diagnosis) = await SceneAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = scene.Store;
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CapacityReadFaultInterceptor(fault, cancellation);
        await using var db = _fixture.CreateContext(interceptor);
        var faulting = new FaultInjectingDbContext(db);

        if (fault == FenceReadFault.Cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store, cancellationToken: cancellation.Token));
        }
        else
        {
            var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store);
            Assert.True(result.IsFailure);
            Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        }

        Assert.True(interceptor.Fired);
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
    }
}
