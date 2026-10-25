using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

/// <summary>One step receipt. A recorded claim, a dispatch marker and a recorded exit are bookkeeping: none proves a live process, and
/// Exited is the invoked process only, never every descendant. <see cref="Quarantined"/> is a dispatched linked operation without
/// coherent host Exited evidence that stays an effective owner even when its row is terminal. <see cref="OwnershipReleased"/> (ADR-0065)
/// says that one complete coherent Human release chain removed exactly that retained quarantine; it is never true together with
/// <see cref="Quarantined"/> or <see cref="ExitRecorded"/>, and it changes neither the result nor the cycle.</summary>
public sealed record LocalExecutionSequenceStepReceipt(
    Guid StepId,
    int Ordinal,
    LocalExecutionStepKind Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    LocalExecutionSequenceStepState State,
    string? ReasonCode,
    Guid? OperationId,
    string? OperationStatus,
    string? OperationOutcome,
    bool? Dispatched,
    bool? ExitRecorded,
    bool Quarantined,
    bool OwnershipReleased,
    DateTimeOffset PreparationReservedAtUtc,
    DateTimeOffset PreparationDeadlineUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ResultDeadlineUtc);
