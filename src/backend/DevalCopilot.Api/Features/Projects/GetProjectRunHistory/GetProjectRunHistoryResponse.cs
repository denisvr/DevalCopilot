namespace DevalCopilot.Api.Features.Projects.GetProjectRunHistory;

/// <summary>One page of a project's recorded runs, newest first. <c>HasMore</c> is true exactly when older runs remain and
/// <c>NextBeforeExecutionNumber</c> is then the exclusive cursor of the following page; both describe recorded history only, never
/// live progress, the current workspace, current verification or any remote publication.</summary>
public sealed record GetProjectRunHistoryResponse(
    Guid ProjectId, IReadOnlyList<ProjectRunHistoryEntryResponse> Entries, bool HasMore, int? NextBeforeExecutionNumber);
