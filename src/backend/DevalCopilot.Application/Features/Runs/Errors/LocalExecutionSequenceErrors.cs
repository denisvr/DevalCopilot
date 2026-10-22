using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicitly authorized local execution sequence (ADR-0060). No error carries a path, a
/// stored value, an identifier of other work or an exception. The same codes are the fixed arming refusals of the read-only availability.</summary>
public static class LocalExecutionSequenceErrors
{
    public const string NotFoundCode = "local_execution_sequence.not_found";
    public const string RunNotManualCode = "local_execution_sequence.run_not_manual";
    public const string RunNotRunningCode = "local_execution_sequence.run_not_running";
    public const string PriorMutationCode = "local_execution_sequence.prior_mutation";
    public const string AlreadyArmedCode = "local_execution_sequence.already_armed";
    public const string PlanNotFoundCode = "local_execution_sequence.plan_not_found";
    public const string PlanNotEligibleCode = "local_execution_sequence.plan_not_eligible";
    public const string ProjectBusyCode = "local_execution_sequence.project_busy";
    public const string WorkspaceNotReadyCode = "local_execution_sequence.workspace_not_ready";
    public const string LeaseNotActiveCode = "local_execution_sequence.lease_not_active";
    public const string CheckpointMissingCode = "local_execution_sequence.checkpoint_missing";
    public const string WorkspaceOwnedCode = "local_execution_sequence.workspace_owned";
    public const string CapacityUnavailableCode = "local_execution_sequence.capacity_unavailable";
    public const string RecipesMissingCode = "local_execution_sequence.recipes_missing";
    public const string RecipesTooManyCode = "local_execution_sequence.recipes_too_many";
    public const string RecipesIncoherentCode = "local_execution_sequence.recipes_incoherent";
    public const string SettingsUnavailableCode = "local_execution_sequence.settings_unavailable";
    public const string TupleStaleCode = "local_execution_sequence.tuple_stale";
    public const string ConfirmationStaleCode = "local_execution_sequence.confirmation_stale";
    public const string TupleConflictCode = "local_execution_sequence.tuple_conflict";
    public const string ManualControlBlockedCode = "local_execution_sequence.manual_control_blocked";
    public const string PlanningActiveCode = "local_execution_sequence.planning_active";
    public const string StepAuthorityLostCode = "local_execution_sequence.step_authority_lost";
    public const string StepExpiredCode = "local_execution_sequence.step_expired";
    public const string StepSourceChangedCode = "local_execution_sequence.step_source_changed";
    public const string StepInputMismatchCode = "local_execution_sequence.step_input_mismatch";
    public const string AllowanceReachedCode = "local_execution_sequence.allowance_reached";
    public const string PersistenceFailedCode = "local_execution_sequence.persistence_failed";
    public const string PersistenceUnresolvedCode = "local_execution_sequence.persistence_unresolved";
    public const string InvalidRequestCode = "local_execution_sequence.invalid_request";
    public const string BodyTooLargeCode = "local_execution_sequence.body_too_large";
    public const string BodyNotAllowedCode = "local_execution_sequence.body_not_allowed";
    public const string QueueFullCode = "local_execution_sequence.queue_full";
    public const string QueueUnavailableCode = "local_execution_sequence.queue_unavailable";

    public static Error InvalidRequest() => new(
        InvalidRequestCode, "The request is not exactly the supported local execution sequence request.", ErrorType.Validation);

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error BodyTooLarge() => new(
        BodyTooLargeCode, "The request body exceeds the supported size.", ErrorType.Validation);

    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested local execution sequence was not found for this run.");

    public static Error RunNotManual() => Error.Conflict(
        RunNotManualCode, "Only a manual Agent run can be armed for a local execution sequence.");

    public static Error RunNotRunning() => Error.Conflict(
        RunNotRunningCode, "Only a running run can be armed for a local execution sequence.");

    public static Error PriorMutation() => Error.Conflict(
        PriorMutationCode, "A run that already claimed a workspace-mutating Agent attempt, or has a running attempt, cannot arm a local execution sequence.");

    public static Error AlreadyArmed() => Error.Conflict(
        AlreadyArmedCode, "This run already has a local execution sequence; it is armed at most once per run.");

    public static Error PlanNotFound() => Error.Conflict(
        PlanNotFoundCode, "The selected plan was not found for this run.");

    public static Error PlanNotEligible() => Error.Conflict(
        PlanNotEligibleCode,
        "The selected plan is not an explicitly accepted or separately Human-authorized plan that can be implemented in the current source.");

    public static Error ProjectBusy() => Error.Conflict(
        ProjectBusyCode, "An attempt, verification execution, local commit or workspace of this project is active or in an unknown state.");

    public static Error WorkspaceNotReady() => Error.Conflict(
        WorkspaceNotReadyCode, "The project's latest isolated workspace must be ready and owned to arm a local execution sequence.");

    public static Error LeaseNotActive() => Error.Conflict(
        LeaseNotActiveCode, "An active workspace lease is required to arm a local execution sequence.");

    public static Error CheckpointMissing() => Error.Conflict(
        CheckpointMissingCode, "A current Git checkpoint is required to arm a local execution sequence.");

    public static Error WorkspaceOwned() => Error.Conflict(
        WorkspaceOwnedCode, "The workspace has a recorded or unresolved execution owner, or its ownership cannot be trusted.");

    public static Error CapacityUnavailable() => Error.Conflict(
        CapacityUnavailableCode, "The recorded global mutating capacity is at its limit or cannot be trusted.");

    public static Error RecipesMissing() => Error.Conflict(
        RecipesMissingCode, "At least one enabled verification recipe is required to arm a local execution sequence.");

    public static Error RecipesTooMany() => Error.Conflict(
        RecipesTooManyCode, "At most 32 enabled verification recipes can be pinned.");

    public static Error RecipesIncoherent() => Error.Conflict(
        RecipesIncoherentCode, "The enabled verification recipes are not a coherent, uniquely numbered set.");

    public static Error SettingsUnavailable() => Error.Conflict(
        SettingsUnavailableCode, "The run's requested settings, stop policies or budget policies could not be read safely.");

    public static Error TupleStale() => Error.Conflict(
        TupleStaleCode,
        "The requested workspace, checkpoint and fingerprint are no longer the current source of this project; read the availability again.");

    public static Error ConfirmationStale() => Error.Conflict(
        ConfirmationStaleCode,
        "The confirmed plan, recipes, settings or allowance changed after the availability was read; read the availability again.");

    public static Error TupleConflict() => Error.Conflict(
        TupleConflictCode, "This run's local execution sequence was armed for a different plan, source or confirmation.");

    public static Error ManualControlBlocked() => Error.Conflict(
        ManualControlBlockedCode,
        "A local execution sequence still holds automatic authority over this run, its workspace or its project's verification recipes; use Stop after current operation and wait for it to drain.");

    public static Error PlanningActive() => Error.Conflict(
        PlanningActiveCode, "A planning sequence of this run still holds its automatic authority.");

    public static Error StepAuthorityLost() => Error.Conflict(
        StepAuthorityLostCode, "The local execution sequence no longer authorizes this step; nothing was claimed.");

    public static Error StepExpired() => Error.Conflict(
        StepExpiredCode, "The step's preparation deadline elapsed; nothing was claimed.");

    public static Error StepSourceChanged() => Error.Conflict(
        StepSourceChangedCode, "The pinned source, plan, recipes or settings of the local execution sequence no longer hold.");

    public static Error StepInputMismatch() => Error.Conflict(
        StepInputMismatchCode, "The claim does not consume the exact predecessor input that the local execution sequence step expects.");

    public static Error AllowanceReached() => Error.Conflict(
        AllowanceReachedCode, "The authorized automatic correction allowance was reached; nothing was claimed.");

    public static Error QueueFull() => Error.Conflict(
        QueueFullCode, "The bounded queue of local execution cycles is full; wait for a queued cycle to end or stop one.");

    public static Error QueueUnavailable() => Error.Conflict(
        QueueUnavailableCode, "The local execution queue order or bound cannot be trusted; nothing was queued.");

    public static Error PersistenceFailed() => Error.Failure(
        PersistenceFailedCode, "The local execution sequence could not be durably recorded.");

    public static Error PersistenceUnresolved() => Error.Failure(
        PersistenceUnresolvedCode, "Whether the local execution sequence change was durably recorded could not be confirmed.");
}
