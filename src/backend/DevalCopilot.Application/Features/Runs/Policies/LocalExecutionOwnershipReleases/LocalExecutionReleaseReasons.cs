namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The fixed wire code of each release reason (ADR-0065). One snake_case code per reason, carrying no stored value.</summary>
internal static class LocalExecutionReleaseReasons
{
    public static string? CodeOf(LocalExecutionReleaseReason reason) => reason switch
    {
        LocalExecutionReleaseReason.StepNotLinked => "step_not_linked",
        LocalExecutionReleaseReason.OperationNotTerminal => "operation_not_terminal",
        LocalExecutionReleaseReason.NotDispatched => "not_dispatched",
        LocalExecutionReleaseReason.AlreadyFree => "already_free",
        LocalExecutionReleaseReason.SequenceNotEnded => "sequence_not_ended",
        LocalExecutionReleaseReason.EvidenceMissing => "evidence_missing",
        LocalExecutionReleaseReason.EvidenceIncoherent => "evidence_incoherent",
        _ => null,
    };
}
