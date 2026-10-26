using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// The bounded, untracked scalar projections of the recorded planning inspection (ADR-0068). Nothing outside the listed columns is ever
/// selected: no objective digest, detail code, preparation owner token, workspace path, message text or stored enum text. Every stored
/// enum is classified by the database, every number is guarded to an exact integer in its supported range, every text is bounded by its
/// stored bytes (a cast to a blob counts an embedded NUL) and every identifier and time is projected as its raw stored bytes within its
/// bound and parsed by <see cref="PlanningSequenceInspectionStorage"/>, so malformed SQLite storage reads as an explicit refusal rather
/// than a plausible value or a conversion fault. The exact character bounds are applied again in memory by the reader.
/// </summary>
internal static class PlanningSequenceInspectionProjection
{
    /// <summary>The most step rows read: ADR-0055's five plus one, so a reachable extra row proves damaged storage.</summary>
    public const int StepBound = PlanningSequencePolicy.MaximumSteps + 1;

    public const long MaximumSafeInteger = (1L << 53) - 1;

    // A UTF-16 unit is at most three UTF-8 bytes, so every admissible value fits its guard and the exact bound is applied again in memory.
    internal const int FingerprintByteBound = 64;
    internal const int ReasonByteBound = 64 * 3;
    internal const int ModeByteBound = 8;

    private const int IdentifierBytes = PlanningSequenceInspectionStorage.IdentifierBytes;
    private const int TimeBytes = PlanningSequenceInspectionStorage.TimeBytes;

    // A column with INTEGER affinity keeps TEXT, REAL or BLOB it cannot convert. Numbers sort below TEXT and BLOB, so a numeric range
    // admits only numbers, and `%` casts to an integer, so only a stored INTEGER equals its own remainder. The modulus has the column CLR
    // type (a wider one makes EF cast both sides and hides a REAL). Every range below lies strictly inside (-int.MaxValue,
    // int.MaxValue), so no extreme-value special case applies.
    private const int Int32Maximum = int.MaxValue;
    internal const long SafeIntegerModulus = 1L << 53;

    public static IQueryable<PlanningSequenceInspectionRunRow> ProjectRun(this IQueryable<Run> runs) =>
        runs.Select(run => new PlanningSequenceInspectionRunRow(
            ((byte[])(object)run.ProjectId).Length <= IdentifierBytes ? (byte[])(object)run.ProjectId : null,
            ((byte[])(object)EF.Property<string>(run, Run.ExecutionModeStorageProperty)).Length <= ModeByteBound
                ? EF.Property<string>(run, Run.ExecutionModeStorageProperty)
                : null));

    public static IQueryable<PlanningSequenceInspectionSequenceRow> ProjectSequence(this IQueryable<PlanningSequence> sequences) =>
        sequences.Select(sequence => new PlanningSequenceInspectionSequenceRow(
            ((byte[])(object)sequence.Id).Length <= IdentifierBytes ? (byte[])(object)sequence.Id : null,
            ((byte[])(object)sequence.RunId).Length <= IdentifierBytes ? (byte[])(object)sequence.RunId : null,
            ((byte[])(object)sequence.ProjectId).Length <= IdentifierBytes ? (byte[])(object)sequence.ProjectId : null,
            ((byte[])(object)sequence.WorkspaceId).Length <= IdentifierBytes ? (byte[])(object)sequence.WorkspaceId : null,
            ((byte[])(object)sequence.CheckpointId).Length <= IdentifierBytes ? (byte[])(object)sequence.CheckpointId : null,
            ((byte[])(object)sequence.FingerprintSha256).Length <= FingerprintByteBound ? sequence.FingerprintSha256 : null,
            sequence.State == PlanningSequenceState.Armed ? 1
                : sequence.State == PlanningSequenceState.PreparingStep ? 2
                : sequence.State == PlanningSequenceState.AwaitingStepResult ? 3
                : sequence.State == PlanningSequenceState.Stopping ? 4
                : sequence.State == PlanningSequenceState.ReadyForImplementation ? 5
                : sequence.State == PlanningSequenceState.AwaitingHumanDecision ? 6
                : sequence.State == PlanningSequenceState.Stopped ? 7
                : sequence.State == PlanningSequenceState.NeedsAttention ? 8
                : sequence.State == PlanningSequenceState.Interrupted ? 9
                : 0,
            ((byte[])(object)sequence.ReasonCode!).Length <= ReasonByteBound ? sequence.ReasonCode : null,
            sequence.ReasonCode != null && ((byte[])(object)sequence.ReasonCode).Length > ReasonByteBound,
            sequence.Revision >= 1 && sequence.Revision <= MaximumSafeInteger && sequence.Revision % SafeIntegerModulus == sequence.Revision
                ? sequence.Revision
                : null,
            ((byte[])(object)sequence.ArmedAtUtc).Length <= TimeBytes ? (byte[])(object)sequence.ArmedAtUtc : null,
            ((byte[])(object)sequence.UpdatedAtUtc).Length <= TimeBytes ? (byte[])(object)sequence.UpdatedAtUtc : null,
            ((byte[])(object)sequence.StopRequestedAtUtc!).Length <= TimeBytes ? (byte[])(object)sequence.StopRequestedAtUtc : null,
            sequence.StopRequestedAtUtc != null,
            ((byte[])(object)sequence.EndedAtUtc!).Length <= TimeBytes ? (byte[])(object)sequence.EndedAtUtc : null,
            sequence.EndedAtUtc != null));

    /// <summary>The sequence revision alone, as a guarded positive safe integer, for the read-before/read-after comparison.</summary>
    public static IQueryable<long?> ProjectRevision(this IQueryable<PlanningSequence> sequences) =>
        sequences.Select(sequence =>
            sequence.Revision >= 1 && sequence.Revision <= MaximumSafeInteger && sequence.Revision % SafeIntegerModulus == sequence.Revision
                ? (long?)sequence.Revision
                : null);

    public static IQueryable<PlanningSequenceInspectionStepRow> ProjectStep(this IQueryable<PlanningSequenceStep> steps) =>
        steps.Select(step => new PlanningSequenceInspectionStepRow(
            ((byte[])(object)step.Id).Length <= IdentifierBytes ? (byte[])(object)step.Id : null,
            ((byte[])(object)step.SequenceId).Length <= IdentifierBytes ? (byte[])(object)step.SequenceId : null,
            step.Ordinal >= 0 && step.Ordinal < PlanningSequencePolicy.MaximumSteps && step.Ordinal % Int32Maximum == step.Ordinal
                ? step.Ordinal
                : null,
            step.Role == AgentRole.Planner ? 1
                : step.Role == AgentRole.CriticalReviewer ? 2
                : step.Role == AgentRole.Resolver ? 3
                : 0,
            step.State == PlanningSequenceStepState.Reserved ? 1
                : step.State == PlanningSequenceStepState.Claimed ? 2
                : step.State == PlanningSequenceStepState.Concluded ? 3
                : step.State == PlanningSequenceStepState.Revoked ? 4
                : step.State == PlanningSequenceStepState.Unresolved ? 5
                : 0,
            ((byte[])(object)step.PredecessorAttemptId!).Length <= IdentifierBytes ? (byte[])(object)step.PredecessorAttemptId : null,
            step.PredecessorAttemptId != null,
            ((byte[])(object)step.InputProposalMessageId!).Length <= IdentifierBytes ? (byte[])(object)step.InputProposalMessageId : null,
            step.InputProposalMessageId != null,
            ((byte[])(object)step.ReservedAttemptId).Length <= IdentifierBytes ? (byte[])(object)step.ReservedAttemptId : null,
            ((byte[])(object)step.AttemptId!).Length <= IdentifierBytes ? (byte[])(object)step.AttemptId : null,
            step.AttemptId != null,
            ((byte[])(object)step.PreparationReservedAtUtc).Length <= TimeBytes ? (byte[])(object)step.PreparationReservedAtUtc : null,
            ((byte[])(object)step.PreparationDeadlineUtc).Length <= TimeBytes ? (byte[])(object)step.PreparationDeadlineUtc : null,
            ((byte[])(object)step.ReasonCode!).Length <= ReasonByteBound ? step.ReasonCode : null,
            step.ReasonCode != null && ((byte[])(object)step.ReasonCode).Length > ReasonByteBound,
            step.Revision >= 1 && step.Revision <= MaximumSafeInteger && step.Revision % SafeIntegerModulus == step.Revision
                ? step.Revision
                : null,
            ((byte[])(object)step.UpdatedAtUtc).Length <= TimeBytes ? (byte[])(object)step.UpdatedAtUtc : null));

    public static IQueryable<PlanningSequenceInspectionCheckpointRow> ProjectCheckpoint(this IQueryable<GitCheckpoint> checkpoints) =>
        checkpoints.Select(checkpoint => new PlanningSequenceInspectionCheckpointRow(
            ((byte[])(object)checkpoint.WorkspaceId).Length <= IdentifierBytes ? (byte[])(object)checkpoint.WorkspaceId : null,
            ((byte[])(object)checkpoint.FingerprintSha256).Length <= FingerprintByteBound ? checkpoint.FingerprintSha256 : null,
            ((byte[])(object)checkpoint.FingerprintSha256).Length > FingerprintByteBound));

    public static IQueryable<byte[]?> ProjectWorkspaceProject(this IQueryable<GitWorkspace> workspaces) =>
        workspaces.Select(workspace => ((byte[])(object)workspace.ProjectId).Length <= IdentifierBytes
            ? (byte[])(object)workspace.ProjectId
            : null);

    public static IQueryable<PlanningSequenceInspectionMessageRow> ProjectMessage(this IQueryable<CollaborationMessage> messages) =>
        messages.Select(message => new PlanningSequenceInspectionMessageRow(
            ((byte[])(object)message.Id).Length <= IdentifierBytes ? (byte[])(object)message.Id : null,
            ((byte[])(object)message.RunId).Length <= IdentifierBytes ? (byte[])(object)message.RunId : null,
            ((byte[])(object)message.AttemptId!).Length <= IdentifierBytes ? (byte[])(object)message.AttemptId : null,
            message.AttemptId != null,
            message.Type == CollaborationMessageType.Proposal));

    /// <summary>
    /// Restricts the linked attempt to a stored attempt number this operation can disclose: a stored INTEGER from 1 to int.MaxValue, in the
    /// same statement that materializes the shared scalar identity, so the shared projection never converts any other storage. Numbers
    /// sort below TEXT and BLOB, so the range admits only numbers; the remainder (an INTEGER column's own CLR type as the modulus) admits
    /// only an exact stored INTEGER, never a REAL such as 1.75; int.MaxValue is its own remainder's one exception and is compared directly.
    /// A TEXT or BLOB value, a REAL, an out-of-range INTEGER and a nonpositive number select no row, which the caller reads as Unavailable.
    /// </summary>
    public static IQueryable<Attempt> WhereAttemptNumberSupported(this IQueryable<Attempt> attempts) =>
        attempts.Where(attempt => attempt.AttemptNumber >= 1
            && attempt.AttemptNumber <= Int32Maximum
            && (attempt.AttemptNumber == Int32Maximum || attempt.AttemptNumber % Int32Maximum == attempt.AttemptNumber));

    /// <summary>The recorded pin of the linked attempt (the claim's own columns) and the raw form of the three time columns that the shared
    /// scalar identity projection converts, so their shape is judged before that projection can fault on a corrupt one.</summary>
    public static IQueryable<PlanningSequenceInspectionAttemptRow> ProjectAttemptPin(this IQueryable<Attempt> attempts) =>
        attempts.Select(attempt => new PlanningSequenceInspectionAttemptRow(
            ((byte[])(object)attempt.AgentGitWorkspaceId!).Length <= IdentifierBytes ? (byte[])(object)attempt.AgentGitWorkspaceId : null,
            attempt.AgentGitWorkspaceId != null,
            ((byte[])(object)attempt.AgentGitCheckpointId!).Length <= IdentifierBytes ? (byte[])(object)attempt.AgentGitCheckpointId : null,
            attempt.AgentGitCheckpointId != null,
            ((byte[])(object)attempt.AgentCheckpointFingerprintSha256!).Length <= FingerprintByteBound
                ? attempt.AgentCheckpointFingerprintSha256
                : null,
            attempt.AgentCheckpointFingerprintSha256 != null
                && ((byte[])(object)attempt.AgentCheckpointFingerprintSha256).Length > FingerprintByteBound,
            ((byte[])(object)attempt.ClaimedAtUtc).Length <= TimeBytes ? (byte[])(object)attempt.ClaimedAtUtc : null,
            ((byte[])(object)attempt.CompletedAtUtc!).Length <= TimeBytes ? (byte[])(object)attempt.CompletedAtUtc : null,
            attempt.CompletedAtUtc != null,
            ((byte[])(object)attempt.AgentDispatchedAtUtc!).Length <= TimeBytes ? (byte[])(object)attempt.AgentDispatchedAtUtc : null,
            attempt.AgentDispatchedAtUtc != null));
}
