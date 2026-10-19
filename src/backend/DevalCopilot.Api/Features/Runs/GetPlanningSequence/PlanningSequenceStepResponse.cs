namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>One bounded step receipt: its fixed role and provider, its state, and the claimed attempt's recorded status and outcome
/// when linked. <c>Dispatched</c> says only whether a dispatch marker exists. Times are UTC round-trip strings.</summary>
public sealed record PlanningSequenceStepResponse(
    Guid StepId,
    int Ordinal,
    string Role,
    string Provider,
    string State,
    string? ReasonCode,
    Guid? AttemptId,
    int? AttemptNumber,
    string? AttemptStatus,
    string? AttemptOutcome,
    bool? Dispatched,
    string PreparationReservedAtUtc,
    string PreparationDeadlineUtc,
    string? ClaimedAtUtc,
    string? ResultDeadlineUtc);
