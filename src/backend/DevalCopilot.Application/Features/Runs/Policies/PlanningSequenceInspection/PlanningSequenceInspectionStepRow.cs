namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// The bounded scalar facts of one recorded step, classified by the database. Identifiers and times are raw stored bytes withheld above
/// their bound, and a <c>Present</c> flag tells a rejected value from an absent one. <see cref="RoleCode"/> is 1 to 3 (Planner,
/// CriticalReviewer, Resolver) and <see cref="StateCode"/> 1 to 5 for a known value and 0 otherwise. A number is null unless the stored
/// value is an exact integer in its supported range. The preparation owner token is never selected.
/// </summary>
internal sealed record PlanningSequenceInspectionStepRow(
    byte[]? Id,
    byte[]? SequenceId,
    int? Ordinal,
    int RoleCode,
    int StateCode,
    byte[]? PredecessorAttemptId,
    bool PredecessorPresent,
    byte[]? InputProposalMessageId,
    bool InputProposalPresent,
    byte[]? ReservedAttemptId,
    byte[]? AttemptId,
    bool AttemptPresent,
    byte[]? PreparationReservedAtUtc,
    byte[]? PreparationDeadlineUtc,
    string? ReasonCode,
    bool ReasonOversized,
    long? Revision,
    byte[]? UpdatedAtUtc);
