namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The bounded scalar facts of one recorded step, classified by the database. <see cref="KindCode"/> is 1 to 6 (the Domain order plus one)
/// and <see cref="StateCode"/> 1 to 5 for a known value and 0 otherwise. A number is null unless the stored value is an exact integer in
/// its supported range; <see cref="RecipePresent"/> tells a rejected recipe position from an absent one. The preparation owner token is
/// never selected.
/// </summary>
internal sealed record LocalExecutionInspectionStepRow(
    Guid Id,
    Guid SequenceId,
    int? Ordinal,
    int KindCode,
    int? PassNumber,
    int? RecipeOrdinal,
    bool RecipePresent,
    Guid? PredecessorStepId,
    Guid SourceCheckpointId,
    string? SourceFingerprint,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    int StateCode,
    Guid ReservedOperationId,
    Guid? OperationId,
    DateTimeOffset PreparationReservedAtUtc,
    DateTimeOffset PreparationDeadlineUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ResultDeadlineUtc,
    string? ReasonCode,
    bool ReasonOversized);
