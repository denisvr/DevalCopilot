using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

internal static class LocalExecutionQueueDisclosures
{
    /// <summary>The closed disclosure of a pending or reserved admission, or null when the cycle has none (an Immediate cycle, or an
    /// admission that was consumed or closed).</summary>
    public static LocalExecutionQueueDisclosure? Of(LocalExecutionSequence sequence, LocalExecutionAdmission? admission) =>
        admission is null || admission.SequenceId != sequence.Id
            ? null
            : new LocalExecutionQueueDisclosure(
                admission.Id,
                admission.AdmissionNumber,
                admission.State,
                admission.Kind,
                admission.PassNumber,
                admission.RecipeOrdinal,
                admission.EnqueuedAtUtc,
                sequence.AdvancementDeadlineUtc,
                admission.WaitingReason,
                admission.WaitingReasonRecordedAtUtc);
}
