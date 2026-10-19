using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>One step receipt. A recorded claim and a dispatch marker are bookkeeping: neither proves a live process.</summary>
public sealed record PlanningSequenceStepReceipt(
    Guid StepId,
    int Ordinal,
    AgentRole Role,
    AgentProvider Provider,
    PlanningSequenceStepState State,
    string? ReasonCode,
    Guid? AttemptId,
    int? AttemptNumber,
    AttemptStatus? AttemptStatus,
    AgentOutcome? AttemptOutcome,
    bool? Dispatched,
    DateTimeOffset PreparationReservedAtUtc,
    DateTimeOffset PreparationDeadlineUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ResultDeadlineUtc);
