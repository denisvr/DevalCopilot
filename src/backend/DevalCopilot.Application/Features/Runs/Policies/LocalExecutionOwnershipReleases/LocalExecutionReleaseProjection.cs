using System.Linq.Expressions;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The guarded scalar projection of one release-candidate row (ADR-0065). Like the other recorded inspections, every stored enum is
/// classified by the database, every number is an exact integer in its supported range and every text is bounded by its stored bytes
/// before it is converted. The witness's host launch identity and nonce, the confirmation of the arming, settings snapshots, paths,
/// arguments, environment values and provider output are never selected. The last member of every row (<see cref="LocalExecutionReleaseRow.StorageValid"/>)
/// is computed fresh from the raw storage of this one statement and is never persisted or cached: every NULLABLE numeric fact read is genuinely NULL
/// or exactly a supported integer. Where an unsupported nonnull value would otherwise project as the legal null, only this flag tells them apart.
/// </summary>
internal static class LocalExecutionReleaseProjection
{
    private const int Int32Maximum = LocalExecutionInspectionProjection.Int32Maximum;
    private const int Int32Minimum = int.MinValue;
    private const int Int32NegatedMaximum = -int.MaxValue;
    private const long Int64Maximum = long.MaxValue;
    private const int Unrecognized = 99;

    public static IQueryable<LocalExecutionReleaseRow> Project(this IQueryable<LocalExecutionReleaseJoin> joins, IDevalCopilotDbContext dbContext) =>
        joins.Select(RowOf(dbContext));

    public static Expression<Func<LocalExecutionReleaseJoin, LocalExecutionReleaseRow>> RowOf(IDevalCopilotDbContext dbContext)
    {
        var releasableOutcomes = LocalExecutionReleaseTerminal.AgentOutcomes.ToArray();
        var definedOutcomes = LocalExecutionReleaseTerminal.DefinedAgentOutcomes.ToArray();
        return row => new LocalExecutionReleaseRow(
            new LocalExecutionReleaseRoot(
                (Guid?)row.Run.Id ?? Guid.Empty,
                (Guid?)row.Run.ProjectId ?? Guid.Empty,
                (Guid?)row.Sequence.Id,
                (Guid?)row.Sequence.RunId,
                (Guid?)row.Sequence.ProjectId,
                (Guid?)row.Sequence.WorkspaceId,
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
                row.Sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate ? 1
                    : row.Sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission ? 2
                    : 0,
                row.Sequence.RecipeCount >= LocalExecutionSequencePolicy.MinimumRecipes
                    && row.Sequence.RecipeCount <= LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Sequence.RecipeCount % Int32Maximum == row.Sequence.RecipeCount
                    ? (int?)row.Sequence.RecipeCount
                    : null,
                (Guid?)row.Workspace.ProjectId,
                dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                    .Where(counted => counted.SequenceId == row.Sequence.Id)
                    .Take(LocalExecutionInspectionProjection.RecipeBound)
                    .Count(),
                dbContext.LocalExecutionSequenceSteps.AsNoTracking()
                    .Where(counted => counted.SequenceId == row.Sequence.Id)
                    .Take(LocalExecutionInspectionProjection.StepBound)
                    .Count(),
                EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.ManualAgentText),
            new LocalExecutionReleaseStep(
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
                row.Step.OperationId,
                (Guid?)row.Source.WorkspaceId,
                ((byte[])(object)row.Source.FingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Source.FingerprintSha256
                    : null,
                (Guid?)row.Recipe.VerificationCommandId),
            new LocalExecutionReleaseWitness(
                (Guid?)row.Witness.Id,
                (Guid?)row.Witness.SequenceId,
                (Guid?)row.Witness.RunId,
                (Guid?)row.Witness.ProjectId,
                (Guid?)row.Witness.WorkspaceId,
                (Guid?)row.Witness.StepId,
                row.Witness.OperationFamily == LocalExecutionOperationFamily.Agent ? 1
                    : row.Witness.OperationFamily == LocalExecutionOperationFamily.Verification ? 2
                    : 0,
                row.Witness.AgentAttemptId,
                row.Witness.VerificationExecutionId,
                row.Witness.Kind == LocalExecutionStepKind.Implementation ? 1
                    : row.Witness.Kind == LocalExecutionStepKind.Verification ? 2
                    : row.Witness.Kind == LocalExecutionStepKind.CodeReview ? 3
                    : row.Witness.Kind == LocalExecutionStepKind.VerificationDiagnosis ? 4
                    : row.Witness.Kind == LocalExecutionStepKind.ReviewCorrection ? 5
                    : row.Witness.Kind == LocalExecutionStepKind.DiagnosisCorrection ? 6
                    : 0,
                row.Witness.Ordinal >= 0
                    && row.Witness.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                    && row.Witness.Ordinal % Int32Maximum == row.Witness.Ordinal
                    ? (int?)row.Witness.Ordinal
                    : null,
                row.Witness.PassNumber >= 0
                    && row.Witness.PassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                    && row.Witness.PassNumber % Int32Maximum == row.Witness.PassNumber
                    ? (int?)row.Witness.PassNumber
                    : null,
                row.Witness.RecipeOrdinal >= 0
                    && row.Witness.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Witness.RecipeOrdinal % Int32Maximum == row.Witness.RecipeOrdinal
                    ? row.Witness.RecipeOrdinal
                    : null,
                row.Witness.PredecessorStepId,
                (Guid?)row.Witness.SourceCheckpointId,
                ((byte[])(object)row.Witness.SourceFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Witness.SourceFingerprintSha256
                    : null,
                row.Witness.ReportMessageId,
                row.Witness.SubjectAttemptId,
                row.Witness.AgentResponseContract == AgentResponseContract.ImplementationReport ? 1
                    : row.Witness.AgentResponseContract == AgentResponseContract.ImplementationReview ? 2
                    : row.Witness.AgentResponseContract == AgentResponseContract.ReviewCorrection ? 3
                    : row.Witness.AgentResponseContract == AgentResponseContract.VerificationDiagnosis ? 4
                    : 0,
                row.Witness.RecipeCommandId,
                row.Witness.AdmissionId,
                (DateTimeOffset?)row.Witness.DispatchedAtUtc),
            new LocalExecutionReleaseObservation(
                (Guid?)row.Observation.Id,
                (Guid?)row.Observation.WitnessId,
                row.Observation.ProcessOutcome == ProcessOutcome.Exited ? 1
                    : row.Observation.ProcessOutcome == ProcessOutcome.TimedOut ? 2
                    : row.Observation.ProcessOutcome == ProcessOutcome.Cancelled ? 3
                    : 0,
                row.Observation.SemanticExitCode >= int.MinValue
                    && row.Observation.SemanticExitCode <= int.MaxValue
                    && (row.Observation.SemanticExitCode == Int32Maximum
                        || row.Observation.SemanticExitCode == Int32Minimum
                        || row.Observation.SemanticExitCode == Int32NegatedMaximum
                        || row.Observation.SemanticExitCode % Int32Maximum == row.Observation.SemanticExitCode)
                    ? row.Observation.SemanticExitCode
                    : null,
                row.Observation.ProcessDuration >= TimeSpan.Zero
                    && row.Observation.ProcessDuration == (TimeSpan)(object)(long)(object)row.Observation.ProcessDuration
                    ? (TimeSpan?)row.Observation.ProcessDuration
                    : null,
                row.Observation.PhysicalExitCode >= int.MinValue
                    && row.Observation.PhysicalExitCode <= int.MaxValue
                    && (row.Observation.PhysicalExitCode == Int32Maximum
                        || row.Observation.PhysicalExitCode == Int32Minimum
                        || row.Observation.PhysicalExitCode == Int32NegatedMaximum
                        || row.Observation.PhysicalExitCode % Int32Maximum == row.Observation.PhysicalExitCode)
                    ? (int?)row.Observation.PhysicalExitCode
                    : null,
                row.Observation.ObservedElapsed >= TimeSpan.Zero
                    && row.Observation.ObservedElapsed == (TimeSpan)(object)(long)(object)row.Observation.ObservedElapsed
                    ? (TimeSpan?)row.Observation.ObservedElapsed
                    : null,
                (DateTimeOffset?)row.Observation.RecordedAtUtc),
            new LocalExecutionReleaseReceipt(
                (Guid?)row.Release.Id,
                (Guid?)row.ReleaseByWitness.Id,
                (Guid?)row.Release.WitnessId,
                (Guid?)row.Release.ObservationId,
                (Guid?)row.Release.StepId,
                (Guid?)row.Release.EventId,
                (DateTimeOffset?)row.Release.ReleasedAtUtc,
                row.Release.ConfirmationVersion == 1 ? (int?)1 : null,
                ((byte[])(object)row.Release.ConfirmationSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Release.ConfirmationSha256
                    : null,
                (DateTimeOffset?)row.Release.ConfirmationExpiresAtUtc,
                (Guid?)row.Event.Id,
                (Guid?)row.Event.RunId,
                (row.Event.Sequence >= 1 && row.Event.Sequence < Int64Maximum && row.Event.Sequence % Int64Maximum == row.Event.Sequence)
                    || row.Event.Sequence == Int64Maximum
                    ? (long?)row.Event.Sequence
                    : null,
                row.Event.EventType == RunEventType.LocalExecutionOwnershipReleased,
                row.Event.ActorKind == ParticipantKind.Human,
                new LocalExecutionReleaseIntent(
                    row.Release.IntentVersion == LocalExecutionOwnershipRelease.CurrentIntentVersion ? (int?)LocalExecutionOwnershipRelease.CurrentIntentVersion : null,
                    (LocalExecutionOperationFamily?)row.Release.IntentFamily,
                    (Guid?)row.Release.IntentOperationId,
                    (Guid?)row.Release.IntentSequenceId,
                    (Guid?)row.Release.IntentRunId,
                    (Guid?)row.Release.IntentProjectId,
                    (Guid?)row.Release.IntentWorkspaceId,
                    (LocalExecutionSequenceState?)row.Release.IntentSequenceState,
                    (LocalExecutionAdmissionMode?)row.Release.IntentAdmissionMode,
                    row.Release.IntentAdmissionId,
                    (LocalExecutionStepKind?)row.Release.IntentKind,
                    row.Release.IntentOrdinal >= 0
                        && row.Release.IntentOrdinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                        && row.Release.IntentOrdinal % Int32Maximum == row.Release.IntentOrdinal
                        ? (int?)row.Release.IntentOrdinal
                        : null,
                    row.Release.IntentPassNumber >= 0
                        && row.Release.IntentPassNumber <= LocalExecutionSequencePolicy.MaximumPasses
                        && row.Release.IntentPassNumber % Int32Maximum == row.Release.IntentPassNumber
                        ? (int?)row.Release.IntentPassNumber
                        : null,
                    row.Release.IntentRecipeOrdinal >= 0
                        && row.Release.IntentRecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                        && row.Release.IntentRecipeOrdinal % Int32Maximum == row.Release.IntentRecipeOrdinal
                        ? row.Release.IntentRecipeOrdinal
                        : null,
                    row.Release.IntentPredecessorStepId,
                    (Guid?)row.Release.IntentSourceCheckpointId,
                    row.Release.IntentSourceFingerprintSha256,
                    row.Release.IntentReportMessageId,
                    row.Release.IntentSubjectAttemptId,
                    row.Release.IntentAgentResponseContract,
                    row.Release.IntentRecipeCommandId,
                    row.Release.IntentOperationStatus,
                    row.Release.IntentAgentOutcome,
                    row.Release.IntentCompletionFingerprintSha256,
                    (ProcessOutcome?)row.Release.IntentProcessOutcome,
                    row.Release.IntentSemanticExitCode >= int.MinValue
                        && row.Release.IntentSemanticExitCode <= int.MaxValue
                        && (row.Release.IntentSemanticExitCode == Int32Maximum
                            || row.Release.IntentSemanticExitCode == Int32Minimum
                            || row.Release.IntentSemanticExitCode == Int32NegatedMaximum
                            || row.Release.IntentSemanticExitCode % Int32Maximum == row.Release.IntentSemanticExitCode)
                        ? row.Release.IntentSemanticExitCode
                        : null,
                    row.Release.IntentProcessDuration >= TimeSpan.Zero
                        && row.Release.IntentProcessDuration == (TimeSpan)(object)(long)(object)row.Release.IntentProcessDuration
                        ? (TimeSpan?)row.Release.IntentProcessDuration
                        : null,
                    row.Release.IntentPhysicalExitCode >= int.MinValue
                        && row.Release.IntentPhysicalExitCode <= int.MaxValue
                        && (row.Release.IntentPhysicalExitCode == Int32Maximum
                            || row.Release.IntentPhysicalExitCode == Int32Minimum
                            || row.Release.IntentPhysicalExitCode == Int32NegatedMaximum
                            || row.Release.IntentPhysicalExitCode % Int32Maximum == row.Release.IntentPhysicalExitCode)
                        ? (int?)row.Release.IntentPhysicalExitCode
                        : null,
                    row.Release.IntentObservedElapsed >= TimeSpan.Zero
                        && row.Release.IntentObservedElapsed == (TimeSpan)(object)(long)(object)row.Release.IntentObservedElapsed
                        ? (TimeSpan?)row.Release.IntentObservedElapsed
                        : null,
                    (DateTimeOffset?)row.Release.IntentDispatchedAtUtc,
                    (DateTimeOffset?)row.Release.IntentObservationRecordedAtUtc,
                    row.Release.IntentConfirmationVersion == LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
                        ? (int?)LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
                        : null,
                    (DateTimeOffset?)row.Release.IntentConfirmationExpiresAtUtc)),
            new LocalExecutionReleaseOperation(
                (Guid?)row.Attempt.Id,
                (Guid?)row.Attempt.RunId,
                row.Attempt.AgentGitWorkspaceId,
                row.Attempt.AgentGitCheckpointId,
                row.Attempt.Status == AttemptStatus.Running ? 1
                    : row.Attempt.Status == AttemptStatus.Completed ? 2
                    : row.Attempt.Status == AttemptStatus.Failed ? 3
                    : row.Attempt.Status == AttemptStatus.Interrupted ? 4
                    : 0,
                row.Attempt.AgentResponseContract == AgentResponseContract.ImplementationReport ? 1
                    : row.Attempt.AgentResponseContract == AgentResponseContract.ImplementationReview ? 2
                    : row.Attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection ? 3
                    : row.Attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis ? 4
                    : row.Attempt.AgentResponseContract == null ? 0
                    : Unrecognized,
                row.Attempt.AgentOutcome == null ? 0
                    : releasableOutcomes.Contains(row.Attempt.AgentOutcome.Value) ? 1
                    : definedOutcomes.Contains(row.Attempt.AgentOutcome.Value) ? 2
                    : Unrecognized,
                row.Attempt.AgentOutcome != null && releasableOutcomes.Contains(row.Attempt.AgentOutcome.Value) ? row.Attempt.AgentOutcome : null,
                ((byte[])(object)row.Attempt.AgentCheckpointFingerprintSha256!).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Attempt.AgentCheckpointFingerprintSha256
                    : null,
                row.Attempt.AgentDispatchedAtUtc,
                row.Attempt.AgentProcessOutcome == ProcessOutcome.Exited ? 1
                    : row.Attempt.AgentProcessOutcome == ProcessOutcome.TimedOut ? 2
                    : row.Attempt.AgentProcessOutcome == ProcessOutcome.Cancelled ? 3
                    : row.Attempt.AgentProcessOutcome == null ? 0
                    : Unrecognized,
                row.Attempt.AgentProcessExitCode >= int.MinValue
                    && row.Attempt.AgentProcessExitCode <= int.MaxValue
                    && (row.Attempt.AgentProcessExitCode == Int32Maximum
                        || row.Attempt.AgentProcessExitCode == Int32Minimum
                        || row.Attempt.AgentProcessExitCode == Int32NegatedMaximum
                        || row.Attempt.AgentProcessExitCode % Int32Maximum == row.Attempt.AgentProcessExitCode)
                    ? row.Attempt.AgentProcessExitCode
                    : null,
                row.Attempt.AgentProcessDuration >= TimeSpan.Zero
                    && row.Attempt.AgentProcessDuration == (TimeSpan)(object)(long)(object)row.Attempt.AgentProcessDuration
                    ? row.Attempt.AgentProcessDuration
                    : null,
                (Guid?)row.Execution.Id,
                (Guid?)row.Execution.ProjectId,
                (Guid?)row.Execution.GitWorkspaceId,
                (Guid?)row.Execution.GitCheckpointId,
                ((byte[])(object)row.Execution.CheckpointFingerprintSha256).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Execution.CheckpointFingerprintSha256
                    : null,
                ((byte[])(object)row.Execution.CompletionFingerprintSha256!).Length <= LocalExecutionInspectionProjection.FingerprintByteBound
                    ? row.Execution.CompletionFingerprintSha256
                    : null,
                (Guid?)row.Execution.VerificationCommandId,
                row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.Running ? 1
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.Passed ? 2
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.Failed ? 3
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.TimedOut ? 4
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.Cancelled ? 5
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.Interrupted ? 6
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.SourceChanged ? 7
                    : row.Execution.Status == Domain.Features.Projects.VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused ? 8
                    : 0,
                row.Execution.Outcome == null ? 0
                    : row.Execution.Outcome == Domain.Features.Projects.VerificationExecutionOutcome.Exited ? 1
                    : row.Execution.Outcome == Domain.Features.Projects.VerificationExecutionOutcome.TimedOut ? 2
                    : row.Execution.Outcome == Domain.Features.Projects.VerificationExecutionOutcome.Cancelled ? 3
                    : row.Execution.Outcome == Domain.Features.Projects.VerificationExecutionOutcome.Failed ? 4
                    : Unrecognized,
                row.Execution.ExitCode >= int.MinValue
                    && row.Execution.ExitCode <= int.MaxValue
                    && (row.Execution.ExitCode == Int32Maximum
                        || row.Execution.ExitCode == Int32Minimum
                        || row.Execution.ExitCode == Int32NegatedMaximum
                        || row.Execution.ExitCode % Int32Maximum == row.Execution.ExitCode)
                    ? row.Execution.ExitCode
                    : null,
                row.Execution.DispatchedAtUtc,
                (Guid?)row.Admission.Id,
                (Guid?)row.Admission.SequenceId,
                row.Admission.StepId,
                row.Admission.State == LocalExecutionAdmissionState.Waiting ? 1
                    : row.Admission.State == LocalExecutionAdmissionState.Reserved ? 2
                    : row.Admission.State == LocalExecutionAdmissionState.Consumed ? 3
                    : row.Admission.State == LocalExecutionAdmissionState.Retired ? 4
                    : row.Admission.State == LocalExecutionAdmissionState.Unresolved ? 5
                    : 0,
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
                (Guid?)row.Admission.SourceCheckpointId),
            (row.Observation.SemanticExitCode == null
                || (row.Observation.SemanticExitCode >= int.MinValue
                    && row.Observation.SemanticExitCode <= int.MaxValue
                    && (row.Observation.SemanticExitCode == Int32Maximum
                        || row.Observation.SemanticExitCode == Int32Minimum
                        || row.Observation.SemanticExitCode == Int32NegatedMaximum
                        || row.Observation.SemanticExitCode % Int32Maximum == row.Observation.SemanticExitCode)))
            && (row.Attempt.AgentProcessExitCode == null
                || (row.Attempt.AgentProcessExitCode >= int.MinValue
                    && row.Attempt.AgentProcessExitCode <= int.MaxValue
                    && (row.Attempt.AgentProcessExitCode == Int32Maximum
                        || row.Attempt.AgentProcessExitCode == Int32Minimum
                        || row.Attempt.AgentProcessExitCode == Int32NegatedMaximum
                        || row.Attempt.AgentProcessExitCode % Int32Maximum == row.Attempt.AgentProcessExitCode)))
            && (row.Execution.ExitCode == null
                || (row.Execution.ExitCode >= int.MinValue
                    && row.Execution.ExitCode <= int.MaxValue
                    && (row.Execution.ExitCode == Int32Maximum
                        || row.Execution.ExitCode == Int32Minimum
                        || row.Execution.ExitCode == Int32NegatedMaximum
                        || row.Execution.ExitCode % Int32Maximum == row.Execution.ExitCode)))
            && (row.Release.IntentSemanticExitCode == null
                || (row.Release.IntentSemanticExitCode >= int.MinValue
                    && row.Release.IntentSemanticExitCode <= int.MaxValue
                    && (row.Release.IntentSemanticExitCode == Int32Maximum
                        || row.Release.IntentSemanticExitCode == Int32Minimum
                        || row.Release.IntentSemanticExitCode == Int32NegatedMaximum
                        || row.Release.IntentSemanticExitCode % Int32Maximum == row.Release.IntentSemanticExitCode)))
            && (row.Witness.RecipeOrdinal == null
                || (row.Witness.RecipeOrdinal >= 0
                    && row.Witness.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Witness.RecipeOrdinal % Int32Maximum == row.Witness.RecipeOrdinal))
            && (row.Admission.RecipeOrdinal == null
                || (row.Admission.RecipeOrdinal >= 0
                    && row.Admission.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Admission.RecipeOrdinal % Int32Maximum == row.Admission.RecipeOrdinal))
            && (row.Release.IntentRecipeOrdinal == null
                || (row.Release.IntentRecipeOrdinal >= 0
                    && row.Release.IntentRecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                    && row.Release.IntentRecipeOrdinal % Int32Maximum == row.Release.IntentRecipeOrdinal)));
    }
}
