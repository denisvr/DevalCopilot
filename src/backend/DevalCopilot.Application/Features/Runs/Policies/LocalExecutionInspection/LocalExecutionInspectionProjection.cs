using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The bounded, untracked scalar projections of the recorded local execution inspection (ADR-0061). Nothing outside the listed columns is
/// ever selected: no settings snapshot, confirmation or plan-input digest, preparation owner token, workspace path, message text or stored
/// enum text. Every stored enum is classified by the database, every number is guarded to an exact integer in its supported range and every
/// text is bounded by its stored bytes (a cast to a blob counts an embedded NUL) before it is converted, so malformed SQLite storage reads
/// as an explicit refusal rather than a plausible value. The exact character bounds are applied again in memory by the reader.
/// </summary>
internal static class LocalExecutionInspectionProjection
{
    /// <summary>The most step rows read: the largest legitimate sequence plus one, so a reachable extra row proves damaged storage.</summary>
    public const int StepBound = 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6 + 1;

    /// <summary>The most pinned recipe rows read: the largest legitimate set plus one.</summary>
    public const int RecipeBound = LocalExecutionSequencePolicy.MaximumRecipes + 1;

    /// <summary>The internal read guard of one stored arguments JSON (2 MiB). It is not a write limit and is never disclosed.</summary>
    public const int ArgumentsByteBound = 2 * 1024 * 1024;

    /// <summary>The internal read guard of one stored executable path (128 KiB). It is not a write limit and is never disclosed.</summary>
    public const int ExecutablePathByteBound = 128 * 1024;

    public const long MaximumSafeInteger = (1L << 53) - 1;

    private const int Unrecognized = 99;

    // A UTF-16 unit is at most three UTF-8 bytes, so every admissible value fits its guard and the exact bound is applied again in memory.
    internal const int FingerprintByteBound = 64;
    internal const int ReasonByteBound = 64 * 3;
    internal const int NameByteBound = 600;
    internal const int ModeByteBound = 8;

    // A column with INTEGER affinity keeps TEXT, REAL or BLOB it cannot convert. Numbers sort below TEXT and BLOB, so a numeric range
    // admits only numbers, and `%` casts to an integer, so only a stored INTEGER equals its own remainder. The modulus has the column CLR
    // type (a wider one makes EF cast both sides and hides a REAL). Every range below lies strictly inside (-int.MaxValue, int.MaxValue),
    // so no extreme-value special case applies; only the exit code reaches the extremes and carries its three direct comparisons.
    internal const int Int32Maximum = int.MaxValue;
    private const int Int32Minimum = int.MinValue;
    private const int Int32NegatedMaximum = -int.MaxValue;
    internal const long SafeIntegerModulus = 1L << 53;

    public static IQueryable<LocalExecutionInspectionRunRow> ProjectRun(this IQueryable<Run> runs) =>
        runs.Select(run => new LocalExecutionInspectionRunRow(
            run.Id,
            run.ProjectId,
            ((byte[])(object)EF.Property<string>(run, Run.ExecutionModeStorageProperty)).Length <= ModeByteBound
                ? EF.Property<string>(run, Run.ExecutionModeStorageProperty)
                : null));

    public static IQueryable<LocalExecutionInspectionSequenceRow> ProjectSequence(this IQueryable<LocalExecutionSequence> sequences) =>
        sequences.Select(sequence => new LocalExecutionInspectionSequenceRow(
            sequence.Id,
            sequence.RunId,
            sequence.ProjectId,
            sequence.WorkspaceId,
            sequence.CheckpointId,
            ((byte[])(object)sequence.FingerprintSha256).Length <= FingerprintByteBound ? sequence.FingerprintSha256 : null,
            sequence.PlanProposalMessageId,
            sequence.PlanAuthority == LocalExecutionPlanAuthority.AcceptedRoot ? 1
                : sequence.PlanAuthority == LocalExecutionPlanAuthority.AcceptedRevision ? 2
                : sequence.PlanAuthority == LocalExecutionPlanAuthority.HumanAuthorizedRevision ? 3
                : 0,
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
            ((byte[])(object)sequence.ReasonCode!).Length <= ReasonByteBound ? sequence.ReasonCode : null,
            sequence.ReasonCode != null && ((byte[])(object)sequence.ReasonCode).Length > ReasonByteBound,
            sequence.RecipeCount >= LocalExecutionSequencePolicy.MinimumRecipes
                && sequence.RecipeCount <= LocalExecutionSequencePolicy.MaximumRecipes
                && sequence.RecipeCount % Int32Maximum == sequence.RecipeCount
                ? sequence.RecipeCount
                : null,
            sequence.CorrectionAllowance >= 0
                && sequence.CorrectionAllowance <= LocalExecutionSequencePolicy.MaximumCorrections
                && sequence.CorrectionAllowance % Int32Maximum == sequence.CorrectionAllowance
                ? sequence.CorrectionAllowance
                : null,
            sequence.Revision >= 1 && sequence.Revision <= MaximumSafeInteger && sequence.Revision % SafeIntegerModulus == sequence.Revision
                ? sequence.Revision
                : null,
            sequence.ArmedAtUtc,
            sequence.UpdatedAtUtc,
            sequence.EndedAtUtc,
            sequence.AdmissionMode == LocalExecutionAdmissionMode.Immediate ? 1
                : sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission ? 2
                : 0));

    /// <summary>The sequence revision alone, as a guarded positive safe integer, for the read-before/read-after comparison.</summary>
    public static IQueryable<long?> ProjectRevision(this IQueryable<LocalExecutionSequence> sequences) =>
        sequences.Select(sequence =>
            sequence.Revision >= 1 && sequence.Revision <= MaximumSafeInteger && sequence.Revision % SafeIntegerModulus == sequence.Revision
                ? (long?)sequence.Revision
                : null);

    /// <summary>A bool column is truthy for any nonzero number, so the enabled flag is read as its stored bytes: only exactly 1 is enabled.</summary>
    public static IQueryable<LocalExecutionInspectionRecipeRow> ProjectRecipe(
        this IQueryable<LocalExecutionSequenceRecipe> recipes) =>
        recipes.Select(recipe => new LocalExecutionInspectionRecipeRow(
            recipe.VerificationCommandId,
            recipe.Ordinal >= 0
                && recipe.Ordinal < LocalExecutionSequencePolicy.MaximumRecipes
                && recipe.Ordinal % Int32Maximum == recipe.Ordinal
                ? recipe.Ordinal
                : null,
            recipe.CommandNumber >= 1 && recipe.CommandNumber < Int32Maximum && recipe.CommandNumber % Int32Maximum == recipe.CommandNumber
                ? recipe.CommandNumber
                : null,
            ((byte[])(object)recipe.CommandName).Length <= NameByteBound ? recipe.CommandName : null,
            ((byte[])(object)recipe.CommandName).Length > NameByteBound,
            ((byte[])(object)recipe.IsEnabled).Length <= 8 ? (byte[])(object)recipe.IsEnabled : null));

    public static IQueryable<LocalExecutionInspectionSelectedRecipeRow> ProjectSelectedRecipe(
        this IQueryable<LocalExecutionSequenceRecipe> recipes) =>
        recipes.Select(recipe => new LocalExecutionInspectionSelectedRecipeRow(
            recipe.VerificationCommandId,
            recipe.CommandNumber >= 1 && recipe.CommandNumber < Int32Maximum && recipe.CommandNumber % Int32Maximum == recipe.CommandNumber
                ? recipe.CommandNumber
                : null,
            ((byte[])(object)recipe.CommandName).Length <= NameByteBound ? recipe.CommandName : null,
            ((byte[])(object)recipe.CommandName).Length > NameByteBound,
            ((byte[])(object)recipe.ExecutablePath).Length <= ExecutablePathByteBound ? recipe.ExecutablePath : null,
            ((byte[])(object)recipe.ExecutablePath).Length > ExecutablePathByteBound,
            ((byte[])(object)recipe.ArgumentsJson).Length <= ArgumentsByteBound ? recipe.ArgumentsJson : null,
            ((byte[])(object)recipe.ArgumentsJson).Length > ArgumentsByteBound,
            recipe.TimeoutSeconds >= 1
                && recipe.TimeoutSeconds <= VerificationCommand.MaximumTimeoutSeconds
                && recipe.TimeoutSeconds % Int32Maximum == recipe.TimeoutSeconds
                ? recipe.TimeoutSeconds
                : null));

    public static IQueryable<LocalExecutionInspectionStepRow> ProjectStep(this IQueryable<LocalExecutionSequenceStep> steps) =>
        steps.Select(step => new LocalExecutionInspectionStepRow(
            step.Id,
            step.SequenceId,
            step.Ordinal >= 0
                && step.Ordinal < 3 * LocalExecutionSequencePolicy.MaximumRecipes + 6
                && step.Ordinal % Int32Maximum == step.Ordinal
                ? step.Ordinal
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
                ? step.PassNumber
                : null,
            step.RecipeOrdinal >= 0
                && step.RecipeOrdinal < LocalExecutionSequencePolicy.MaximumRecipes
                && step.RecipeOrdinal % Int32Maximum == step.RecipeOrdinal
                ? step.RecipeOrdinal
                : null,
            step.RecipeOrdinal != null,
            step.PredecessorStepId,
            step.SourceCheckpointId,
            ((byte[])(object)step.SourceFingerprintSha256).Length <= FingerprintByteBound ? step.SourceFingerprintSha256 : null,
            step.ReportMessageId,
            step.SubjectAttemptId,
            step.State == LocalExecutionSequenceStepState.Reserved ? 1
                : step.State == LocalExecutionSequenceStepState.Claimed ? 2
                : step.State == LocalExecutionSequenceStepState.Concluded ? 3
                : step.State == LocalExecutionSequenceStepState.Revoked ? 4
                : step.State == LocalExecutionSequenceStepState.Unresolved ? 5
                : 0,
            step.ReservedOperationId,
            step.OperationId,
            step.PreparationReservedAtUtc,
            step.PreparationDeadlineUtc,
            step.ClaimedAtUtc,
            step.ResultDeadlineUtc,
            ((byte[])(object)step.ReasonCode!).Length <= ReasonByteBound ? step.ReasonCode : null,
            step.ReasonCode != null && ((byte[])(object)step.ReasonCode).Length > ReasonByteBound));

    public static IQueryable<LocalExecutionInspectionCheckpointRow> ProjectCheckpoint(this IQueryable<GitCheckpoint> checkpoints) =>
        checkpoints.Select(checkpoint => new LocalExecutionInspectionCheckpointRow(
            checkpoint.Id,
            checkpoint.WorkspaceId,
            ((byte[])(object)checkpoint.FingerprintSha256).Length <= FingerprintByteBound ? checkpoint.FingerprintSha256 : null,
            ((byte[])(object)checkpoint.FingerprintSha256).Length > FingerprintByteBound));

    public static IQueryable<LocalExecutionInspectionMessageRow> ProjectMessage(this IQueryable<CollaborationMessage> messages) =>
        messages.Select(message => new LocalExecutionInspectionMessageRow(
            message.Id,
            message.RunId,
            message.Type == CollaborationMessageType.Proposal,
            message.Type == CollaborationMessageType.ExecutionReport));

    public static IQueryable<LocalExecutionInspectionExecutionRow> ProjectExecution(this IQueryable<VerificationExecution> executions) =>
        executions.Select(execution => new LocalExecutionInspectionExecutionRow(
            execution.Id,
            execution.ProjectId,
            execution.GitWorkspaceId,
            execution.GitCheckpointId,
            execution.VerificationCommandId,
            execution.ExecutionNumber >= 1
                && execution.ExecutionNumber < Int32Maximum
                && execution.ExecutionNumber % Int32Maximum == execution.ExecutionNumber
                ? execution.ExecutionNumber
                : null,
            ((byte[])(object)execution.CommandName).Length <= NameByteBound ? execution.CommandName : null,
            ((byte[])(object)execution.CommandName).Length > NameByteBound,
            ((byte[])(object)execution.ExecutablePath).Length <= ExecutablePathByteBound ? execution.ExecutablePath : null,
            ((byte[])(object)execution.ExecutablePath).Length > ExecutablePathByteBound,
            ((byte[])(object)execution.Arguments).Length <= ArgumentsByteBound ? (byte[])(object)execution.Arguments : null,
            ((byte[])(object)execution.Arguments).Length > ArgumentsByteBound,
            execution.TimeoutSeconds >= 1
                && execution.TimeoutSeconds <= VerificationCommand.MaximumTimeoutSeconds
                && execution.TimeoutSeconds % Int32Maximum == execution.TimeoutSeconds
                ? execution.TimeoutSeconds
                : null,
            ((byte[])(object)execution.CheckpointFingerprintSha256).Length <= FingerprintByteBound
                ? execution.CheckpointFingerprintSha256
                : null,
            ((byte[])(object)execution.CheckpointFingerprintSha256).Length > FingerprintByteBound,
            ((byte[])(object)execution.CompletionFingerprintSha256!).Length <= FingerprintByteBound
                ? execution.CompletionFingerprintSha256
                : null,
            execution.CompletionFingerprintSha256 != null
                && ((byte[])(object)execution.CompletionFingerprintSha256).Length > FingerprintByteBound,
            execution.CompletionFingerprintSha256 != null,
            execution.Status == VerificationExecutionStatus.Running ? 1
                : execution.Status == VerificationExecutionStatus.Passed ? 2
                : execution.Status == VerificationExecutionStatus.Failed ? 3
                : execution.Status == VerificationExecutionStatus.TimedOut ? 4
                : execution.Status == VerificationExecutionStatus.Cancelled ? 5
                : execution.Status == VerificationExecutionStatus.Interrupted ? 6
                : execution.Status == VerificationExecutionStatus.SourceChanged ? 7
                : execution.Status == VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused ? 8
                : 0,
            execution.Outcome == null ? 0
                : execution.Outcome == VerificationExecutionOutcome.Exited ? 1
                : execution.Outcome == VerificationExecutionOutcome.TimedOut ? 2
                : execution.Outcome == VerificationExecutionOutcome.Cancelled ? 3
                : execution.Outcome == VerificationExecutionOutcome.Failed ? 4
                : Unrecognized,
            execution.ExitCode >= int.MinValue
                && execution.ExitCode <= int.MaxValue
                && (execution.ExitCode == Int32Maximum
                    || execution.ExitCode == Int32Minimum
                    || execution.ExitCode == Int32NegatedMaximum
                    || execution.ExitCode % Int32Maximum == execution.ExitCode)
                ? execution.ExitCode
                : null,
            execution.ExitCode != null,
            execution.ClaimedAtUtc,
            execution.DispatchedAtUtc,
            execution.CompletedAtUtc));
}
