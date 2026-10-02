using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.FaultInjectingDbContext;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Commit and save faults of the diagnosis claim (ADR-0018), mirroring the ordinary code-review claim's transaction-boundary
/// guarantees: the independent durability probe — never the failed call, the claim's own context, or a failed rollback — is
/// the sole authority. Persisted keeps the sealed manifest and succeeds, NotPersisted deletes it and leaves nothing, and
/// Unresolved keeps the file and reports an unresolved persistence failure.
/// </summary>
public sealed class CreateVerificationDiagnosisAttemptTransactionFaultTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<DiagnosisTestScene> SceneAsync() => await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

    private async Task<Devalente.Shared.Results.Result<CreateVerificationDiagnosisAttemptCommandResult>> ClaimAsync(
        DiagnosisTestScene scene, FaultInjectingDbContext faulting, IAttemptDurabilityProbe? probe = null)
    {
        return await scene.ClaimHandler(faulting, probe: probe).HandleAsync(
            new CreateVerificationDiagnosisAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);
    }

    private async Task AssertNothingDurableAsync(DiagnosisTestScene scene, Guid sealedAttemptId)
    {
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.AgentResponseContract == AgentResponseContract.VerificationDiagnosis));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), scene.Store.DeletedSealedFiles);
    }

    private async Task AssertDurableAsync(DiagnosisTestScene scene, Guid attemptId)
    {
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.AgentResponseContract == AgentResponseContract.VerificationDiagnosis);
        Assert.Equal(attemptId, attempt.Id);
        Assert.Single(verify.AttemptInputMessages.Where(i => i.AttemptId == attemptId));
        Assert.Equal(2, verify.AttemptVerificationEvidence.Count(e => e.AttemptId == attemptId));
        Assert.Single(verify.Artifacts.Where(a => a.AttemptId == attemptId));
        Assert.Empty(scene.Store.DeletedSealedFiles);
    }

    // ---- Transaction acquisition -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_transaction_acquisition_failure_fails_closed_and_deletes_the_sealed_manifest()
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(scene, new FaultInjectingDbContext(_fixture.CreateContext()) { ThrowOnBeginTransaction = true });

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    [Fact]
    public async Task Cancellation_during_transaction_acquisition_propagates_and_deletes_the_sealed_manifest()
    {
        var scene = await SceneAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ClaimAsync(scene, new FaultInjectingDbContext(_fixture.CreateContext()) { ThrowCancellationOnBeginTransaction = true }));

        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    // ---- Commit faults with the real durability probe ----------------------------------------------------------------------

    [Theory]
    [InlineData(CommitFailureMode.BeforeCommit)]
    [InlineData(CommitFailureMode.BeforeCommitRollbackAlsoThrows)]
    public async Task A_commit_that_failed_without_persisting_is_cleaned_up_even_when_the_rollback_also_throws(CommitFailureMode mode)
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(scene, new FaultInjectingDbContext(_fixture.CreateContext()) { CommitFailure = mode });

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    [Theory]
    [InlineData(CommitFailureMode.AfterCommit)]
    [InlineData(CommitFailureMode.AfterCommitRollbackAlsoThrows)]
    public async Task A_commit_that_actually_persisted_despite_throwing_is_a_success_and_keeps_the_manifest(CommitFailureMode mode)
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(scene, new FaultInjectingDbContext(_fixture.CreateContext()) { CommitFailure = mode });

        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        await AssertDurableAsync(scene, result.Value.AttemptId);
    }

    [Fact]
    public async Task Cancellation_before_the_commit_propagates_and_cleans_up()
    {
        var scene = await SceneAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => ClaimAsync(
            scene, new FaultInjectingDbContext(_fixture.CreateContext()) { CommitFailure = CommitFailureMode.CancellationBeforeCommit }));

        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    [Fact]
    public async Task Cancellation_after_the_commit_propagates_without_deleting_the_durably_referenced_manifest()
    {
        var scene = await SceneAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => ClaimAsync(
            scene, new FaultInjectingDbContext(_fixture.CreateContext()) { CommitFailure = CommitFailureMode.CancellationAfterCommit }));

        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.AgentResponseContract == AgentResponseContract.VerificationDiagnosis);
        Assert.Empty(scene.Store.DeletedSealedFiles);
        Assert.Equal(2, verify.AttemptVerificationEvidence.Count(e => e.AttemptId == attempt.Id));
    }

    // ---- Save faults -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_save_update_failure_that_applied_nothing_is_classified_and_cleaned_up()
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(
            scene, new FaultInjectingDbContext(_fixture.CreateContext()) { SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave });

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    [Theory]
    [InlineData(SaveChangesFailureMode.CancellationBeforeSave)]
    [InlineData(SaveChangesFailureMode.CancellationAfterSave)]
    public async Task Cancellation_around_the_save_propagates_and_the_rolled_back_claim_is_cleaned_up(SaveChangesFailureMode mode)
    {
        var scene = await SceneAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ClaimAsync(scene, new FaultInjectingDbContext(_fixture.CreateContext()) { SaveChangesFailure = mode }));

        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    // ---- The probe is the sole authority -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_probe_that_says_persisted_after_a_failed_save_is_trusted_and_keeps_the_manifest()
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(
            scene,
            new FaultInjectingDbContext(_fixture.CreateContext()) { SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave },
            new FixedDurabilityProbe(AttemptDurabilityCheckResult.Persisted));

        Assert.True(result.IsSuccess, result.IsFailure ? Assert.Single(result.Errors).Code : null);
        Assert.Empty(scene.Store.DeletedSealedFiles);
    }

    [Theory]
    [InlineData(SaveChangesFailureMode.UpdateExceptionBeforeSave, CommitFailureMode.None)]
    [InlineData(SaveChangesFailureMode.None, CommitFailureMode.BeforeCommit)]
    public async Task An_unresolved_probe_keeps_the_manifest_and_reports_an_unresolved_persistence_failure(
        SaveChangesFailureMode save, CommitFailureMode commit)
    {
        var scene = await SceneAsync();
        var probe = new FixedDurabilityProbe(AttemptDurabilityCheckResult.Unresolved);

        var result = await ClaimAsync(
            scene, new FaultInjectingDbContext(_fixture.CreateContext()) { SaveChangesFailure = save, CommitFailure = commit }, probe);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_unresolved", Assert.Single(result.Errors).Code);
        Assert.Empty(scene.Store.DeletedSealedFiles);
        Assert.Equal(1, probe.Calls);
    }

    [Theory]
    [InlineData(SaveChangesFailureMode.UpdateExceptionBeforeSave, CommitFailureMode.None)]
    [InlineData(SaveChangesFailureMode.None, CommitFailureMode.BeforeCommit)]
    public async Task A_not_persisted_probe_deletes_the_manifest(SaveChangesFailureMode save, CommitFailureMode commit)
    {
        var scene = await SceneAsync();

        var result = await ClaimAsync(
            scene, new FaultInjectingDbContext(_fixture.CreateContext()) { SaveChangesFailure = save, CommitFailure = commit },
            new FixedDurabilityProbe(AttemptDurabilityCheckResult.NotPersisted));

        Assert.True(result.IsFailure);
        await AssertNothingDurableAsync(scene, Assert.Single(scene.Store.SealedAttemptIds));
    }

    [Fact]
    public async Task A_refused_claim_never_asks_the_durability_probe()
    {
        var scene = await SceneAsync();
        var probe = new FixedDurabilityProbe(AttemptDurabilityCheckResult.Persisted);
        await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", scene.Run.Id);

        var result = await scene.ClaimHandler(new FaultInjectingDbContext(_fixture.CreateContext()), probe: probe).HandleAsync(
            new CreateVerificationDiagnosisAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, probe.Calls);
    }
}
