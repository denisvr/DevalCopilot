namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The scalar facts of the one step an admission links, as the same statement read them. A null <see cref="Id"/> is the absence of a
/// linked step row. Classification and guards follow <see cref="LocalExecutionInspectionStepRow"/>; the claim time and result deadline are
/// only tested for presence, and the preparation owner token is never selected.
/// </summary>
internal sealed record LocalExecutionAdmissionHistoryStep(
    Guid? Id,
    Guid? SequenceId,
    int? Ordinal,
    int KindCode,
    int? PassNumber,
    int? RecipeOrdinal,
    bool RecipePresent,
    Guid? PredecessorStepId,
    Guid? SourceCheckpointId,
    string? SourceFingerprint,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    int StateCode,
    Guid? ReservedOperationId,
    Guid? OperationId,
    bool ClaimedPresent,
    bool ResultDeadlinePresent,
    string? ReasonCode,
    bool ReasonOversized);
