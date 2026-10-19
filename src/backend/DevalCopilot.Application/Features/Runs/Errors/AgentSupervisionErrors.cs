using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing refusals of the Agent supervision observation (ADR-0056). No error carries a path, a stored value, an
/// identifier of other work or an exception. An observation error is never an Agent outcome and never provider failure.</summary>
public static class AgentSupervisionErrors
{
    public const string NotFoundCode = "agent_supervision.not_found";
    public const string ObservationNotFoundCode = "agent_supervision.observation_not_found";
    public const string InvalidRequestCode = "agent_supervision.invalid_request";
    public const string NotCurrentHostCode = "agent_supervision.not_current_host";
    public const string AttemptNotObservableCode = "agent_supervision.attempt_not_observable";
    public const string ConflictCode = "agent_supervision.conflict";
    public const string NotOwnerCode = "agent_supervision.not_owner";
    public const string ClosedCode = "agent_supervision.closed";
    public const string PhaseNotForwardCode = "agent_supervision.phase_not_forward";
    public const string ChronologyInvalidCode = "agent_supervision.chronology_invalid";
    public const string PersistenceFailedCode = "agent_supervision.persistence_failed";
    public const string BodyNotAllowedCode = "agent_supervision.body_not_allowed";
    public const string QueryNotAllowedCode = "agent_supervision.query_not_allowed";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error ObservationNotFound() => Error.NotFound(
        ObservationNotFoundCode, "The requested observation was not found.");

    public static Error InvalidRequest() => new(
        InvalidRequestCode, "The request is not exactly the supported request.", ErrorType.Validation);

    public static Error NotCurrentHost() => Error.Conflict(
        NotCurrentHostCode, "Only the host launch that owns an observation may write it.");

    public static Error AttemptNotObservable() => Error.Conflict(
        AttemptNotObservableCode, "Only a coherent, running Agent attempt can be observed.");

    public static Error Conflict() => Error.Conflict(
        ConflictCode, "The attempt already has a different observation; it is never replaced or adopted.");

    public static Error NotOwner() => Error.Conflict(
        NotOwnerCode, "Only the original observer of the original host may write this observation.");

    public static Error Closed() => Error.Conflict(ClosedCode, "The observation is closed and never changes again.");

    public static Error PhaseNotForward() => Error.Conflict(
        PhaseNotForwardCode, "An observation phase moves forward only, once, with a representable deadline.");

    public static Error ChronologyInvalid() => Error.Conflict(
        ChronologyInvalidCode, "The recorded times are not coherent with the clock; the observation is not fresh.");

    public static Error PersistenceFailed() => Error.Failure(
        PersistenceFailedCode, "The observation could not be durably recorded.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request accepts no query string.", ErrorType.Validation);
}
