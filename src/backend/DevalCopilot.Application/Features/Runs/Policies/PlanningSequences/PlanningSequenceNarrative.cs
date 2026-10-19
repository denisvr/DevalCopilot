using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>The fixed next-action codes of a planning sequence state (ADR-0055). The text lives with the presentation; these codes are the
/// closed contract.</summary>
internal static class PlanningSequenceNarrative
{
    public const string Wait = "wait_or_stop_after_current_step";
    public const string WaitForDrain = "wait_for_current_step_to_finish";
    public const string ReviewAcceptedPlan = "review_accepted_plan_then_request_implementation_separately";
    public const string DecideOnEscalation = "decide_on_the_recorded_escalation";
    public const string ManualWorkAvailable = "continue_with_explicit_manual_work";
    public const string InspectAndReconcile = "inspect_and_reconcile";

    public static string NextActionOf(PlanningSequenceState state) => state switch
    {
        PlanningSequenceState.Armed or PlanningSequenceState.PreparingStep or PlanningSequenceState.AwaitingStepResult => Wait,
        PlanningSequenceState.Stopping => WaitForDrain,
        PlanningSequenceState.ReadyForImplementation => ReviewAcceptedPlan,
        PlanningSequenceState.AwaitingHumanDecision => DecideOnEscalation,
        PlanningSequenceState.Stopped => ManualWorkAvailable,
        _ => InspectAndReconcile,
    };
}
