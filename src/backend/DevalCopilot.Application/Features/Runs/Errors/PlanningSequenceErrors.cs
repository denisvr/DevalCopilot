using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicitly authorized planning sequence (ADR-0055). No error carries a path, a stored
/// value, an identifier of other work or an exception. The same codes are the fixed arming refusals of the read-only status.</summary>
public static class PlanningSequenceErrors
{
    public const string NotFoundCode = "planning_sequence.not_found";
    public const string RunNotManualCode = "planning_sequence.run_not_manual";
    public const string RunNotPristineCode = "planning_sequence.run_not_pristine";
    public const string ProjectBusyCode = "planning_sequence.project_busy";
    public const string WorkspaceNotReadyCode = "planning_sequence.workspace_not_ready";
    public const string LeaseNotActiveCode = "planning_sequence.lease_not_active";
    public const string CheckpointMissingCode = "planning_sequence.checkpoint_missing";
    public const string TupleStaleCode = "planning_sequence.tuple_stale";
    public const string TupleConflictCode = "planning_sequence.tuple_conflict";
    public const string ManualControlBlockedCode = "planning_sequence.manual_control_blocked";
    public const string StepAuthorityLostCode = "planning_sequence.step_authority_lost";
    public const string StepExpiredCode = "planning_sequence.step_expired";
    public const string StepSourceChangedCode = "planning_sequence.step_source_changed";
    public const string StepInputMismatchCode = "planning_sequence.step_input_mismatch";
    public const string PersistenceFailedCode = "planning_sequence.persistence_failed";
    public const string PersistenceUnresolvedCode = "planning_sequence.persistence_unresolved";
    public const string InvalidRequestCode = "planning_sequence.invalid_request";
    public const string BodyTooLargeCode = "planning_sequence.body_too_large";
    public const string BodyNotAllowedCode = "planning_sequence.body_not_allowed";

    /// <summary>The fixed, non-echoing refusal of a malformed arming or bodyless request; the body is never repeated.</summary>
    public static Error InvalidRequest() => new(
        InvalidRequestCode, "The request is not exactly the supported planning sequence request.", ErrorType.Validation);

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error BodyTooLarge() => new(
        BodyTooLargeCode, "The request body exceeds the supported size.", ErrorType.Validation);

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested planning sequence was not found for this run.");

    public static Error RunNotManual() => Error.Conflict(
        RunNotManualCode, "Only a manual Agent run can be armed for a planning sequence.");

    public static Error RunNotPristine() => Error.Conflict(
        RunNotPristineCode,
        "Only a created run with no attempt, collaboration message or local-commit operation can first arm a planning sequence.");

    public static Error ProjectBusy() => Error.Conflict(
        ProjectBusyCode, "An attempt, verification execution, local commit or workspace of this project is active or in an unknown state.");

    public static Error WorkspaceNotReady() => Error.Conflict(
        WorkspaceNotReadyCode, "The project's latest isolated workspace must be ready and owned to arm a planning sequence.");

    public static Error LeaseNotActive() => Error.Conflict(
        LeaseNotActiveCode, "An active workspace lease is required to arm a planning sequence.");

    public static Error CheckpointMissing() => Error.Conflict(
        CheckpointMissingCode, "A current Git checkpoint is required to arm a planning sequence.");

    public static Error TupleStale() => Error.Conflict(
        TupleStaleCode,
        "The requested workspace, checkpoint and fingerprint are no longer the current source of this project; read the status again.");

    public static Error TupleConflict() => Error.Conflict(
        TupleConflictCode, "This run's planning sequence was armed for a different workspace, checkpoint or fingerprint.");

    public static Error ManualControlBlocked() => Error.Conflict(
        ManualControlBlockedCode,
        "A planning sequence still holds automatic authority over this run or its workspace; use Stop after current step and wait for it to drain.");

    public static Error StepAuthorityLost() => Error.Conflict(
        StepAuthorityLostCode, "The planning sequence no longer authorizes this step; nothing was claimed.");

    public static Error StepExpired() => Error.Conflict(
        StepExpiredCode, "The step's preparation deadline elapsed; nothing was claimed.");

    public static Error StepSourceChanged() => Error.Conflict(
        StepSourceChangedCode, "The pinned workspace, lease, checkpoint or objective of the planning sequence no longer holds.");

    public static Error StepInputMismatch() => Error.Conflict(
        StepInputMismatchCode, "The claim does not consume the exact predecessor input that the planning sequence step expects.");

    public static Error PersistenceFailed() => Error.Failure(
        PersistenceFailedCode, "The planning sequence could not be durably recorded.");

    public static Error PersistenceUnresolved() => Error.Failure(
        PersistenceUnresolvedCode, "Whether the planning sequence change was durably recorded could not be confirmed.");
}
