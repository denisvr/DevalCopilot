using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The guarded scalar projection of one admission-history row, shared by the per-run statement (ADR-0063) and the global overview
/// statement (ADR-0064) so both are proved by the same <see cref="LocalExecutionAdmissionHistoryProof"/> over the same facts. Every
/// stored enum is classified by the database, every number is an exact integer in its supported range and every text is bounded by its
/// stored bytes before it is converted. A run that does not exist reads as <see cref="Guid.Empty"/>, which the proof refuses. No
/// settings snapshot, confirmation, plan-input digest, preparation token, path or detail is selected.
/// </summary>
internal static class LocalExecutionAdmissionHistoryProjection
{
    private const int Int32Maximum = LocalExecutionInspectionProjection.Int32Maximum;
    private const long Int64Maximum = long.MaxValue;

    public static IQueryable<LocalExecutionAdmissionHistoryRow> Project(
        this IQueryable<LocalExecutionAdmissionHistoryJoin> joins, IDevalCopilotDbContext dbContext) =>
        joins.Select(RowOf(dbContext));

    /// <summary>The projection itself, exposed as an expression so the global statement can bind it to its own joined shape.</summary>
    public static Expression<Func<LocalExecutionAdmissionHistoryJoin, LocalExecutionAdmissionHistoryRow>> RowOf(
        IDevalCopilotDbContext dbContext) =>
        row => new LocalExecutionAdmissionHistoryRow(
            new LocalExecutionAdmissionHistoryRoot(
                (Guid?)row.Run.Id ?? Guid.Empty,
                (Guid?)row.Run.ProjectId ?? Guid.Empty,
                ((byte[])(object)EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)).Length
                    <= LocalExecutionInspectionProjection.ModeByteBound
                    ? EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)
                    : null,
                (Guid?)row.Sequence.Id,
                (Guid?)row.Sequence.RunId,
                (Guid?)row.Sequence.ProjectId,
                (Guid?)row.Sequence.WorkspaceId,
                (Guid?)row.Sequence.CheckpointId,
                ((byte[])(object)row.Sequence.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Sequence.FingerprintSha256
                    : null,
                row.Sequence.State == LocalExecutionSequenceState.Armed ? 1
                    : row.Sequence.State == LocalExecutionSequenceState.PreparingStep ? 2
                    : row.Sequence.State == LocalExecutionSequenceState.AwaitingStepResult ? 3
                    : row.Sequence.State == LocalExecutionSequenceState.Stopping ? 4
                    : row.Sequence.State == LocalExecutionSequenceState.ReadyForHumanApproval ? 5
                    : row.Sequence.State == LocalExecutionSequenceState.HumanEscalation ? 6
                    : row.Sequence.State == LocalExecutionSequenceState.Stopped ? 7
                    : row.Sequence.State == LocalExecutionSequenceState.NeedsAttention ? 8
                    : row.Sequence.State == LocalExecutionSequenceState.Interrupted ? 9
                    : row.Sequence.State == LocalExecutionSequenceState.WaitingForAdmission ? 10
                    : 0,
                ((byte[])(object)row.Sequence.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                    ? row.Sequence.ReasonCode
                    : null,
                row.Sequence.ReasonCode != null
                    && ((byte[])(object)row.Sequence.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound,
                row.Sequence.RecipeCount >= LocalExecutionSequencePolicy.MinimumRecipes
                    && row.Sequence.RecipeCount <= LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Sequence.RecipeCount % Int32Maximum == row.Sequence.RecipeCount
                    ? (int?)row.Sequence.RecipeCount
                    : null,
                row.Sequence.Revision >= 1
                    && row.Sequence.Revision <= LocalExecutionInspectionProjection.MaximumSafeInteger
                    && row.Sequence.Revision % LocalExecutionInspectionProjection.SafeIntegerModulus == row.Sequence.Revision
                    ? (long?)row.Sequence.Revision
                    : null,
                (DateTimeOffset?)row.Sequence.ArmedAtUtc,
                (DateTimeOffset?)row.Sequence.AdvancementDeadlineUtc,
                (DateTimeOffset?)row.Sequence.UpdatedAtUtc,
                row.Sequence.EndedAtUtc,
                row.Sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate ? 1
                    : row.Sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission ? 2
                    : 0,
                (Guid?)row.Workspace.ProjectId,
                (Guid?)row.Initial.WorkspaceId,
                ((byte[])(object)row.Initial.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Initial.FingerprintSha256
                    : null,
                dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                    .Where(counted => counted.SequenceId == row.Sequence.Id)
                    .Take(LocalExecutionInspectionProjection.RecipeBound)
                    .Count(),
                dbContext.LocalExecutionSequenceSteps.AsNoTracking()
                    .Where(counted => counted.SequenceId == row.Sequence.Id)
                    .Take(LocalExecutionInspectionProjection.StepBound)
                    .Count()),
            new LocalExecutionAdmissionHistoryAdmission(
                (Guid?)row.Admission.Id,
                (Guid?)row.Admission.SequenceId,
                (row.Admission.AdmissionNumber >= 1 && row.Admission.AdmissionNumber < Int64Maximum
                    && row.Admission.AdmissionNumber % Int64Maximum == row.Admission.AdmissionNumber)
                    || row.Admission.AdmissionNumber == Int64Maximum
                    ? (long?)row.Admission.AdmissionNumber
                    : null,
                row.Admission.Ordinal >= 0
                    && row.Admission.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                    && row.Admission.Ordinal % Int32Maximum == row.Admission.Ordinal
                    ? (int?)row.Admission.Ordinal
                    : null,
                row.Admission.Kind == LocalExecutionStepKind.Implementation ? 1
                    : row.Admission.Kind == LocalExecutionStepKind.Verification ? 2
                    : row.Admission.Kind == LocalExecutionStepKind.CodeReview ? 3
                    : row.Admission.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                    : row.Admission.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                    : row.Admission.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                    : 0,
                row.Admission.PassNumber >= 0
                    && row.Admission.PassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                    && row.Admission.PassNumber % Int32Maximum == row.Admission.PassNumber
                    ? (int?)row.Admission.PassNumber
                    : null,
                row.Admission.RecipeOrdinal >= 0
                    && row.Admission.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Admission.RecipeOrdinal % Int32Maximum == row.Admission.RecipeOrdinal
                    ? row.Admission.RecipeOrdinal
                    : null,
                row.Admission.RecipeOrdinal != null,
                row.Admission.PredecessorStepId,
                (Guid?)row.Admission.SourceCheckpointId,
                ((byte[])(object)row.Admission.SourceFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Admission.SourceFingerprintSha256
                    : null,
                row.Admission.ReportMessageId,
                row.Admission.SubjectAttemptId,
                row.Admission.State == LocalExecutionAdmissionState.Waiting ? 1
                    : row.Admission.State == LocalExecutionAdmissionState.Reserved ? 2
                    : row.Admission.State == LocalExecutionAdmissionState.Consumed ? 3
                    : row.Admission.State == LocalExecutionAdmissionState.Retired ? 4
                    : row.Admission.State == LocalExecutionAdmissionState.Unresolved ? 5
                    : 0,
                row.Admission.StepId,
                row.Admission.WaitingReason == LocalExecutionWaitingReason.AwaitingAdmissionCheck ? 1
                    : row.Admission.WaitingReason == LocalExecutionWaitingReason.WaitingForWorkspace ? 2
                    : row.Admission.WaitingReason == LocalExecutionWaitingReason.WaitingForCapacity ? 3
                    : row.Admission.WaitingReason == LocalExecutionWaitingReason.WaitingForEarlierEligibleWork ? 4
                    : row.Admission.WaitingReason == LocalExecutionWaitingReason.WaitingForPreparationCapacity ? 5
                    : row.Admission.WaitingReason == LocalExecutionWaitingReason.Unavailable ? 6
                    : 0,
                (DateTimeOffset?)row.Admission.WaitingReasonRecordedAtUtc,
                (DateTimeOffset?)row.Admission.EnqueuedAtUtc,
                ((byte[])(object)row.Admission.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                    ? row.Admission.ReasonCode
                    : null,
                row.Admission.ReasonCode != null
                    && ((byte[])(object)row.Admission.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound,
                (row.Admission.Revision >= 1 && row.Admission.Revision < Int64Maximum
                    && row.Admission.Revision % Int64Maximum == row.Admission.Revision)
                    || row.Admission.Revision == Int64Maximum
                    ? (long?)row.Admission.Revision
                    : null,
                (DateTimeOffset?)row.Admission.UpdatedAtUtc),
            new LocalExecutionAdmissionHistoryStep(
                (Guid?)row.Step.Id,
                (Guid?)row.Step.SequenceId,
                row.Step.Ordinal >= 0
                    && row.Step.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                    && row.Step.Ordinal % Int32Maximum == row.Step.Ordinal
                    ? (int?)row.Step.Ordinal
                    : null,
                row.Step.Kind == LocalExecutionStepKind.Implementation ? 1
                    : row.Step.Kind == LocalExecutionStepKind.Verification ? 2
                    : row.Step.Kind == LocalExecutionStepKind.CodeReview ? 3
                    : row.Step.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                    : row.Step.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                    : row.Step.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                    : 0,
                row.Step.PassNumber >= 0
                    && row.Step.PassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                    && row.Step.PassNumber % Int32Maximum == row.Step.PassNumber
                    ? (int?)row.Step.PassNumber
                    : null,
                row.Step.RecipeOrdinal >= 0
                    && row.Step.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Step.RecipeOrdinal % Int32Maximum == row.Step.RecipeOrdinal
                    ? row.Step.RecipeOrdinal
                    : null,
                row.Step.RecipeOrdinal != null,
                row.Step.PredecessorStepId,
                (Guid?)row.Step.SourceCheckpointId,
                ((byte[])(object)row.Step.SourceFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Step.SourceFingerprintSha256
                    : null,
                row.Step.ReportMessageId,
                row.Step.SubjectAttemptId,
                row.Step.State == LocalExecutionSequenceStepState.Reserved ? 1
                    : row.Step.State == LocalExecutionSequenceStepState.Claimed ? 2
                    : row.Step.State == LocalExecutionSequenceStepState.Concluded ? 3
                    : row.Step.State == LocalExecutionSequenceStepState.Revoked ? 4
                    : row.Step.State == LocalExecutionSequenceStepState.Unresolved ? 5
                    : 0,
                (Guid?)row.Step.ReservedOperationId,
                row.Step.OperationId,
                row.Step.ClaimedAtUtc != null,
                row.Step.ResultDeadlineUtc != null,
                ((byte[])(object)row.Step.ReasonCode!).Length <= LocalExecutionInspectionProjection.ReasonByteBound
                    ? row.Step.ReasonCode
                    : null,
                row.Step.ReasonCode != null
                    && ((byte[])(object)row.Step.ReasonCode!).Length > LocalExecutionInspectionProjection.ReasonByteBound),
            new LocalExecutionAdmissionHistoryReference(
                (Guid?)row.Source.WorkspaceId,
                ((byte[])(object)row.Source.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Source.FingerprintSha256
                    : null,
                (Guid?)row.Report.RunId,
                row.Report.Type == CollaborationMessageType.ExecutionReport,
                (Guid?)row.Recipe.VerificationCommandId,
                ((byte[])(object)row.Recipe.CommandName).Length <= LocalExecutionInspectionProjection.NameByteBound
                    ? row.Recipe.CommandName
                    : null,
                ((byte[])(object)row.Recipe.CommandName).Length > LocalExecutionInspectionProjection.NameByteBound));
}
