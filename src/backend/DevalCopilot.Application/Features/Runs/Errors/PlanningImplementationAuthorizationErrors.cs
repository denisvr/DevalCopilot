using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing errors of the explicit human authorization of one implementation claim for an
/// escalated final plan (ADR-0016), at the authorization, claim, and dispatch seams. None carries submitted or stored
/// rationale text, a manifest, an identifier of foreign evidence, or a provider payload.</summary>
public static class PlanningImplementationAuthorizationErrors
{
    public const string RationaleInvalidCode = "planning_authorizations.rationale_invalid";
    public const string SourceNotFoundCode = "planning_authorizations.source_not_found";
    public const string SourceInvalidCode = "planning_authorizations.source_invalid";
    public const string SourceStaleCode = "planning_authorizations.source_stale";
    public const string ContextNotCurrentCode = "planning_authorizations.context_not_current";
    public const string RationaleConflictCode = "planning_authorizations.rationale_conflict";
    public const string RecordedInvalidCode = "planning_authorizations.recorded_invalid";
    public const string AlreadyConsumedCode = "planning_authorizations.already_consumed";
    public const string EventMissingCode = "planning_authorizations.event_missing";
    public const string ClaimInvalidCode = "agent_attempts.planning_authorization_invalid";
    public const string ClaimConsumedCode = "agent_attempts.planning_authorization_consumed";
    public const string ClaimStaleCode = "agent_attempts.planning_authorization_stale";
    public const string DispatchMismatchCode = "agent_attempts.planning_authorization_mismatch";

    public static string RationaleInvalidMessage { get; } =
        $"The rationale must be non-blank text of at most {Domain.Features.Runs.PlanningImplementationInstruction.MaximumLength} characters "
        + "without control characters or unsafe content.";

    public static Error RationaleInvalid() => Error.Failure(RationaleInvalidCode, RationaleInvalidMessage);

    public static Error SourceNotFound() => Error.NotFound(
        SourceNotFoundCode, "The requested planning escalation was not found for this run.");

    public static Error SourceInvalid() => Error.Conflict(
        SourceInvalidCode, "The planning escalation and its proposal lineage are not a valid, complete, provider-observed chain.");

    public static Error SourceStale() => Error.Conflict(
        SourceStaleCode, "The planning escalation no longer belongs to the run's current planning lineage and checkpoint.");

    public static Error ContextNotCurrent() => Error.Conflict(
        ContextNotCurrentCode, "The run, its workspace, lease, or checkpoint no longer permit this authorization.");

    public static Error RationaleConflict() => Error.Conflict(
        RationaleConflictCode, "An authorization with a different rationale already exists for this escalation.");

    public static Error RecordedInvalid() => Error.Conflict(
        RecordedInvalidCode, "The recorded authorization could not be validated.");

    public static Error AlreadyConsumed() => Error.Conflict(
        AlreadyConsumedCode, "The authorization for this escalation was already used by an implementation claim and is not renewed.");

    public static Error EventMissing() => Error.Failure(
        EventMissingCode, "The persisted authorization event could not be recovered.");

    public static Error ClaimInvalid() => Error.Conflict(
        ClaimInvalidCode, "The recorded human authorization for this plan could not be validated.");

    public static Error ClaimConsumed() => Error.Conflict(
        ClaimConsumedCode, "The human authorization for this plan was already used by an implementation claim.");

    public static Error ClaimStale() => Error.Conflict(
        ClaimStaleCode, "The human authorization for this plan no longer matches the run's current planning lineage and checkpoint.");

    public static Error DispatchMismatch() => Error.Conflict(
        DispatchMismatchCode, "The attempt's recorded human plan authorization does not match what is expected or what is sealed.");
}
