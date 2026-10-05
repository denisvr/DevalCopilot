using DevalCopilot.Domain.Features.Runs;

using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The one policy of the run-scoped Codex account-usage stop (ADR-0025), used at the claim seam, by the dispatch gate and by the
/// terminal recording command so they cannot drift. Every reported bucket and window is evaluated conservatively: a used percentage at
/// or above the threshold stops (equality reaches the stop), and a non-null provider reached-limit state stops even when the
/// percentages are low. Evidence that is not complete and current is never read as "below": an invalid, partial or unavailable
/// observation, a retrieval instant outside the read interval or in the future, evidence older than <see cref="MaxEvidenceAge"/> at the
/// commit seam (a host freshness limit, not the age of the provider's data), or a known reset that has already passed, stops with an
/// unavailable decision. Permission only ever means this one local guard did not stop: never account access, readiness or quota.
/// </summary>
public static class CodexAccountUsageStopPolicy
{
    /// <summary>The oldest host-retrieved evidence a commit seam accepts.</summary>
    public static readonly TimeSpan MaxEvidenceAge = TimeSpan.FromSeconds(30);

    /// <summary>The decision for a configured stop whose evidence could not be obtained at all (no observation was possible).</summary>
    public static AgentCodexAccountUsageDecision NoEvidence(int thresholdPercent) => AgentCodexAccountUsageDecision.Create(
        CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceUnavailable, thresholdPercent, null, []);

    /// <summary>The decision for an attempt whose stored threshold snapshot is not a valid setting: no number is ever guessed.</summary>
    public static AgentCodexAccountUsageDecision UnusableThreshold() => AgentCodexAccountUsageDecision.Create(
        CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.ThresholdUnusable, null, null, []);

    /// <summary>Evaluates one check at <paramref name="nowUtc"/>, the host clock at the commit seam.</summary>
    public static CodexAccountUsageStopEvaluation Evaluate(CodexAccountUsageGuardFacts? facts, int thresholdPercent, DateTimeOffset nowUtc)
    {
        if (facts is null || facts.ThresholdPercent != thresholdPercent || !CodexAccountUsageStop.IsValid(thresholdPercent))
        {
            return Stop(NoEvidence(thresholdPercent));
        }

        var observation = facts.Observation;
        if (!observation.IsValid || observation.RetrievedAtUtc is not { } retrieved || observation.Buckets.IsDefaultOrEmpty)
        {
            return Stop(NoEvidence(thresholdPercent));
        }

        var windows = ValidatedWindows(observation);
        if (retrieved < facts.ReadStartedUtc
            || retrieved > facts.ReadCompletedUtc
            || facts.ReadStartedUtc > facts.ReadCompletedUtc
            || retrieved > nowUtc
            || nowUtc - retrieved > MaxEvidenceAge
            || observation.Buckets.Any(bucket => HasPassedReset(bucket, nowUtc)))
        {
            return Stop(AgentCodexAccountUsageDecision.Create(
                CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceExpired, thresholdPercent, retrieved, []));
        }

        if (windows.Any(window => window.UsedPercent >= thresholdPercent))
        {
            return Stop(AgentCodexAccountUsageDecision.Create(
                CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, thresholdPercent, retrieved, windows));
        }

        return observation.ProviderReportedLimitReached
            ? Stop(AgentCodexAccountUsageDecision.Create(
                CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ProviderReportedLimitReached,
                thresholdPercent, retrieved, windows))
            : CodexAccountUsageStopEvaluation.Permitted;
    }

    private static CodexAccountUsageStopEvaluation Stop(AgentCodexAccountUsageDecision decision) => new(decision);

    private static bool HasPassedReset(AccountUsageBucket bucket, DateTimeOffset nowUtc) =>
        (bucket.Primary?.ResetsAtUtc is { } primary && primary <= nowUtc)
        || (bucket.Secondary?.ResetsAtUtc is { } secondary && secondary <= nowUtc);

    private static List<CodexAccountUsageWindowFact> ValidatedWindows(AccountUsageObservation observation)
    {
        var windows = new List<CodexAccountUsageWindowFact>();
        foreach (var bucket in observation.Buckets)
        {
            if (bucket.Primary is { } primary)
            {
                windows.Add(new CodexAccountUsageWindowFact(bucket.Id, CodexAccountUsageWindowKind.Primary, primary.UsedPercent));
            }

            if (bucket.Secondary is { } secondary)
            {
                windows.Add(new CodexAccountUsageWindowFact(bucket.Id, CodexAccountUsageWindowKind.Secondary, secondary.UsedPercent));
            }
        }

        return windows;
    }
}
