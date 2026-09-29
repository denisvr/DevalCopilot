using System.Linq.Expressions;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// One dispatched Agent attempt's persisted stop evidence, classified by the database so that an
/// unrecognized stored <c>Status</c>, <c>AgentProvider</c>, or <c>AgentRole</c> string is never
/// materialized through EF's string-to-enum conversion (which would throw). Each flag is a comparison
/// against the known enum names, so anything unrecognized, including a string that parses to an
/// undefined number, simply matches none of them. <see cref="Classify"/> then maps the flags to the
/// accumulator's inputs: an attempt whose status is neither running nor a defined terminal status, or
/// whose provider and role are not a coherent pair, is untrusted and becomes an unattributed gap for
/// both providers (never a concluded attempt, never assigned to a provider). Only fixed classification
/// leaves this type: no stored string or exception text.
/// </summary>
internal sealed record PersistedAgentAttemptStopEvidence(
    bool IsRunning,
    bool IsTerminal,
    bool IsCodexPair,
    bool IsClaudePair,
    int? InputTokens,
    int? OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens,
    string? SchemaVersion)
{
    /// <summary>Server-side projection; the Agent roles each provider's claim factories assign are
    /// Codex: Planner, Resolver, CodeReviewer; Claude Code: CriticalReviewer, Implementer (which also
    /// covers review correction).</summary>
    public static readonly Expression<Func<Attempt, PersistedAgentAttemptStopEvidence>> Projection = attempt =>
        new PersistedAgentAttemptStopEvidence(
            attempt.Status == AttemptStatus.Running,
            attempt.Status == AttemptStatus.Completed
                || attempt.Status == AttemptStatus.Failed
                || attempt.Status == AttemptStatus.Interrupted,
            attempt.AgentProvider == AgentProvider.Codex
                && (attempt.AgentRole == AgentRole.Planner
                    || attempt.AgentRole == AgentRole.Resolver
                    || attempt.AgentRole == AgentRole.CodeReviewer),
            attempt.AgentProvider == AgentProvider.ClaudeCode
                && (attempt.AgentRole == AgentRole.CriticalReviewer || attempt.AgentRole == AgentRole.Implementer),
            attempt.AgentInputTokens,
            attempt.AgentOutputTokens,
            attempt.AgentCacheCreationInputTokens,
            attempt.AgentCacheReadInputTokens,
            attempt.AgentTokenUsageSchemaVersion);

    /// <summary>The accumulator inputs for a trustworthy row, or an untrusted one (provider
    /// <see langword="null"/>), which the accumulator counts as an unattributed gap.</summary>
    public (AttemptStatus Status, AgentProvider? Provider) Classify()
    {
        var statusIsSound = IsRunning || IsTerminal;
        AgentProvider? provider = !statusIsSound ? null : IsCodexPair ? AgentProvider.Codex : IsClaudePair ? AgentProvider.ClaudeCode : null;
        return (IsRunning ? AttemptStatus.Running : AttemptStatus.Completed, provider);
    }

    public AgentTokenUsageEvidence? Usage(AgentProvider? provider) =>
        AgentTokenUsageEvidence.FromPersisted(
            provider, InputTokens, OutputTokens, CacheCreationInputTokens, CacheReadInputTokens, SchemaVersion);
}
