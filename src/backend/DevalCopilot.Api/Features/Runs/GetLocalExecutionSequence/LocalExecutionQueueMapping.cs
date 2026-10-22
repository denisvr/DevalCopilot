using System.Globalization;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>The one safe mapping of a queued cycle's disclosure to its API-owned graph (ADR-0062). An unknown state, kind, reason or
/// order number is refused (the caller reports the inconsistent tuple) instead of being shown.</summary>
internal static class LocalExecutionQueueMapping
{
    /// <summary>The graph, or false when the disclosure is not internally consistent.</summary>
    public static bool TryMap(LocalExecutionQueueDisclosure? disclosure, out LocalExecutionQueueDisclosureResponse? response)
    {
        response = null;
        if (disclosure is null)
        {
            return true;
        }

        if (disclosure.AdmissionNumber < 1
            || !Enum.IsDefined(disclosure.AdmissionState)
            || !Enum.IsDefined(disclosure.Kind)
            || disclosure.AdmissionState is LocalExecutionAdmissionState.Consumed or LocalExecutionAdmissionState.Retired
            || !LocalExecutionWaitingReason.IsKnown(disclosure.WaitingReason)
            || (disclosure.Kind == LocalExecutionStepKind.Verification) != disclosure.RecipeOrdinal.HasValue)
        {
            return false;
        }

        response = new LocalExecutionQueueDisclosureResponse(
            disclosure.AdmissionId,
            disclosure.AdmissionNumber.ToString(CultureInfo.InvariantCulture),
            disclosure.AdmissionState.ToString(),
            disclosure.Kind.ToString(),
            disclosure.PassNumber,
            disclosure.RecipeOrdinal,
            Time(disclosure.EnqueuedAtUtc),
            Time(disclosure.AdvancementDeadlineUtc),
            disclosure.WaitingReason,
            LocalExecutionSequenceText.WaitingReasonText(disclosure.WaitingReason),
            Time(disclosure.WaitingReasonRecordedAtUtc));
        return true;
    }

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
