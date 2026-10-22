using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The closed disclosure of one queued cycle's current pending or reserved admission (ADR-0062): its identity and order number, the
/// pending kind, pass and recipe position, when it was queued, the original advancement deadline and the last durably recorded waiting
/// reason with its time. The reason describes the recorded admission observation, never current worker, process or slot availability,
/// and no exact global position or start time is promised.
/// </summary>
public sealed record LocalExecutionQueueDisclosure(
    Guid AdmissionId,
    long AdmissionNumber,
    LocalExecutionAdmissionState AdmissionState,
    LocalExecutionStepKind Kind,
    int PassNumber,
    int? RecipeOrdinal,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset AdvancementDeadlineUtc,
    string WaitingReason,
    DateTimeOffset WaitingReasonRecordedAtUtc);
