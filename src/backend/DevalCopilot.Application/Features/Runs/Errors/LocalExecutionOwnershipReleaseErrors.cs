using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the Human release of a terminal local operation's quarantine (ADR-0065). No error carries a
/// path, a stored value, an identifier, a host token, a ticket, a nonce or a rejected request value; one fixed 404 covers an absent or
/// foreign run or step.</summary>
public static class LocalExecutionOwnershipReleaseErrors
{
    public const string NotFoundCode = "local_execution_ownership_release.not_found";
    public const string InvalidRequestCode = "local_execution_ownership_release.invalid_request";
    public const string BodyTooLargeCode = "local_execution_ownership_release.body_too_large";
    public const string ConfirmationStaleCode = "local_execution_ownership_release.confirmation_stale";
    public const string NotEligibleCode = "local_execution_ownership_release.not_eligible";
    public const string UnavailableCode = "local_execution_ownership_release.unavailable";
    public const string ConfirmationConflictCode = "local_execution_ownership_release.confirmation_conflict";
    public const string PersistenceFailedCode = "local_execution_ownership_release.persistence_failed";
    public const string PersistenceUnresolvedCode = "local_execution_ownership_release.persistence_unresolved";
    public const string ObservationRefusedCode = "local_execution_ownership_release.observation_refused";

    public static Error NotFound() => Error.NotFound(
        NotFoundCode, "The requested local execution step was not found for this run.");

    public static Error InvalidRequest() => new(
        InvalidRequestCode, "The request is not exactly the supported ownership release confirmation.", ErrorType.Validation);

    public static Error BodyTooLarge() => new(
        BodyTooLargeCode, "The request body exceeds the supported size.", ErrorType.Validation);

    public static Error ConfirmationStale() => Error.Conflict(
        ConfirmationStaleCode,
        "The confirmation expired, or the recorded facts changed after it was read; read the release candidate again.");

    public static Error NotEligible() => Error.Conflict(
        NotEligibleCode, "This operation's ownership cannot be released: it does not retain a quarantine this control may release.");

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode,
        "The recorded original-host termination evidence for this operation is missing, incomplete or cannot be trusted; this request recorded nothing, and the live workspace and capacity readings show what is held.");

    public static Error ConfirmationConflict() => Error.Conflict(
        ConfirmationConflictCode, "This operation's ownership was already released under a different confirmation.");

    public static Error PersistenceFailed() => Error.Failure(
        PersistenceFailedCode, "The ownership release could not be durably recorded.");

    public static Error PersistenceUnresolved() => Error.Failure(
        PersistenceUnresolvedCode, "Whether the ownership release was durably recorded could not be confirmed; read the candidate again.");

    /// <summary>The recorder refused the supplied physical termination observation: it is foreign to, or contradicts, the dispatch
    /// witness, the operation or the returned process result. Nothing of the ordinary result is recorded by that transaction.</summary>
    public static Error ObservationRefused() => Error.Conflict(
        ObservationRefusedCode, "The supplied physical termination evidence does not belong to this operation's original dispatch.");
}
