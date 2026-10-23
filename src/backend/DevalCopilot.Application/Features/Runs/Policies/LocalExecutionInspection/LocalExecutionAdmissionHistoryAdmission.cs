namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The scalar facts of one recorded admission as one statement read them, classified by the database. A null <see cref="Id"/> is the
/// absence of any admission row. <see cref="KindCode"/> is 1 to 6, <see cref="StateCode"/> 1 to 5 and <see cref="WaitingReasonCode"/> 1 to
/// 6 for a known value and 0 otherwise; a number is null unless the stored value is an exact integer in its supported range and
/// <see cref="RecipePresent"/> tells a rejected recipe position from an absent one. An order number or revision is a positive Int64.
/// </summary>
internal sealed record LocalExecutionAdmissionHistoryAdmission(
    Guid? Id,
    Guid? SequenceId,
    long? Number,
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
    Guid? StepId,
    int WaitingReasonCode,
    DateTimeOffset? WaitingReasonRecordedAtUtc,
    DateTimeOffset? EnqueuedAtUtc,
    string? ReasonCode,
    bool ReasonOversized,
    long? Revision,
    DateTimeOffset? UpdatedAtUtc);
