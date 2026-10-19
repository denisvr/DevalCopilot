using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>The fixed English text of the closed Agent supervision reason and next-action codes (ADR-0056). No stored text, provider text,
/// identifier or exception is ever echoed through these strings, and none claims provider progress or a live process.</summary>
internal static class AgentSupervisionText
{
    public static string? ReasonText(string? reasonCode) => reasonCode switch
    {
        AgentSupervisionAttentionReason.StaleHeartbeat =>
            "The host observer has not reported for at least thirty seconds. This does not show whether earlier work stopped.",
        AgentSupervisionAttentionReason.PhaseDeadlineElapsed =>
            "The observed phase passed its deadline. No process was cancelled and earlier authorized work may still finish later.",
        AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult =>
            "The host observer ended without a recorded terminal result. The attempt stays as recorded until it is reconciled.",
        AgentSupervisionAttentionReason.HostInterrupted =>
            "The host that observed this attempt is no longer running. Its last report is not current.",
        AgentSupervisionVocabulary.MultipleActiveAgentAttempts =>
            "More than one Agent attempt is recorded as running; no single one can be shown.",
        AgentSupervisionVocabulary.AttemptFactsIncoherent =>
            "The recorded Agent attempt facts are not coherent, so nothing is shown as observed.",
        AgentSupervisionVocabulary.SupervisionFactsIncoherent =>
            "The recorded observation facts are not coherent, so nothing is shown as observed.",
        AgentSupervisionVocabulary.ChronologyInvalid =>
            "The recorded observation times are not coherent with the host clock, so nothing is shown as fresh.",
        _ => null,
    };

    public static string NextActionText(string? nextActionCode) => nextActionCode switch
    {
        AgentSupervisionVocabulary.NoAgentAttemptRecorded => "No Agent attempt is recorded for this run.",
        AgentSupervisionVocabulary.InspectTheRecordedAttempt => "Inspect the recorded attempt; no observation is recorded for it.",
        AgentSupervisionVocabulary.WaitAndRefresh => "Wait; the host observer is reporting.",
        AgentSupervisionVocabulary.ReviewTheRecordedResult => "Review the recorded result of the attempt.",
        _ => "Inspect the recorded attempt and reconcile it before any further work.",
    };
}
