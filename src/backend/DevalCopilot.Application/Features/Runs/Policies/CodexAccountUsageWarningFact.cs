using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The bounded, read-side statement of the run's saved advisory Codex account-usage warning (ADR-0026). It describes a configured
/// <em>threshold</em> only: never an observation, account access, readiness, remaining quota or live capacity, and reading it performs
/// no provider work. <see cref="Percent"/> is present only for <see cref="CodexAccountUsageWarningState.Configured"/>.
/// </summary>
public sealed record CodexAccountUsageWarningFact(CodexAccountUsageWarningState State, int? Percent)
{
    public static CodexAccountUsageWarningFact ForRun(Run run) => run.ReadCodexAccountUsageWarningPercent() switch
    {
        { IsMalformed: true } => new CodexAccountUsageWarningFact(CodexAccountUsageWarningState.Unknown, null),
        { Value: { } value } => new CodexAccountUsageWarningFact(CodexAccountUsageWarningState.Configured, value),
        _ => new CodexAccountUsageWarningFact(CodexAccountUsageWarningState.NotConfigured, null),
    };
}
