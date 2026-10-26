namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>One recorded verification execution of a history page: its exact identity, positive project-scoped number, the command name
/// of its immutable snapshot (never the current recipe) and its UTC claim time as a round-trip string.</summary>
public sealed record ProjectVerificationHistoryItemResponse(Guid ExecutionId, int ExecutionNumber, string CommandName, string ClaimedAtUtc);
