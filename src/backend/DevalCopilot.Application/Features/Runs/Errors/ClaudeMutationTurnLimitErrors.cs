using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Errors;

/// <summary>Stable, non-echoing errors of the Claude mutation turn-limit request: its set operation and
/// the claim-time snapshot. No error carries a stored value, a provider payload, or an exception.</summary>
public static class ClaudeMutationTurnLimitErrors
{
    public const string RunNotFoundCode = "runs.not_found";
    public const string RunNotEditableCode = "claude_turn_limit.run_not_editable";
    public const string ConcurrentChangeCode = "claude_turn_limit.concurrent_change";
    public const string PersistedLimitInvalidCode = "agent_attempts.claude_turn_limit_invalid";

    public static Error RunNotFound() => Error.NotFound(RunNotFoundCode, "This run does not exist.");

    public static Error RunNotEditable() =>
        Error.Failure(RunNotEditableCode, "This run's Claude turn-limit request can no longer be changed.");

    public static Error ConcurrentChange() =>
        Error.Conflict(ConcurrentChangeCode, "The run's Claude turn-limit request changed concurrently; retry the request.");

    /// <summary>The run's stored request is outside the accepted range. It is refused, never clamped or
    /// silently omitted, so no attempt is claimed with a different limit than the one recorded.</summary>
    public static Error PersistedLimitInvalid() => Error.Failure(
        PersistedLimitInvalidCode, "The run's stored Claude turn-limit request is not valid, so no attempt was claimed.");
}
