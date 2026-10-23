using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The one provider-neutral statement of the admission history (ADR-0063): the run, its sequence, the bounded admission ledger and every
/// historical fact its proof needs (each linked step, the pinned workspace and initial checkpoint, each source checkpoint, each report
/// message, each pinned recipe, and bounded counts of the sequence's recipes and steps) read together as left joins, so the whole answer
/// comes from one consistent database snapshot. Joins read the persisted facts by identity only; no latest, current or eligibility row is
/// selected. The sequences are bounded to two and the ledger to <see cref="AdmissionBound"/> before any join, and a final bound caps the
/// result, so damaged storage never yields an unbounded set. Like the other inspection projections, every stored enum is classified by
/// the database, every number is an exact integer in its range, and every text is bounded by its stored bytes before it is converted.
/// </summary>
internal static class LocalExecutionAdmissionHistoryStatement
{
    /// <summary>The most admission rows read: the largest legitimate ledger plus one, so a reachable extra row proves damaged storage.</summary>
    public const int AdmissionBound = LocalExecutionInspectionProjection.StepBound;

    /// <summary>The most rows the statement returns: a legitimate ledger never exceeds the absolute maximum number of steps.</summary>
    public const int RowBound = AdmissionBound + 1;

    private const int Int32Maximum = LocalExecutionInspectionProjection.Int32Maximum;
    private const long Int64Maximum = long.MaxValue;

    public static IQueryable<LocalExecutionAdmissionHistoryRow> For(IDevalCopilotDbContext dbContext, Guid runId)
    {
        var sequences = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.RunId == runId)
            .OrderBy(candidate => candidate.Id)
            .Take(2);
        var admissions = dbContext.LocalExecutionAdmissions.AsNoTracking()
            .Where(candidate => dbContext.LocalExecutionSequences.AsNoTracking()
                .Any(owner => owner.Id == candidate.SequenceId && owner.RunId == runId))
            .OrderBy(candidate => candidate.SequenceId)
            .ThenBy(candidate => candidate.Ordinal)
            .Take(AdmissionBound);

        return (
            from run in dbContext.Runs.AsNoTracking()
            where run.Id == runId
            join sequence in sequences on run.Id equals sequence.RunId into sequenceGroup
            from sequence in sequenceGroup.DefaultIfEmpty()
            join admission in admissions on (Guid?)sequence.Id equals (Guid?)admission.SequenceId into admissionGroup
            from admission in admissionGroup.DefaultIfEmpty()
            join step in dbContext.LocalExecutionSequenceSteps.AsNoTracking() on admission.StepId equals (Guid?)step.Id into stepGroup
            from step in stepGroup.DefaultIfEmpty()
            join workspace in dbContext.GitWorkspaces.AsNoTracking()
                on (Guid?)sequence.WorkspaceId equals (Guid?)workspace.Id into workspaceGroup
            from workspace in workspaceGroup.DefaultIfEmpty()
            join initial in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)sequence.CheckpointId equals (Guid?)initial.Id into initialGroup
            from initial in initialGroup.DefaultIfEmpty()
            join source in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)admission.SourceCheckpointId equals (Guid?)source.Id into sourceGroup
            from source in sourceGroup.DefaultIfEmpty()
            join report in dbContext.CollaborationMessages.AsNoTracking()
                on admission.ReportMessageId equals (Guid?)report.Id into reportGroup
            from report in reportGroup.DefaultIfEmpty()
            join recipe in dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                on new { Sequence = (Guid?)sequence.Id, Position = admission.RecipeOrdinal }
                equals new { Sequence = (Guid?)recipe.SequenceId, Position = (int?)recipe.Ordinal } into recipeGroup
            from recipe in recipeGroup.DefaultIfEmpty()
            orderby admission.Ordinal
            select new LocalExecutionAdmissionHistoryRow(
                new LocalExecutionAdmissionHistoryRoot(
                    run.Id,
                    run.ProjectId,
                    ((byte[])(object)EF.Property<string>(run, Run.ExecutionModeStorageProperty)).Length
                        <= LocalExecutionInspectionProjection.ModeByteBound
                        ? EF.Property<string>(run, Run.ExecutionModeStorageProperty)
                        : null,
                    (Guid?)sequence.Id,
                    (Guid?)sequence.RunId,
                    (Guid?)sequence.ProjectId,
                    (Guid?)sequence.WorkspaceId,
                    (Guid?)sequence.CheckpointId,
                    ((byte[])(object)sequence.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                        ? sequence.FingerprintSha256
                        : null,
                    sequence.State == LocalExecutionSequenceState.Armed ? 1
                        : sequence.State == LocalExecutionSequenceState.PreparingStep ? 2
                        : sequence.State == LocalExecutionSequenceState.AwaitingStepResult ? 3
                        : sequence.State == LocalExecutionSequenceState.Stopping ? 4
                        : sequence.State == LocalExecutionSequenceState.ReadyForHumanApproval ? 5
                        : sequence.State == LocalExecutionSequenceState.HumanEscalation ? 6
                        : sequence.State == LocalExecutionSequenceState.Stopped ? 7
                        : sequence.State == LocalExecutionSequenceState.NeedsAttention ? 8
                        : sequence.State == LocalExecutionSequenceState.Interrupted ? 9
                        : sequence.State == LocalExecutionSequenceState.WaitingForAdmission ? 10
                        : 0,
                    ((byte[])(object)sequence.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                        ? sequence.ReasonCode
                        : null,
                    sequence.ReasonCode != null
                        && ((byte[])(object)sequence.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound,
                    sequence.RecipeCount >= LocalExecutionSequencePolicy.MinimumRecipes
                        && sequence.RecipeCount <= LocalExecutionSequencePolicy.MaximumRecipes
                        && sequence.RecipeCount % Int32Maximum == sequence.RecipeCount
                        ? (int?)sequence.RecipeCount
                        : null,
                    sequence.Revision >= 1
                        && sequence.Revision <= LocalExecutionInspectionProjection.MaximumSafeInteger
                        && sequence.Revision % LocalExecutionInspectionProjection.SafeIntegerModulus == sequence.Revision
                        ? (long?)sequence.Revision
                        : null,
                    (DateTimeOffset?)sequence.ArmedAtUtc,
                    (DateTimeOffset?)sequence.AdvancementDeadlineUtc,
                    (DateTimeOffset?)sequence.UpdatedAtUtc,
                    sequence.EndedAtUtc,
                    sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate ? 1
                        : sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission ? 2
                        : 0,
                    (Guid?)workspace.ProjectId,
                    (Guid?)initial.WorkspaceId,
                    ((byte[])(object)initial.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                        ? initial.FingerprintSha256
                        : null,
                    dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                        .Where(counted => counted.SequenceId == sequence.Id)
                        .Take(LocalExecutionInspectionProjection.RecipeBound)
                        .Count(),
                    dbContext.LocalExecutionSequenceSteps.AsNoTracking()
                        .Where(counted => counted.SequenceId == sequence.Id)
                        .Take(LocalExecutionInspectionProjection.StepBound)
                        .Count()),
                new LocalExecutionAdmissionHistoryAdmission(
                    (Guid?)admission.Id,
                    (Guid?)admission.SequenceId,
                    (admission.AdmissionNumber >= 1 && admission.AdmissionNumber < Int64Maximum
                        && admission.AdmissionNumber % Int64Maximum == admission.AdmissionNumber)
                        || admission.AdmissionNumber == Int64Maximum
                        ? (long?)admission.AdmissionNumber
                        : null,
                    admission.Ordinal >= 0
                        && admission.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                        && admission.Ordinal % Int32Maximum == admission.Ordinal
                        ? (int?)admission.Ordinal
                        : null,
                    admission.Kind == LocalExecutionStepKind.Implementation ? 1
                        : admission.Kind == LocalExecutionStepKind.Verification ? 2
                        : admission.Kind == LocalExecutionStepKind.CodeReview ? 3
                        : admission.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                        : admission.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                        : admission.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                        : 0,
                    admission.PassNumber >= 0
                        && admission.PassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                        && admission.PassNumber % Int32Maximum == admission.PassNumber
                        ? (int?)admission.PassNumber
                        : null,
                    admission.RecipeOrdinal >= 0
                        && admission.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                        && admission.RecipeOrdinal % Int32Maximum == admission.RecipeOrdinal
                        ? admission.RecipeOrdinal
                        : null,
                    admission.RecipeOrdinal != null,
                    admission.PredecessorStepId,
                    (Guid?)admission.SourceCheckpointId,
                    ((byte[])(object)admission.SourceFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                        ? admission.SourceFingerprintSha256
                        : null,
                    admission.ReportMessageId,
                    admission.SubjectAttemptId,
                    admission.State == LocalExecutionAdmissionState.Waiting ? 1
                        : admission.State == LocalExecutionAdmissionState.Reserved ? 2
                        : admission.State == LocalExecutionAdmissionState.Consumed ? 3
                        : admission.State == LocalExecutionAdmissionState.Retired ? 4
                        : admission.State == LocalExecutionAdmissionState.Unresolved ? 5
                        : 0,
                    admission.StepId,
                    admission.WaitingReason == LocalExecutionWaitingReason.AwaitingAdmissionCheck ? 1
                        : admission.WaitingReason == LocalExecutionWaitingReason.WaitingForWorkspace ? 2
                        : admission.WaitingReason == LocalExecutionWaitingReason.WaitingForCapacity ? 3
                        : admission.WaitingReason == LocalExecutionWaitingReason.WaitingForEarlierEligibleWork ? 4
                        : admission.WaitingReason == LocalExecutionWaitingReason.WaitingForPreparationCapacity ? 5
                        : admission.WaitingReason == LocalExecutionWaitingReason.Unavailable ? 6
                        : 0,
                    (DateTimeOffset?)admission.WaitingReasonRecordedAtUtc,
                    (DateTimeOffset?)admission.EnqueuedAtUtc,
                    ((byte[])(object)admission.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                        ? admission.ReasonCode
                        : null,
                    admission.ReasonCode != null
                        && ((byte[])(object)admission.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound,
                    (admission.Revision >= 1 && admission.Revision < Int64Maximum && admission.Revision % Int64Maximum == admission.Revision)
                        || admission.Revision == Int64Maximum
                        ? (long?)admission.Revision
                        : null,
                    (DateTimeOffset?)admission.UpdatedAtUtc),
                new LocalExecutionAdmissionHistoryStep(
                    (Guid?)step.Id,
                    (Guid?)step.SequenceId,
                    step.Ordinal >= 0
                        && step.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                        && step.Ordinal % Int32Maximum == step.Ordinal
                        ? (int?)step.Ordinal
                        : null,
                    step.Kind == LocalExecutionStepKind.Implementation ? 1
                        : step.Kind == LocalExecutionStepKind.Verification ? 2
                        : step.Kind == LocalExecutionStepKind.CodeReview ? 3
                        : step.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                        : step.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                        : step.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                        : 0,
                    step.PassNumber >= 0
                        && step.PassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                        && step.PassNumber % Int32Maximum == step.PassNumber
                        ? (int?)step.PassNumber
                        : null,
                    step.RecipeOrdinal >= 0
                        && step.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                        && step.RecipeOrdinal % Int32Maximum == step.RecipeOrdinal
                        ? step.RecipeOrdinal
                        : null,
                    step.RecipeOrdinal != null,
                    step.PredecessorStepId,
                    (Guid?)step.SourceCheckpointId,
                    ((byte[])(object)step.SourceFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                        ? step.SourceFingerprintSha256
                        : null,
                    step.ReportMessageId,
                    step.SubjectAttemptId,
                    step.State == LocalExecutionSequenceStepState.Reserved ? 1
                        : step.State == LocalExecutionSequenceStepState.Claimed ? 2
                        : step.State == LocalExecutionSequenceStepState.Concluded ? 3
                        : step.State == LocalExecutionSequenceStepState.Revoked ? 4
                        : step.State == LocalExecutionSequenceStepState.Unresolved ? 5
                        : 0,
                    (Guid?)step.ReservedOperationId,
                    step.OperationId,
                    step.ClaimedAtUtc != null,
                    step.ResultDeadlineUtc != null,
                    ((byte[])(object)step.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                        ? step.ReasonCode
                        : null,
                    step.ReasonCode != null
                        && ((byte[])(object)step.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound),
                new LocalExecutionAdmissionHistoryReference(
                    (Guid?)source.WorkspaceId,
                    ((byte[])(object)source.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                        ? source.FingerprintSha256
                        : null,
                    (Guid?)report.RunId,
                    report.Type == CollaborationMessageType.ExecutionReport,
                    (Guid?)recipe.VerificationCommandId,
                    ((byte[])(object)recipe.CommandName).Length <= LocalExecutionInspectionProjection.NameByteBound
                        ? recipe.CommandName
                        : null,
                    ((byte[])(object)recipe.CommandName).Length > LocalExecutionInspectionProjection.NameByteBound)))
            .Take(RowBound);
    }
}
