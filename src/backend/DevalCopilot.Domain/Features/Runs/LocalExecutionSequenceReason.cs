namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The fixed, bounded reason codes of a local execution sequence's terminal or attention transition (ADR-0060). The text is durable
/// schema; no stored or provider text is ever echoed through a reason.
/// </summary>
public static class LocalExecutionSequenceReason
{
    public const string ReviewApproved = "review_approved";
    public const string AllowanceReached = "allowance_reached";
    public const string DiagnosisEscalated = "diagnosis_escalated";
    public const string StoppedBeforeClaim = "stopped_before_claim";
    public const string StoppedAfterOperation = "stopped_after_operation";
    public const string PreparationRefused = "preparation_refused";
    public const string PreparationExpired = "preparation_expired";
    public const string PreparationFailed = "preparation_failed";
    public const string PreparationUnresolved = "preparation_unresolved";
    public const string ResultDeadlineElapsed = "result_deadline_elapsed";
    public const string AdvancementDeadlineElapsed = "advancement_deadline_elapsed";
    public const string OperationFailed = "operation_failed";
    public const string OperationOutcomeInvalid = "operation_outcome_invalid";
    public const string ExitEvidenceMissing = "exit_evidence_missing";
    public const string VerificationNotDiagnosable = "verification_not_diagnosable";
    public const string AuthorityChanged = "authority_changed";
    public const string CapacityRefused = "capacity_refused";
    public const string HostRestarted = "host_restarted";

    public const int MaximumDetailLength = 96;

    public static bool IsKnown(string? reason) => reason is ReviewApproved or AllowanceReached or DiagnosisEscalated
        or StoppedBeforeClaim or StoppedAfterOperation or PreparationRefused or PreparationExpired or PreparationFailed
        or PreparationUnresolved or ResultDeadlineElapsed or AdvancementDeadlineElapsed or OperationFailed
        or OperationOutcomeInvalid or ExitEvidenceMissing or VerificationNotDiagnosable or AuthorityChanged
        or CapacityRefused or HostRestarted;

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
