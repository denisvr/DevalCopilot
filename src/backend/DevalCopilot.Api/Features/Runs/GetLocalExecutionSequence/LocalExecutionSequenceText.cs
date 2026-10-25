using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>The fixed English text of the closed local-execution-sequence reason, next-action and arming-refusal codes (ADR-0060). No
/// stored text, provider text, identifier, path or exception is ever echoed through these strings.</summary>
internal static class LocalExecutionSequenceText
{
    public static string? ReasonText(string? reasonCode) => reasonCode switch
    {
        LocalExecutionSequenceReason.ReviewApproved =>
            "An ordinary review approved the exact linked report. The cycle is complete; Human approval and commit remain separate.",
        LocalExecutionSequenceReason.AllowanceReached =>
            "The automatic correction allowance was reached with unresolved findings; a Human decision is required.",
        LocalExecutionSequenceReason.DiagnosisEscalated =>
            "The verification diagnosis escalated; a Human decision is required.",
        LocalExecutionSequenceReason.StoppedBeforeClaim =>
            "Stopped before the next operation was claimed; nothing was claimed or consumed.",
        LocalExecutionSequenceReason.StoppedAfterOperation =>
            "Stopped after the claimed operation finished; no successor was started.",
        LocalExecutionSequenceReason.PreparationRefused =>
            "The next operation's claim was refused; the sequence stopped without retrying.",
        LocalExecutionSequenceReason.PreparationExpired =>
            "The next operation was not claimed within two minutes of its reservation; nothing was claimed.",
        LocalExecutionSequenceReason.PreparationFailed =>
            "Preparing the next operation failed; nothing was retried.",
        LocalExecutionSequenceReason.PreparationUnresolved =>
            "Whether the next operation was claimed could not be confirmed; nothing was retried.",
        LocalExecutionSequenceReason.ResultDeadlineElapsed =>
            "The result deadline elapsed. No process was cancelled; the operation stays visible and owned until it is reconciled.",
        LocalExecutionSequenceReason.AdvancementDeadlineElapsed =>
            "The two-hour advancement deadline elapsed; no further operation is started.",
        LocalExecutionSequenceReason.OperationFailed =>
            "A claimed operation did not complete as expected; no successor was started.",
        LocalExecutionSequenceReason.OperationOutcomeInvalid =>
            "An operation's recorded result could not be proved; no successor was started.",
        LocalExecutionSequenceReason.ExitEvidenceMissing =>
            "A dispatched operation ended without recorded process exit evidence. Its exact operation stays owned until the original host records termination evidence and a separate Human release follows; without that evidence there is no supported recovery action.",
        LocalExecutionSequenceReason.VerificationNotDiagnosable =>
            "The verification pass is neither fully passed nor a coherent failure set; nothing was diagnosed or corrected.",
        LocalExecutionSequenceReason.AuthorityChanged =>
            "The pinned source, plan, recipes or settings changed; the sequence did not continue.",
        LocalExecutionSequenceReason.CapacityRefused =>
            "The workspace or global capacity did not allow the claimed operation to dispatch; the sequence stopped without waiting.",
        LocalExecutionSequenceReason.HostRestarted =>
            "The host restarted; the sequence was interrupted and never resumes automatically.",
        _ => null,
    };

    public static string WaitingReasonText(string reason) => reason switch
    {
        LocalExecutionWaitingReason.AwaitingAdmissionCheck => "Queued. No admission check has been recorded yet; nothing was started.",
        LocalExecutionWaitingReason.WaitingForWorkspace =>
            "Recorded: another execution owned this project's workspace at the last admission check. Nothing was started.",
        LocalExecutionWaitingReason.WaitingForCapacity =>
            "Recorded: the global mutating capacity was at its limit at the last admission check. Nothing was started.",
        LocalExecutionWaitingReason.WaitingForEarlierEligibleWork =>
            "Recorded: an earlier queued step that competes for the same workspace or capacity is admitted first. Nothing was started.",
        LocalExecutionWaitingReason.WaitingForPreparationCapacity =>
            "Recorded: no queued preparation slot was available at the last admission check. Nothing was started.",
        _ => "Recorded: whether the workspace or capacity was free could not be judged safely. Nothing was started.",
    };

    public static string NextActionText(string? nextActionCode) => nextActionCode switch
    {
        "wait_or_stop_after_current_operation" => "Wait, or choose Stop after current operation.",
        "wait_for_current_operation_to_finish" => "Wait for the current operation to finish.",
        "review_and_decide_on_human_approval_separately" =>
            "Review the approved report. Human approval and commit each need their own separate explicit request.",
        "decide_on_the_recorded_escalation" => "Review the recorded escalation and decide separately.",
        "continue_with_explicit_manual_work" => "Continue with explicit manual requests.",
        "no_supported_recovery_action_in_this_version" => "No automatic recovery. A separate Human release is possible only with recorded original-host termination evidence; without it there is no supported recovery action.",
        _ => "Inspect the recorded operations and reconcile before any further work.",
    };

    public static string? RefusalText(string? refusalCode) => refusalCode switch
    {
        LocalExecutionSequenceErrors.AlreadyArmedCode => LocalExecutionSequenceErrors.AlreadyArmed().Description,
        LocalExecutionSequenceErrors.RunNotManualCode => LocalExecutionSequenceErrors.RunNotManual().Description,
        LocalExecutionSequenceErrors.RunNotRunningCode => LocalExecutionSequenceErrors.RunNotRunning().Description,
        LocalExecutionSequenceErrors.PriorMutationCode => LocalExecutionSequenceErrors.PriorMutation().Description,
        LocalExecutionSequenceErrors.PlanNotFoundCode => LocalExecutionSequenceErrors.PlanNotFound().Description,
        LocalExecutionSequenceErrors.PlanNotEligibleCode => LocalExecutionSequenceErrors.PlanNotEligible().Description,
        LocalExecutionSequenceErrors.PlanningActiveCode => LocalExecutionSequenceErrors.PlanningActive().Description,
        LocalExecutionSequenceErrors.ProjectBusyCode => LocalExecutionSequenceErrors.ProjectBusy().Description,
        LocalExecutionSequenceErrors.WorkspaceNotReadyCode => LocalExecutionSequenceErrors.WorkspaceNotReady().Description,
        LocalExecutionSequenceErrors.LeaseNotActiveCode => LocalExecutionSequenceErrors.LeaseNotActive().Description,
        LocalExecutionSequenceErrors.CheckpointMissingCode => LocalExecutionSequenceErrors.CheckpointMissing().Description,
        LocalExecutionSequenceErrors.WorkspaceOwnedCode => LocalExecutionSequenceErrors.WorkspaceOwned().Description,
        LocalExecutionSequenceErrors.CapacityUnavailableCode => LocalExecutionSequenceErrors.CapacityUnavailable().Description,
        LocalExecutionSequenceErrors.QueueFullCode => LocalExecutionSequenceErrors.QueueFull().Description,
        LocalExecutionSequenceErrors.RecipesMissingCode => LocalExecutionSequenceErrors.RecipesMissing().Description,
        LocalExecutionSequenceErrors.RecipesTooManyCode => LocalExecutionSequenceErrors.RecipesTooMany().Description,
        LocalExecutionSequenceErrors.RecipesIncoherentCode => LocalExecutionSequenceErrors.RecipesIncoherent().Description,
        LocalExecutionSequenceErrors.SettingsUnavailableCode => LocalExecutionSequenceErrors.SettingsUnavailable().Description,
        null => null,
        _ => "Arming is not available.",
    };
}
