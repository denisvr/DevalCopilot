using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the two diagnosis claims, the Codex verification
/// diagnosis and the Claude diagnosis-origin correction. Both re-read the lifecycle untracked inside their write-locked claim
/// transaction, after external work, so an abandonment that committed at the seam refuses them and one that lost is refused.</summary>
public sealed class DiagnosisClaimsAbandonmentRaceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<DiagnosisTestScene> ManualSceneAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        await scene.SqlAsync("UPDATE runs SET ExecutionMode = 2");
        return scene;
    }

    private async Task AssertAbandonedWithoutClaimAsync(DiagnosisTestScene scene, Guid sealedAttemptId)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == scene.Run.Id).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == scene.Run.Id && e.EventType == RunEventType.RunAbandoned));
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_diagnosis_transaction_refuses_the_claim_and_removes_its_manifest()
    {
        var scene = await ManualSceneAsync();
        scene.Scene.Detach();
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, scene.Run.Id)).IsSuccess),
        };

        var result = await scene.ClaimAsync(faulting, store: store);

        Assert.True(result.IsFailure);
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await AssertAbandonedWithoutClaimAsync(scene, sealedAttemptId);
    }

    [Fact]
    public async Task A_diagnosis_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var scene = await ManualSceneAsync();
        scene.Scene.Detach();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, scene.Run.Id, async _ =>
        {
            var claim = await scene.ClaimAsync();
            Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, scene.Run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == scene.Run.Id && a.AgentResponseContract == AgentResponseContract.VerificationDiagnosis
            && a.Status == AttemptStatus.Running));
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_correction_transaction_refuses_the_claim_and_removes_its_manifest()
    {
        var scene = await ManualSceneAsync();
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        scene.Scene.Detach();
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, scene.Run.Id)).IsSuccess),
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store);

        Assert.True(result.IsFailure);
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await AssertAbandonedWithoutClaimAsync(scene, sealedAttemptId);
    }

    [Fact]
    public async Task A_correction_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var scene = await ManualSceneAsync();
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        scene.Scene.Detach();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, scene.Run.Id, async _ =>
        {
            var claim = await scene.ClaimCorrectionAsync(diagnosis.Id);
            Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, scene.Run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == scene.Run.Id && a.AgentRole == AgentRole.Implementer
            && a.Status == AttemptStatus.Running && a.AgentRepairSourceAttemptId == null && a.Id != diagnosis.Id));
    }
}
