namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The single Domain rule relating provider-reported <see cref="AgentTokenUsageEvidence"/> to an
/// Agent attempt's provider, dispatch state, and requested terminal <see cref="AgentOutcome"/>. Enforced by
/// every Agent completion transition on <see cref="Attempt"/> and evaluated in advance by each
/// result-recording Application handler so a violation fails closed without mutation.
/// Deliberately simpler than <see cref="AgentProcessEvidencePolicy"/>: token usage is best-effort
/// observation, so it is never required for any outcome, and it may accompany any outcome of a
/// dispatched attempt — including a failed or non-zero-exit invocation, since a provider can still
/// report the usage it consumed before failing.
/// </summary>
public static class AgentTokenUsageEvidencePolicy
{
    /// <summary>The locally evidenced Claude Code provider usage contract. A new provider or
    /// schema requires explicit adapter-contract evidence before it can become authoritative.</summary>
    public const string ClaudeCliSchemaVersion = "claude-cli-usage-v1";

    /// <summary>The locally evidenced Codex CLI provider usage contract: the <c>usage</c> object
    /// of the unique terminal <c>turn.completed</c> JSONL event on <c>codex exec --json</c>
    /// non-interactive stdout. See <c>CodexCliTokenUsage</c> for the parsing contract this schema
    /// version tags.</summary>
    public const string CodexCliSchemaVersion = "codex-cli-usage-v1";

    public static bool IsSupportedSource(AgentProvider? provider, string? schemaVersion) =>
        (provider == AgentProvider.ClaudeCode && string.Equals(schemaVersion, ClaudeCliSchemaVersion, StringComparison.Ordinal))
        || (provider == AgentProvider.Codex && string.Equals(schemaVersion, CodexCliSchemaVersion, StringComparison.Ordinal));

    /// <summary>Returns the first violation, or <see langword="null"/> when the combination is
    /// valid. Absent evidence is always valid. Present evidence must come from the exact proven
    /// provider/schema pair, and may never accompany a
    /// pre-invocation outcome — the closed set is owned by
    /// <see cref="AgentProcessEvidencePolicy.IsPreInvocationOutcome"/> and reused here rather than
    /// copied, so the two rules can never drift — and may never accompany an undispatched
    /// attempt.</summary>
    public static AgentTokenUsageEvidenceViolation? Evaluate(
        AgentProvider? provider, AgentOutcome requestedOutcome, bool dispatched, AgentTokenUsageEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }

        if (!IsSupportedSource(provider, evidence.SchemaVersion))
        {
            return AgentTokenUsageEvidenceViolation.UnsupportedProviderSchema;
        }

        if (AgentProcessEvidencePolicy.IsPreInvocationOutcome(requestedOutcome))
        {
            return AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence;
        }

        if (!dispatched)
        {
            return AgentTokenUsageEvidenceViolation.NotDispatched;
        }

        return null;
    }
}
