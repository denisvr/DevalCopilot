namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;

/// <summary>The closed reason and next-action codes of the Agent supervision read (ADR-0056). Fixed schema; no stored text is echoed.</summary>
public static class AgentSupervisionVocabulary
{
    public const string MultipleActiveAgentAttempts = "multiple_active_agent_attempts";
    public const string AttemptFactsIncoherent = "attempt_facts_incoherent";
    public const string SupervisionFactsIncoherent = "supervision_facts_incoherent";
    public const string ChronologyInvalid = "chronology_invalid";

    public const string NoAgentAttemptRecorded = "no_agent_attempt_recorded";
    public const string InspectTheRecordedAttempt = "inspect_the_recorded_attempt";
    public const string WaitAndRefresh = "wait_and_refresh";
    public const string InspectAndReconcileBeforeFurtherWork = "inspect_and_reconcile_before_further_work";
    public const string ReviewTheRecordedResult = "review_the_recorded_result";

    public static bool IsKnownUnavailableReason(string? reason) => reason is MultipleActiveAgentAttempts or AttemptFactsIncoherent
        or SupervisionFactsIncoherent or ChronologyInvalid;

    public static bool IsKnownNextAction(string? action) => action is NoAgentAttemptRecorded or InspectTheRecordedAttempt or WaitAndRefresh
        or InspectAndReconcileBeforeFurtherWork or ReviewTheRecordedResult;
}
