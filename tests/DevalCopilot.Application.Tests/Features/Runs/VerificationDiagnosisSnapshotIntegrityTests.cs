using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The durable snapshot of a verification diagnosis membership (ADR-0018): every diagnosis row carries the versioned digest of
/// the command, execution, and failed-output facts the claim decided against. Each test changes one such fact through an
/// independent writer while every identifier — and therefore the relational membership — stays identical, and proves the claim
/// seam, the dispatch gate, the applicability that gates result recording, and the diagnosis-based correction authority all
/// refuse. A diagnosis row without a valid digest fails closed.
/// </summary>
public sealed class VerificationDiagnosisSnapshotIntegrityTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    // {0} = the failed execution, {1} = the passed command. Every statement keeps each identifier and the Failed/Passed
    // classification coherent, so the change is invisible to an identifier or status comparison.
    public static TheoryData<string> Mutations => new()
    {
        "UPDATE verification_output_artifacts SET ContentHash = 'sha256:tampered' WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardOutput'",
        "UPDATE verification_output_artifacts SET ContentHash = 'sha256:tampered' WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardError'",
        "UPDATE verification_output_artifacts SET ByteLength = ByteLength + 1 WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardOutput'",
        "UPDATE verification_output_artifacts SET RelativeStoragePath = 'elsewhere/moved.sealed' WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardError'",
        "UPDATE verification_output_artifacts SET Truncated = CASE WHEN Truncated = 1 THEN 0 ELSE 1 END WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardOutput'",
        "UPDATE verification_output_artifacts SET CaptureOutcome = 'RecoveredAfterHostInterruption' WHERE VerificationExecutionId = '{0}' AND Purpose = 'StandardError'",
        "UPDATE verification_executions SET ExitCode = ExitCode + 1 WHERE Id = '{0}'",
        "UPDATE verification_executions SET WorkspacePath = 'C:/another/workspace' WHERE Id = '{0}'",
        "UPDATE verification_executions SET TimeoutSeconds = TimeoutSeconds + 1 WHERE Id = '{0}'",
        "UPDATE verification_commands SET Name = 'renamed command' WHERE Id = '{1}'",
        "UPDATE verification_commands SET TimeoutSeconds = TimeoutSeconds + 1 WHERE Id = '{1}'",
    };

    private async Task<(Guid FailedExecutionId, Guid PassedCommandId)> IdsAsync()
    {
        await using var db = _fixture.CreateContext();
        var failed = await db.VerificationExecutions.AsNoTracking().SingleAsync(e => e.Status == VerificationExecutionStatus.Failed);
        var passed = await db.VerificationExecutions.AsNoTracking().SingleAsync(e => e.Status == VerificationExecutionStatus.Passed);
        return (failed.Id, passed.VerificationCommandId);
    }

    private async Task ApplyAsync(DiagnosisTestScene scene, string mutation)
    {
        var (failedExecutionId, passedCommandId) = await IdsAsync();
        await scene.SqlAsync(mutation.Replace("{0}", failedExecutionId.ToString().ToUpperInvariant()).Replace("{1}", passedCommandId.ToString().ToUpperInvariant()));
    }

    private async Task<VerificationDiagnosisApplicability.Verdict> VerdictAsync(Attempt attempt)
    {
        await using var db = _fixture.CreateContext();
        return await VerificationDiagnosisApplicability.EvaluateAsync(db, await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id), CancellationToken.None);
    }

    [Fact]
    public async Task Every_claimed_membership_carries_the_valid_digest_of_its_own_current_snapshot()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);

        await using var db = _fixture.CreateContext();
        var rows = await db.AttemptVerificationEvidence.Where(e => e.AttemptId == claim.Value.AttemptId).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(AttemptVerificationEvidence.IsValidSnapshotDigest(row.SnapshotSha256)));
        Assert.Equal(2, rows.Select(row => row.SnapshotSha256).Distinct().Count());
        for (var index = 0; index < rows.Count; index++)
        {
            Assert.Equal(await SnapshotAsync(db, rows[index].VerificationCommandId, rows[index].VerificationExecutionId), rows[index].SnapshotSha256);
        }
    }

    [Fact]
    public void Ordinary_review_memberships_keep_a_null_digest_and_a_malformed_digest_is_never_valid()
    {
        var ordinary = AttemptVerificationEvidence.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0);

        Assert.Null(ordinary.SnapshotSha256);
        Assert.False(AttemptVerificationEvidence.IsValidSnapshotDigest(null));
        Assert.False(AttemptVerificationEvidence.IsValidSnapshotDigest(new string('A', 64)));
        Assert.False(AttemptVerificationEvidence.IsValidSnapshotDigest(new string('a', 63)));
        Assert.True(AttemptVerificationEvidence.IsValidSnapshotDigest(new string('a', 64)));
        Assert.Throws<ArgumentException>(() => AttemptVerificationEvidence.RecordDiagnosisSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, "not-a-digest"));
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task A_changed_fact_with_identical_identifiers_is_refused_at_the_claim_seam(string mutation)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db) { BeforeBeginTransaction = _ => ApplyAsync(scene, mutation) };

        var result = await scene.ClaimAsync(faulting, store: store);

        Assert.True(result.IsFailure, "The claim must be refused at the seam.");
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == sealedAttemptId));
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task A_changed_fact_after_the_claim_is_refused_at_dispatch_and_by_the_applicability_that_gates_recording(string mutation)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        await using var read = _fixture.CreateContext();
        var attempt = await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId);
        Assert.Equal(VerificationDiagnosisApplicability.Verdict.Applicable, await VerdictAsync(attempt));
        await ApplyAsync(scene, mutation);

        await using var db = _fixture.CreateContext();
        var dispatched = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, attempt.Id), CancellationToken.None);

        Assert.True(dispatched.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.VerificationEvidenceChangedCode, Code(dispatched));
        Assert.Equal(VerificationDiagnosisApplicability.Verdict.VerificationEvidenceChanged, await VerdictAsync(attempt));
        await using var verify = _fixture.CreateContext();
        Assert.False((await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id)).AgentDispatchedAtUtc.HasValue);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task A_changed_fact_withdraws_the_correction_authority_of_a_recorded_diagnosis(string mutation)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount: 2);
        await using (var before = _fixture.CreateContext())
        {
            var status = (await new GetVerificationDiagnosisStatusQueryHandler(before).HandleAsync(
                new GetVerificationDiagnosisStatusQuery(scene.Run.Id), CancellationToken.None)).Value;
            Assert.True(status.CorrectionApplicable);
        }

        await ApplyAsync(scene, mutation);

        await using var after = _fixture.CreateContext();
        var changed = (await new GetVerificationDiagnosisStatusQueryHandler(after).HandleAsync(
            new GetVerificationDiagnosisStatusQuery(scene.Run.Id), CancellationToken.None)).Value;
        Assert.False(changed.CorrectionApplicable);
        var correction = await scene.ClaimCorrectionAsync(changed.AttemptId!.Value);
        Assert.True(correction.IsFailure);
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(correction));
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("'not-a-digest'")]
    [InlineData("'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'")]
    public async Task A_diagnosis_membership_without_a_valid_digest_fails_closed(string digest)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        await scene.SqlAsync(
            $"UPDATE attempt_verification_evidence SET SnapshotSha256 = {digest} WHERE AttemptId = '{claim.Value.AttemptId.ToString().ToUpperInvariant()}' AND Sequence = 1");

        await using var db = _fixture.CreateContext();
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId);
        var dispatched = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, attempt.Id), CancellationToken.None);

        Assert.Equal(VerificationDiagnosisApplicability.Verdict.VerificationEvidenceChanged, await VerdictAsync(attempt));
        Assert.True(dispatched.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.VerificationEvidenceChangedCode, Code(dispatched));
    }
}
