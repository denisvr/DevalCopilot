namespace DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

/// <summary>
/// One recorded step: its fixed role, provider and response contract, its recorded state and fixed reason, the predecessor attempt and
/// input Proposal it names and its recorded attempt link. <c>AttemptId</c> is a recorded link, not proof of the attempt, a dispatch or any
/// outcome; those need the selected inspection and the existing Agent evidence. <c>Revision</c> is a canonical Int64 string and times are
/// UTC round-trip strings.
/// </summary>
public sealed record PlanningSequenceInspectionStepResponse(
    Guid StepId,
    int Ordinal,
    string Role,
    string Provider,
    string ResponseContract,
    string RecordedState,
    string? ReasonCode,
    string Revision,
    Guid? PredecessorAttemptId,
    Guid? InputProposalMessageId,
    Guid? AttemptId,
    string PreparationReservedAtUtc,
    string PreparationDeadlineUtc,
    string UpdatedAtUtc);
