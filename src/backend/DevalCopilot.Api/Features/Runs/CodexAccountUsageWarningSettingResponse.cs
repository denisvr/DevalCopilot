using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// The run's saved advisory Codex account-usage warning (ADR-0026). <c>State</c> is exactly one of <c>NotConfigured</c> (no warning is
/// saved), <c>Configured</c> (the threshold is in <c>Percent</c>) or <c>Unknown</c> (the stored value is not a valid setting, so no
/// number is shown). A threshold only: never an observation, account access, readiness, remaining quota or live capacity, and
/// reading it performs no provider work.
/// </summary>
public sealed record CodexAccountUsageWarningSettingResponse(string State, int? Percent)
{
    /// <summary>Null when there is no fact.</summary>
    public static CodexAccountUsageWarningSettingResponse? FromDomain(CodexAccountUsageWarningFact? fact) =>
        fact is null ? null : new CodexAccountUsageWarningSettingResponse(StateName(fact.State), fact.Percent);

    private static string StateName(CodexAccountUsageWarningState state) => state switch
    {
        CodexAccountUsageWarningState.NotConfigured => "NotConfigured",
        CodexAccountUsageWarningState.Configured => "Configured",
        _ => "Unknown",
    };
}
