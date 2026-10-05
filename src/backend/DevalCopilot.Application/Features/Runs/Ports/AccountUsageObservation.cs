using System.Collections.Immutable;

namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// What the strict account-usage observation (ADR-0025) reports: either one fully valid snapshot the whole way through, or nothing.
/// There is no partial form: an invalid, partial, duplicated, unavailable or truncated provider answer is <see cref="Unavailable"/>,
/// never a healthy subset. The buckets are an owned immutable copy (nothing the adapter or any caller still holds can change what a
/// decision was made from), and the retrieval instant is the host's own clock, not the age of the provider's data.
/// It is a fact about one read: never account access, provider readiness or quota availability.
/// </summary>
public sealed class AccountUsageObservation
{
    public const int MaxBuckets = 16;

    public const int MaxBucketIdLength = 64;

    private AccountUsageObservation(
        bool isValid, DateTimeOffset? retrievedAtUtc, ImmutableArray<AccountUsageBucket> buckets, bool providerReportedLimitReached)
    {
        IsValid = isValid;
        RetrievedAtUtc = retrievedAtUtc;
        Buckets = buckets;
        ProviderReportedLimitReached = providerReportedLimitReached;
    }

    /// <summary>The one answer for every case that is not a complete, valid snapshot.</summary>
    public static AccountUsageObservation Unavailable { get; } = new(false, null, [], false);

    public bool IsValid { get; }

    /// <summary>The host clock reading when the snapshot's reply was received.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; }

    public ImmutableArray<AccountUsageBucket> Buckets { get; }

    /// <summary>True when the provider classified any bucket as having reached a limit (a non-null reached-limit state, including a
    /// classification this host does not recognize, which is never displayed or used to permit).</summary>
    public bool ProviderReportedLimitReached { get; }

    /// <summary>A valid observation from copied facts, or <see cref="Unavailable"/> when the facts are not a complete valid snapshot:
    /// no bucket, more than <see cref="MaxBuckets"/>, a bucket without a usable window, a duplicated or unsafe bucket identifier, a
    /// percentage outside 0 to 100, or a missing retrieval instant.</summary>
    public static AccountUsageObservation Create(
        DateTimeOffset retrievedAtUtc, IEnumerable<AccountUsageBucket?>? buckets, bool providerReportedLimitReached)
    {
        if (buckets is null)
        {
            return Unavailable;
        }

        var copy = ImmutableArray.CreateBuilder<AccountUsageBucket>();
        var identifiers = new HashSet<string?>(StringComparer.Ordinal);
        foreach (var bucket in buckets)
        {
            if (bucket is null || copy.Count >= MaxBuckets || !IsUsable(bucket) || !identifiers.Add(bucket.Id))
            {
                return Unavailable;
            }

            copy.Add(bucket);
        }

        return copy.Count == 0
            ? Unavailable
            : new AccountUsageObservation(true, retrievedAtUtc.ToUniversalTime(), copy.ToImmutable(), providerReportedLimitReached);
    }

    private static bool IsUsable(AccountUsageBucket bucket) =>
        (bucket.Primary is not null || bucket.Secondary is not null)
        && (bucket.Id is null || IsSafeIdentifier(bucket.Id))
        && HasValidPercent(bucket.Primary)
        && HasValidPercent(bucket.Secondary);

    private static bool HasValidPercent(AccountUsageWindow? window) => window is null || window.UsedPercent is >= 0 and <= 100;

    private static bool IsSafeIdentifier(string value) =>
        value.Length is >= 1 and <= MaxBucketIdLength
        && value.All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.');
}
