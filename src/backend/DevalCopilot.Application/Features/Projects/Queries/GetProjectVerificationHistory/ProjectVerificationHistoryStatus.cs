namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

/// <summary><see cref="Recorded"/>: a possibly empty, strictly descending page of at most 20 admitted executions. <see cref="Unavailable"/>:
/// a selected row or the lookahead key could not be admitted; no item, no further page and no cursor, and its absent next page is not an
/// assertion that history is exhausted.</summary>
public enum ProjectVerificationHistoryStatus
{
    Recorded,
    Unavailable,
}
