namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The historical reference facts of one admission, read by the same statement: its source checkpoint's workspace and fingerprint, the
/// run and kind of its report message, and the pinned recipe at its position. A null identity is the absence of that row; nothing is
/// substituted from a latest row.
/// </summary>
internal sealed record LocalExecutionAdmissionHistoryReference(
    Guid? SourceCheckpointWorkspaceId,
    string? SourceCheckpointFingerprint,
    Guid? ReportRunId,
    bool ReportIsExecutionReport,
    Guid? RecipeCommandId,
    string? RecipeName,
    bool RecipeNameOversized);
