namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

/// <summary>One page of a run's recorded collaboration messages, newest first. <c>HasMore</c> is true exactly when older messages
/// remain and <c>NextBeforeSequence</c> (an exact decimal string) is then the exclusive cursor of the following page. The page is not
/// an atomic snapshot of the run and carries no total; it describes recorded content only, never live status, a current approval or
/// provider success.</summary>
public sealed record GetCollaborationHistoryResponse(
    Guid RunId, IReadOnlyList<CollaborationHistoryEntryResponse> Entries, bool HasMore, string? NextBeforeSequence);
