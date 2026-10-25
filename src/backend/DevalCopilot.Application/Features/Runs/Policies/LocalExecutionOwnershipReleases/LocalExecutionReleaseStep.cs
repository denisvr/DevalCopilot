namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The selected step's historical tuple. <see cref="StateCode"/> is 1 to 5 for a known state and 0 otherwise,
/// <see cref="KindCode"/> 1 to 6 for a known kind and 0 otherwise; a number is null unless it is an exact integer in its range and the
/// fingerprint is null unless its stored bytes fit. A null identity is the absence of the step.</summary>
internal sealed record LocalExecutionReleaseStep(
    Guid? Id,
    Guid? SequenceId,
    int? Ordinal,
    int KindCode,
    int? PassNumber,
    int? RecipeOrdinal,
    bool HasRecipe,
    Guid? PredecessorStepId,
    Guid? SourceCheckpointId,
    string? SourceFingerprint,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    int StateCode,
    Guid? OperationId,
    Guid? SourceCheckpointWorkspaceId,
    string? SourceCheckpointFingerprint,
    Guid? RecipeCommandId);
