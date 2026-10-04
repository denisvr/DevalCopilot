namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The single Domain rule relating provider-reported <see cref="AgentModelContextLimitsEvidence"/> to an Agent
/// attempt's provider, dispatch state, and requested terminal <see cref="AgentOutcome"/>. Enforced by every Agent
/// completion transition on <see cref="Attempt"/> and evaluated in advance by each result-recording Application
/// handler so a violation fails closed without mutation. Like token usage it is best-effort observation: never
/// required for any outcome, and it may accompany any outcome of a dispatched attempt, including a failed one.
/// </summary>
public static class AgentModelContextLimitsEvidencePolicy
{
    /// <summary>The locally evidenced Claude Code <c>modelUsage</c> parsing contract: the optional
    /// <c>modelUsage</c> map of the single <c>--print --output-format json</c> stdout envelope, of which only each key
    /// and its <c>contextWindow</c> and <c>maxOutputTokens</c> members are read.</summary>
    public const string ClaudeCliSource = "claude-cli-model-usage-v1";

    public static bool IsSupportedSource(AgentProvider? provider, string? source) =>
        provider == AgentProvider.ClaudeCode && string.Equals(source, ClaudeCliSource, StringComparison.Ordinal);

    /// <summary>Returns the first violation, or <see langword="null"/> when the combination is valid. Absent evidence
    /// is always valid. Present evidence must come from the exact proven provider/source pair, may never accompany a
    /// pre-invocation outcome (the closed set is owned by <see cref="AgentProcessEvidencePolicy.IsPreInvocationOutcome"/>),
    /// and may never accompany an undispatched attempt.</summary>
    public static AgentModelContextLimitsEvidenceViolation? Evaluate(
        AgentProvider? provider, AgentOutcome requestedOutcome, bool dispatched, AgentModelContextLimitsEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }

        if (!IsSupportedSource(provider, evidence.Source))
        {
            return AgentModelContextLimitsEvidenceViolation.UnsupportedProviderSource;
        }

        if (AgentProcessEvidencePolicy.IsPreInvocationOutcome(requestedOutcome))
        {
            return AgentModelContextLimitsEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence;
        }

        return dispatched ? null : AgentModelContextLimitsEvidenceViolation.NotDispatched;
    }
}
