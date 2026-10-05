using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The read-side statement of the decision that stopped one Codex attempt before dispatch (ADR-0025): recorded (the bounded canonical
/// decision) or unavailable (the attempt's outcome says it was stopped but its stored decision is missing, malformed, oversized or not
/// provably this attempt's, so nothing is shown and nothing is guessed). An attempt that was not stopped has no fact at all: absence is
/// never a statement that an account was below a threshold.
/// </summary>
public sealed record CodexAccountUsageDecisionFact(bool Recorded, AgentCodexAccountUsageDecision? Decision)
{
    /// <summary>Null unless the attempt's outcome is one of the two account-usage stop outcomes. Never throws.</summary>
    public static CodexAccountUsageDecisionFact? ForAttempt(Attempt attempt)
    {
        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentProvider != AgentProvider.Codex
            || attempt.AgentOutcome is not (AgentOutcome.AccountUsageStopReached or AgentOutcome.AccountUsageEvidenceUnavailable))
        {
            return null;
        }

        var decision = attempt.GetAgentAccountUsageDecision();
        var agreesWithOutcome = decision is not null
            && (decision.Kind == CodexAccountUsageDecisionKind.Reached) == (attempt.AgentOutcome == AgentOutcome.AccountUsageStopReached);
        return agreesWithOutcome ? new CodexAccountUsageDecisionFact(true, decision) : new CodexAccountUsageDecisionFact(false, null);
    }
}
