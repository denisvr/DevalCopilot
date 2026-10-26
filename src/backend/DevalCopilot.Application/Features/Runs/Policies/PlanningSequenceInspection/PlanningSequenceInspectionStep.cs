using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>One proved recorded step. <see cref="AttemptId"/> is the recorded link, not proof of the attempt, a dispatch or any outcome;
/// those need the selected inspection and the existing Agent evidence readers. The role, provider and response contract are the fixed
/// ADR-0055 table entries of the ordinal.</summary>
public sealed record PlanningSequenceInspectionStep(
    Guid StepId,
    int Ordinal,
    AgentRole Role,
    AgentProvider Provider,
    AgentResponseContract ResponseContract,
    PlanningSequenceStepState RecordedState,
    string? ReasonCode,
    long Revision,
    Guid? PredecessorAttemptId,
    Guid? InputProposalMessageId,
    Guid? AttemptId,
    DateTimeOffset PreparationReservedAtUtc,
    DateTimeOffset PreparationDeadlineUtc,
    DateTimeOffset UpdatedAtUtc);
