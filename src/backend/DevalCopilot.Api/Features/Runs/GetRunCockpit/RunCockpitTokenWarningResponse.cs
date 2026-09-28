using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// One provider's advisory token-activity warning. <c>Provider</c> is <c>"Codex"</c> or
/// <c>"ClaudeCode"</c>; <c>State</c> is one of <c>NotConfigured</c>, <c>ThresholdReached</c>,
/// <c>NoEvidence</c>, <c>BelowThresholdComplete</c>, or <c>Indeterminate</c> (plain strings, this
/// repository's convention). <c>KnownTokenCount</c> is a lower bound whenever any of the pending,
/// insufficient-evidence, or unattributed counts is non-zero; <c>Indeterminate</c> is explicitly not
/// an all-clear. Advisory only, derived from locally recorded provider-reported usage.
/// </summary>
public sealed record RunCockpitTokenWarningResponse(
    string Provider,
    long? ThresholdTokens,
    string State,
    long KnownTokenCount,
    int CountedAttempts,
    int PendingAttempts,
    int InsufficientEvidenceAttempts,
    int UnattributedAttempts)
{
    public static RunCockpitTokenWarningResponse FromDomain(RunCockpitTokenWarningEntry entry) => new(
        entry.Provider.ToString(),
        entry.ThresholdTokens,
        entry.State.ToString(),
        entry.KnownTokenCount,
        entry.CountedAttempts,
        entry.PendingAttempts,
        entry.InsufficientEvidenceAttempts,
        entry.UnattributedAttempts);
}
