using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The bounded, read-side statement of the run-scoped Codex account-usage stop (ADR-0025): either the Run's current saved setting
/// (<see cref="ForRun(Run)"/>) or a Codex attempt's own immutable snapshot of it (<see cref="ForAttempt"/>). Both describe a configured
/// <em>threshold</em> for a local guard over a provider-reported percentage: never account access, readiness, remaining quota or
/// live capacity. <see cref="Percent"/> is present only for <see cref="CodexAccountUsageStopState.Configured"/>; a stored value that
/// is not a valid setting is <see cref="CodexAccountUsageStopState.Unknown"/> and never a number.
/// </summary>
public sealed record CodexAccountUsageStopFact(CodexAccountUsageStopState State, int? Percent)
{
    public static CodexAccountUsageStopFact ForRun(Run run) => FromReading(run.ReadCodexAccountUsageStopPercent());

    /// <summary>The fact from stored text, for projections that do not load the Run.</summary>
    public static CodexAccountUsageStopFact ForStored(string? stored) => FromReading(CodexAccountUsageStop.Read(stored));

    /// <summary>The fact for one attempt, or <see langword="null"/> when the attempt is not a Codex Agent attempt (the concept does not
    /// apply). A Codex attempt claimed with the stop disabled, or before the setting existed, is <c>NotConfigured</c>: no historical
    /// policy is invented.</summary>
    public static CodexAccountUsageStopFact? ForAttempt(Attempt attempt) =>
        attempt.Kind == AttemptKind.Agent && attempt.AgentProvider == AgentProvider.Codex
            ? FromReading(attempt.ReadAgentCodexAccountUsageStopPercent())
            : null;

    private static CodexAccountUsageStopFact FromReading(CodexAccountUsageStopReading reading) => reading switch
    {
        { IsMalformed: true } => new CodexAccountUsageStopFact(CodexAccountUsageStopState.Unknown, null),
        { Value: { } value } => new CodexAccountUsageStopFact(CodexAccountUsageStopState.Configured, value),
        _ => new CodexAccountUsageStopFact(CodexAccountUsageStopState.NotConfigured, null),
    };
}
