namespace DevalCopilot.Api.Features.Runs.GetRunEventHistory;

/// <summary>One page of a run's recorded event headers, newest first. <c>HasMore</c> is true exactly when older headers remain and
/// <c>NextBeforeSequence</c> (an exact decimal string) is then the exclusive cursor of the following page. The page is not an atomic
/// snapshot of the run and carries no total; it describes recorded headers only, never live status, a current approval or a
/// complete replay.</summary>
public sealed record GetRunEventHistoryResponse(
    Guid RunId, IReadOnlyList<RunEventHistoryEntryResponse> Entries, bool HasMore, string? NextBeforeSequence);
