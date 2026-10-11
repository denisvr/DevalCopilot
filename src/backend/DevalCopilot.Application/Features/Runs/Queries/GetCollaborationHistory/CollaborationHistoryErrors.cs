using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

public static class CollaborationHistoryErrors
{
    public const string NotFoundCode = "runs.not_found";
    public const string UnavailableCode = "collaboration_history.unavailable";

    public static Error NotFound() => Error.NotFound(NotFoundCode, "The requested run was not found.");

    public static Error Unavailable() =>
        Error.Conflict(UnavailableCode, "The recorded collaboration history of this run is not available.");
}
