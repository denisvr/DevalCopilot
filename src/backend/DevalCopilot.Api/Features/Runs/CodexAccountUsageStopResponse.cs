using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// The run-scoped Codex account-usage stop (ADR-0025), exposed as either the Run's current saved setting or a Codex attempt's own
/// immutable snapshot of it. <c>State</c> is exactly one of <c>NotConfigured</c> (no stop is saved or was snapshotted; a historical
/// attempt is never given a guessed policy), <c>Configured</c> (the threshold is in <c>Percent</c>) or <c>Unknown</c> (the stored
/// value is not a valid setting, so no number is shown). A threshold for a local guard over a provider-reported percentage: never
/// account access, readiness, remaining quota or live capacity.
/// </summary>
public sealed record CodexAccountUsageStopResponse(string State, int? Percent)
{
    /// <summary>Null when there is no fact (an attempt that is not a Codex Agent attempt).</summary>
    public static CodexAccountUsageStopResponse? FromDomain(CodexAccountUsageStopFact? fact) =>
        fact is null ? null : new CodexAccountUsageStopResponse(StateName(fact.State), fact.Percent);

    private static string StateName(CodexAccountUsageStopState state) => state switch
    {
        CodexAccountUsageStopState.NotConfigured => "NotConfigured",
        CodexAccountUsageStopState.Configured => "Configured",
        _ => "Unknown",
    };
}
