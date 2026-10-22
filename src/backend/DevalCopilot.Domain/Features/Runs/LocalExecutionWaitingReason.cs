namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, fixed reasons a pending admission records for why it is waiting (ADR-0062). The text is durable schema. A reason
/// describes the last durably recorded admission observation, not present worker, process or slot availability.
/// </summary>
public static class LocalExecutionWaitingReason
{
    public const string AwaitingAdmissionCheck = "AwaitingAdmissionCheck";
    public const string WaitingForWorkspace = "WaitingForWorkspace";
    public const string WaitingForCapacity = "WaitingForCapacity";
    public const string WaitingForEarlierEligibleWork = "WaitingForEarlierEligibleWork";
    public const string WaitingForPreparationCapacity = "WaitingForPreparationCapacity";
    public const string Unavailable = "Unavailable";

    public static bool IsKnown(string? reason) => reason is AwaitingAdmissionCheck or WaitingForWorkspace or WaitingForCapacity
        or WaitingForEarlierEligibleWork or WaitingForPreparationCapacity or Unavailable;
}
