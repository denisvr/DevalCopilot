using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the explicit abandonment of a manual run (ADR-0031). No error carries a path, a reason, a
/// stored value, an identifier of other work or an exception. The same codes are the refusal facts of the read-only status.</summary>
public static class RunAbandonmentErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotManualCode = "run_abandonment.run_not_manual";
    public const string RunNotAbandonableCode = "run_abandonment.run_not_abandonable";
    public const string ActiveAttemptCode = "run_abandonment.active_attempt";
    public const string ActiveVerificationCode = "run_abandonment.active_verification";
    public const string LocalCommitOpenCode = "run_abandonment.local_commit_open";
    public const string WorkspaceBusyCode = "run_abandonment.workspace_busy";
    public const string AlreadyAbandonedCode = "run_abandonment.already_abandoned";
    public const string ReasonConflictCode = "run_abandonment.reason_conflict";
    public const string AbandonmentIncoherentCode = "run_abandonment.abandonment_incoherent";
    public const string ConcurrentChangeCode = "run_abandonment.concurrent_change";
    public const string InvalidReasonCode = "validation.invalid";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "The requested run was not found.");

    public static Error InvalidReason() => Error.Failure(
        InvalidReasonCode, "The reason needs text, no control characters other than line feeds, and at most 2 KiB.");

    public static Error RunNotManual() => Error.Conflict(
        RunNotManualCode, "Only a manual Agent run can be abandoned.");

    public static Error RunNotAbandonable() => Error.Conflict(
        RunNotAbandonableCode, "Only a created or running manual run can be abandoned.");

    public static Error ActiveAttempt() => Error.Conflict(
        ActiveAttemptCode, "An Agent or process attempt of this project is still active or in an unknown state.");

    public static Error ActiveVerification() => Error.Conflict(
        ActiveVerificationCode, "A verification execution of this project is still active or in an unknown state.");

    public static Error LocalCommitOpen() => Error.Conflict(
        LocalCommitOpenCode, "A local-commit operation of this project is not finished or is in an unknown state.");

    public static Error WorkspaceBusy() => Error.Conflict(
        WorkspaceBusyCode, "A workspace of this project is being prepared or committed, or is in an unknown state.");

    public static Error AlreadyAbandoned() => Error.Conflict(
        AlreadyAbandonedCode, "This run was already abandoned.");

    public static Error ReasonConflict() => Error.Conflict(
        ReasonConflictCode, "This run was already abandoned with a different reason.");

    public static Error AbandonmentIncoherent() => Error.Conflict(
        AbandonmentIncoherentCode, "This run's recorded abandonment facts are not coherent, so it cannot be treated as abandoned.");

    public static Error ConcurrentChange() => Error.Conflict(
        ConcurrentChangeCode, "The run changed while the abandonment was being recorded; read its status and try again.");
}
