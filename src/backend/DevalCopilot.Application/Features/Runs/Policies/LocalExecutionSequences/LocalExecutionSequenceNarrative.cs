using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>The fixed next-action codes of a local execution sequence state (ADR-0060). The text lives with the presentation; these
/// codes are the closed contract.</summary>
internal static class LocalExecutionSequenceNarrative
{
    public const string Wait = "wait_or_stop_after_current_operation";
    public const string WaitForDrain = "wait_for_current_operation_to_finish";
    public const string AwaitHumanApproval = "review_and_decide_on_human_approval_separately";
    public const string DecideOnEscalation = "decide_on_the_recorded_escalation";
    public const string ManualWorkAvailable = "continue_with_explicit_manual_work";
    public const string InspectAndReconcile = "inspect_and_reconcile";
    public const string NoSupportedRecovery = "no_supported_recovery_action_in_this_version";

    public static string NextActionOf(LocalExecutionSequenceState state, bool awaitingReconciliation) => state switch
    {
        LocalExecutionSequenceState.Armed or LocalExecutionSequenceState.PreparingStep or LocalExecutionSequenceState.AwaitingStepResult => Wait,
        LocalExecutionSequenceState.Stopping => WaitForDrain,
        _ when awaitingReconciliation => NoSupportedRecovery,
        LocalExecutionSequenceState.ReadyForHumanApproval => AwaitHumanApproval,
        LocalExecutionSequenceState.HumanEscalation => DecideOnEscalation,
        LocalExecutionSequenceState.Stopped => ManualWorkAvailable,
        _ => InspectAndReconcile,
    };
}
