namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>The closed disclosure of one queued cycle's pending or reserved admission (ADR-0062). <c>AdmissionNumber</c> is a canonical
/// Int64 string and every time a UTC round-trip string. <c>WaitingReason</c> is one of AwaitingAdmissionCheck, WaitingForWorkspace,
/// WaitingForCapacity, WaitingForEarlierEligibleWork, WaitingForPreparationCapacity and Unavailable, and describes the last durably
/// recorded admission observation (its time is <c>WaitingReasonRecordedAtUtc</c>), never current worker, process or slot availability.
/// No exact global position or start time is promised.</summary>
public sealed record LocalExecutionQueueDisclosureResponse(
    Guid AdmissionId,
    string AdmissionNumber,
    string AdmissionState,
    string Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string EnqueuedAtUtc,
    string AdvancementDeadlineUtc,
    string WaitingReason,
    string WaitingReasonText,
    string WaitingReasonRecordedAtUtc);
