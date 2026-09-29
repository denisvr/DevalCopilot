using DevalCopilot.Application.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// One provider's token-activity stop. <c>Provider</c> is <c>"Codex"</c> or <c>"ClaudeCode"</c>;
/// <c>State</c> is one of <c>NotConfigured</c>, <c>NoDispatchedHistory</c>, <c>BelowThresholdComplete</c>,
/// <c>ThresholdReached</c>, or <c>EvidenceIndeterminate</c> (plain strings, this repository's
/// convention). <c>ClaimBlocked</c> is true exactly for the last two: a new Agent claim for the
/// provider is refused. The other states only mean this stop does not refuse a claim; they never
/// assert provider availability or account allowance. <c>KnownTokenCount</c> is a lower bound whenever
/// any pending, insufficient-evidence, or unattributed count is non-zero, and saturated when
/// <c>CountOverflowed</c>. Derived from locally recorded provider-reported usage only, and separate
/// from the advisory token warning.
/// </summary>
public sealed record RunCockpitTokenStopResponse(
    string Provider,
    long? ThresholdTokens,
    string State,
    bool ClaimBlocked,
    long KnownTokenCount,
    int CountedAttempts,
    int PendingAttempts,
    int InsufficientEvidenceAttempts,
    int UnattributedAttempts,
    bool CountOverflowed)
{
    public static RunCockpitTokenStopResponse FromDomain(AgentTokenStopEvaluation entry) => new(
        entry.Provider.ToString(),
        entry.ThresholdTokens,
        entry.State.ToString(),
        entry.BlocksClaim,
        entry.KnownTokenCount,
        entry.CountedAttempts,
        entry.PendingAttempts,
        entry.InsufficientEvidenceAttempts,
        entry.UnattributedAttempts,
        entry.CountOverflowed);
}
