using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>The fixed English text of the closed planning-sequence reason, next-action and arming-refusal codes (ADR-0055). No stored
/// text, provider text, identifier or exception is ever echoed through these strings.</summary>
internal static class PlanningSequenceText
{
    public static string? ReasonText(string? reasonCode) => reasonCode switch
    {
        PlanningSequenceReason.PlanAccepted =>
            "A critical review accepted the plan. Planning is complete; implementation needs its own separate explicit request.",
        PlanningSequenceReason.SecondRoundEscalated =>
            "The second challenge-resolution round finished and needs a Human decision.",
        PlanningSequenceReason.StoppedBeforeClaim =>
            "Stopped before the next step was claimed; nothing was claimed or consumed.",
        PlanningSequenceReason.StoppedAfterStep =>
            "Stopped after the claimed step finished; no successor was started.",
        PlanningSequenceReason.PreparationRefused =>
            "The next step's claim was refused; the sequence stopped without retrying.",
        PlanningSequenceReason.PreparationExpired =>
            "The next step was not claimed within two minutes of its reservation; nothing was claimed.",
        PlanningSequenceReason.PreparationFailed =>
            "Preparing the next step failed; nothing was retried.",
        PlanningSequenceReason.PreparationUnresolved =>
            "Whether the next step was claimed could not be confirmed; nothing was retried.",
        PlanningSequenceReason.ResultDeadlineElapsed =>
            "The result deadline elapsed. No process was cancelled; the step's attempt stays visible until it is reconciled.",
        PlanningSequenceReason.StepFailed =>
            "A claimed step did not complete as expected; no successor was started.",
        PlanningSequenceReason.StepOutcomeInvalid =>
            "A step's recorded result could not be proved; no successor was started.",
        PlanningSequenceReason.AuthorityChanged =>
            "The pinned source, workspace or objective changed; the sequence did not continue.",
        PlanningSequenceReason.HostRestarted =>
            "The host restarted; the sequence was interrupted and never resumes automatically.",
        _ => null,
    };

    public static string NextActionText(string? nextActionCode) => nextActionCode switch
    {
        "wait_or_stop_after_current_step" => "Wait, or choose Stop after current step.",
        "wait_for_current_step_to_finish" => "Wait for the current step to finish.",
        "review_accepted_plan_then_request_implementation_separately" =>
            "Review the accepted plan. Implementation requires its own separate explicit request.",
        "decide_on_the_recorded_escalation" => "Review the recorded escalation and decide separately.",
        "continue_with_explicit_manual_work" => "Continue with explicit manual requests.",
        _ => "Inspect the recorded attempts and reconcile before any further work.",
    };

    public static string? ArmingRefusalText(string? refusalCode) => refusalCode switch
    {
        PlanningSequenceErrors.RunNotManualCode => PlanningSequenceErrors.RunNotManual().Description,
        PlanningSequenceErrors.RunNotPristineCode => PlanningSequenceErrors.RunNotPristine().Description,
        PlanningSequenceErrors.ProjectBusyCode => PlanningSequenceErrors.ProjectBusy().Description,
        PlanningSequenceErrors.WorkspaceNotReadyCode => PlanningSequenceErrors.WorkspaceNotReady().Description,
        PlanningSequenceErrors.LeaseNotActiveCode => PlanningSequenceErrors.LeaseNotActive().Description,
        PlanningSequenceErrors.CheckpointMissingCode => PlanningSequenceErrors.CheckpointMissing().Description,
        null => null,
        _ => "Arming is not available.",
    };
}
