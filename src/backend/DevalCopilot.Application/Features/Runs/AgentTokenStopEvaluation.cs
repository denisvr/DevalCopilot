using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// One provider's token-activity stop evaluation. Counts cover only dispatched Agent attempts (an
/// undispatched attempt never invoked a provider).
/// </summary>
/// <param name="Provider">Codex or Claude Code; the two are never combined.</param>
/// <param name="ThresholdTokens">The owner's configured stop threshold, or null when none is set.</param>
/// <param name="State">The stop state; see <see cref="AgentTokenStopState"/>.</param>
/// <param name="KnownTokenCount">The sum over <paramref name="CountedAttempts"/> of the provider's
/// own formula (see <c>AgentTokenActivityFormula</c>). A lower bound whenever any gap count is
/// non-zero; saturated at <see cref="long.MaxValue"/> when <paramref name="CountOverflowed"/>.</param>
/// <param name="CountedAttempts">Concluded attempts of this provider with sufficient evidence.</param>
/// <param name="PendingAttempts">Still-running attempts of this provider; never counted.</param>
/// <param name="InsufficientEvidenceAttempts">Concluded attempts of this provider whose usage is
/// missing, malformed, unsupported, or (Claude Code) lacks a cache-creation or cache-read count.</param>
/// <param name="UnattributedAttempts">Dispatched attempts with no known provider, run-wide. Never
/// assigned to either provider; a gap for both.</param>
/// <param name="CountOverflowed">The known count is not representable.</param>
public sealed record AgentTokenStopEvaluation(
    AgentProvider Provider,
    long? ThresholdTokens,
    AgentTokenStopState State,
    long KnownTokenCount,
    int CountedAttempts,
    int PendingAttempts,
    int InsufficientEvidenceAttempts,
    int UnattributedAttempts,
    bool CountOverflowed)
{
    /// <summary>Whether this state refuses a new claim for the provider.</summary>
    public bool BlocksClaim => State is AgentTokenStopState.ThresholdReached or AgentTokenStopState.EvidenceIndeterminate;
}
