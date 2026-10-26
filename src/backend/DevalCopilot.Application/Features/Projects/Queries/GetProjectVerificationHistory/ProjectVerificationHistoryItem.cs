namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

/// <summary>One recorded verification execution of the page: its exact identity, positive project-scoped number, the command name of its
/// immutable snapshot (never the current recipe) and its claim time. No run attribution, source applicability or outcome is implied.</summary>
public sealed record ProjectVerificationHistoryItem(Guid ExecutionId, int ExecutionNumber, string CommandName, DateTimeOffset ClaimedAtUtc);
