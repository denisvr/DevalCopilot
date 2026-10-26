namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

/// <summary>The page, in strictly descending execution-number order. <paramref name="NextBeforeExecutionNumber"/> is the last displayed number
/// exactly when <paramref name="HasMore"/>; an Unavailable page has no items, no further page and no cursor.</summary>
public sealed record GetProjectVerificationHistoryQueryResult(
    Guid ProjectId,
    ProjectVerificationHistoryStatus Status,
    IReadOnlyList<ProjectVerificationHistoryItem> Items,
    bool HasMore,
    int? NextBeforeExecutionNumber);
