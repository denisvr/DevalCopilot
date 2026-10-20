using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>Stable, non-echoing refusals of the global execution capacity ceiling (ADR-0058). None carries a path, an identifier of other
/// work, a stored value or an exception, and none is a workspace loss, a provider failure or a terminal outcome.</summary>
public static class ExecutionCapacityErrors
{
    public const string LimitReachedCode = "execution_capacity.limit_reached";
    public const string UnavailableCode = "execution_capacity.unavailable";
    public const string BodyNotAllowedCode = "execution_capacity.body_not_allowed";
    public const string QueryNotAllowedCode = "execution_capacity.query_not_allowed";

    public static Error LimitReached() => Error.Conflict(
        LimitReachedCode, "Two potentially mutating executions are already recorded as active; wait for one to finish.");

    public static Error Unavailable() => Error.Conflict(
        UnavailableCode, "The recorded active executions cannot be counted safely; inspect the recorded executions.");

    public static Error BodyNotAllowed() => new(
        BodyNotAllowedCode, "This request accepts no request body.", ErrorType.Validation);

    public static Error QueryNotAllowed() => new(
        QueryNotAllowedCode, "This request accepts no query string.", ErrorType.Validation);
}
