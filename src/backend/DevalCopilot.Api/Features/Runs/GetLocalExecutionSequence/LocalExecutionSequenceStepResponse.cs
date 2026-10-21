namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>One bounded step receipt: its fixed kind, pass and recipe position, its state and the linked operation's recorded status and
/// outcome. <c>Dispatched</c> says only whether a dispatch marker exists, <c>ExitRecorded</c> only whether coherent host Exited evidence
/// exists (never proof about descendants) and <c>Quarantined</c> that a terminal dispatched operation without that evidence stays owned.
/// The recipe name is untrusted text and is rendered inert. Times are UTC round-trip strings.</summary>
public sealed record LocalExecutionSequenceStepResponse(
    Guid StepId,
    int Ordinal,
    string Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    string State,
    string? ReasonCode,
    Guid? OperationId,
    string? OperationStatus,
    string? OperationOutcome,
    bool? Dispatched,
    bool? ExitRecorded,
    bool Quarantined,
    string PreparationReservedAtUtc,
    string PreparationDeadlineUtc,
    string? ClaimedAtUtc,
    string? ResultDeadlineUtc);
