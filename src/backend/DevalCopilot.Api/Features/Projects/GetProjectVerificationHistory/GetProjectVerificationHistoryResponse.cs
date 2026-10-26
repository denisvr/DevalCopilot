namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// One keyset page of recorded project verification executions, newest first (ADR-0069). <c>Status</c> is <c>Recorded</c> (a possibly
/// empty, strictly descending page of at most 20 items; <c>NextBeforeExecutionNumber</c> is the last displayed number exactly when
/// <c>HasMore</c>) or <c>Unavailable</c> (no item, <c>HasMore</c> false and no cursor; this is not an assertion that history is exhausted).
/// A page is an independent recorded read, not a snapshot of the whole project.
/// </summary>
public sealed record GetProjectVerificationHistoryResponse(
    Guid ProjectId,
    string Status,
    IReadOnlyList<ProjectVerificationHistoryItemResponse> Items,
    bool HasMore,
    int? NextBeforeExecutionNumber);
