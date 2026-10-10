using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

public static class RunEventHistoryErrors
{
    public const string NotFoundCode = "runs.not_found";
    public const string UnavailableCode = "run_event_history.unavailable";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error Unavailable() =>
        Error.Conflict(UnavailableCode, "The recorded event history of this run is not available.");
}
