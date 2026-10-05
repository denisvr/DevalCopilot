using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The pure evaluation of the advisory Codex account-usage warning (ADR-0026), deliberately separate from the account-usage stop's
/// policy and decisions. Every reported bucket and window is evaluated: a used percentage at or above the threshold reaches the
/// warning (equality reaches it), and a non-null provider reached-limit state reaches it even when the percentages are low. Evidence
/// that is not complete and current is never read as below: an invalid, partial or unavailable observation, a retrieval instant
/// outside the host's read interval or in the future, evidence older than <see cref="MaxEvidenceAge"/> when it is evaluated (a host
/// freshness limit, not the age of the provider's data), or a known reset that has already passed is Unavailable, with no valid
/// subset, credit override, sum or inferred zero. Below means only that no reported window reached the saved advisory threshold.
/// </summary>
public static class CodexAccountUsageWarningPolicy
{
    /// <summary>The oldest host-retrieved evidence a check accepts at evaluation.</summary>
    public static readonly TimeSpan MaxEvidenceAge = TimeSpan.FromSeconds(30);

    public static CodexAccountUsageWarningEvaluation Unavailable(CodexAccountUsageWarningReason reason) =>
        new(CodexAccountUsageWarningCheckState.Unavailable, reason, null, [], false);

    public static CodexAccountUsageWarningEvaluation Evaluate(
        int thresholdPercent,
        AccountUsageObservation? observation,
        DateTimeOffset readStartedUtc,
        DateTimeOffset readCompletedUtc,
        DateTimeOffset nowUtc)
    {
        if (!CodexAccountUsageWarning.IsValid(thresholdPercent)
            || observation is not { IsValid: true, RetrievedAtUtc: { } retrieved }
            || observation.Buckets.IsDefaultOrEmpty)
        {
            return Unavailable(CodexAccountUsageWarningReason.EvidenceUnavailable);
        }

        if (retrieved < readStartedUtc
            || retrieved > readCompletedUtc
            || readStartedUtc > readCompletedUtc
            || retrieved > nowUtc
            || nowUtc - retrieved > MaxEvidenceAge
            || observation.Buckets.Any(bucket => HasPassedReset(bucket, nowUtc)))
        {
            return Unavailable(CodexAccountUsageWarningReason.EvidenceExpired);
        }

        var windows = new List<CodexAccountUsageWarningWindow>();
        foreach (var bucket in observation.Buckets)
        {
            Add(windows, bucket.Id, CodexAccountUsageWarningWindowKind.Primary, bucket.Primary, thresholdPercent);
            Add(windows, bucket.Id, CodexAccountUsageWarningWindowKind.Secondary, bucket.Secondary, thresholdPercent);
        }

        var providerReported = observation.ProviderReportedLimitReached;
        if (windows.Any(window => window.ReachedThreshold))
        {
            return new(CodexAccountUsageWarningCheckState.Reached, CodexAccountUsageWarningReason.ThresholdReached, retrieved, windows, providerReported);
        }

        return providerReported
            ? new(CodexAccountUsageWarningCheckState.Reached, CodexAccountUsageWarningReason.ProviderReportedLimitReached, retrieved, windows, true)
            : new(CodexAccountUsageWarningCheckState.Below, null, retrieved, windows, false);
    }

    private static void Add(
        List<CodexAccountUsageWarningWindow> windows,
        string? bucketId,
        CodexAccountUsageWarningWindowKind kind,
        AccountUsageWindow? window,
        int thresholdPercent)
    {
        if (window is not null)
        {
            windows.Add(new CodexAccountUsageWarningWindow(bucketId, kind, window.UsedPercent, window.UsedPercent >= thresholdPercent));
        }
    }

    private static bool HasPassedReset(AccountUsageBucket bucket, DateTimeOffset nowUtc) =>
        (bucket.Primary?.ResetsAtUtc is { } primary && primary <= nowUtc)
        || (bucket.Secondary?.ResetsAtUtc is { } secondary && secondary <= nowUtc);
}
