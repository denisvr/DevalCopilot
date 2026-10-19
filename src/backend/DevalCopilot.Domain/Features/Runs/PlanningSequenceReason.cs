namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The fixed, bounded reason codes of a planning sequence's terminal or attention transition (ADR-0055). The text is durable schema;
/// no stored or provider text is ever echoed through a reason.
/// </summary>
public static class PlanningSequenceReason
{
    public const string PlanAccepted = "plan_accepted";
    public const string SecondRoundEscalated = "second_round_escalated";
    public const string StoppedBeforeClaim = "stopped_before_claim";
    public const string StoppedAfterStep = "stopped_after_step";
    public const string PreparationRefused = "preparation_refused";
    public const string PreparationExpired = "preparation_expired";
    public const string PreparationFailed = "preparation_failed";
    public const string PreparationUnresolved = "preparation_unresolved";
    public const string ResultDeadlineElapsed = "result_deadline_elapsed";
    public const string StepFailed = "step_failed";
    public const string StepOutcomeInvalid = "step_outcome_invalid";
    public const string AuthorityChanged = "authority_changed";
    public const string HostRestarted = "host_restarted";

    public const int MaximumDetailLength = 96;

    public static bool IsKnown(string? reason) => reason is PlanAccepted or SecondRoundEscalated or StoppedBeforeClaim
        or StoppedAfterStep or PreparationRefused or PreparationExpired or PreparationFailed or PreparationUnresolved
        or ResultDeadlineElapsed or StepFailed or StepOutcomeInvalid or AuthorityChanged or HostRestarted;

    /// <summary>A bounded detail (an existing stable error code or closed outcome name): lowercase letters, digits, dot and
    /// underscore only. Anything else is dropped rather than stored.</summary>
    public static string? NormalizeDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail) || detail.Length > MaximumDetailLength)
        {
            return null;
        }

        foreach (var character in detail)
        {
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_'))
            {
                return null;
            }
        }

        return detail;
    }
}
