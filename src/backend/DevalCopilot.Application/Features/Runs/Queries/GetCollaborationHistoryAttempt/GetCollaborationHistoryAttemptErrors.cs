using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

public static class GetCollaborationHistoryAttemptErrors
{
    public const string NotFoundCode = "collaboration_attempt.not_found";
    public const string UnavailableCode = "collaboration_attempt.unavailable";

    /// <summary>One fixed answer for an unknown run and for a missing or foreign source, so neither is distinguishable.</summary>
    public static Error NotFound() =>
        Error.NotFound(NotFoundCode, "The requested recorded collaboration message was not found.");

    public static Error Unavailable() =>
        Error.Conflict(UnavailableCode, "The recorded attempt attribution of this message is not available.");
}
