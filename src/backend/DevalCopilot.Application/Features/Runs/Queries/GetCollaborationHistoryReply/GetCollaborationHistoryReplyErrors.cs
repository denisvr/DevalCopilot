using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

public static class GetCollaborationHistoryReplyErrors
{
    public const string NotFoundCode = "collaboration_reply.not_found";
    public const string UnavailableCode = "collaboration_reply.unavailable";

    /// <summary>One fixed answer for an unknown run and for a missing or foreign source, so neither is distinguishable.</summary>
    public static Error NotFound() =>
        Error.NotFound(NotFoundCode, "The requested recorded collaboration message was not found.");

    public static Error Unavailable() =>
        Error.Conflict(UnavailableCode, "The recorded reply relationship of this message is not available.");
}
