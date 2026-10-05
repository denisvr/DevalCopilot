using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// The strict account-usage observation behind the run-scoped Codex account-usage stop (ADR-0025). It speaks only the documented Codex
/// App Server handshake and the one read-only <c>account/rateLimits/read</c> method through the shared, already-vetted
/// <see cref="CodexAppServerSession"/> (finite timeout, bounded capture, restricted environment, process-tree cleanup, no credential
/// read, no experimental flag, no other method), then admits the reply whole or not at all.
///
/// <para>
/// This is deliberately not the display adapter: that one may drop a malformed window and show the rest, which would let a guard
/// pass on a healthy sibling. Here nothing is dropped. The multi-bucket <c>rateLimitsByLimitId</c> view is used when it is a non-null
/// object and is never merged with, or replaced by, the legacy <c>rateLimits</c> snapshot (an invalid or empty map is unavailable); the
/// legacy snapshot is used only when the map is absent or null. At most 16 buckets with safe 64-character identifiers, each with at
/// least one usable window; a null or absent window is the documented absence, but a present malformed window, an invalid percentage
/// (an integer from 0 to 100 is required), a known optional duration or reset that is present and invalid, a duplicated relevant
/// property or bucket key, excess cardinality, or a failed exchange makes the whole observation unavailable. A non-null
/// <c>rateLimitReachedType</c> on any bucket sets the reached flag whatever its value (an unknown classification is never displayed or
/// used to permit). Credits never influence the answer. The retrieval instant is the host clock when the reply was received; it is
/// not the age of the provider's data. Each call is its own observation: no cache and no sharing.
/// </para>
/// </summary>
public sealed class CodexAccountUsageGuardAdapter(TimeProvider timeProvider, TimeSpan? invocationTimeout = null) : IAccountUsageObserver
{
    private const int AccountRateLimitsReadRequestId = 1;
    private const int MaxTotalCapturedBytes = 64 * 1024;
    private const int MaxLineBytes = 16 * 1024;

    private readonly TimeSpan _invocationTimeout = invocationTimeout ?? CodexAppServerSession.DefaultInvocationTimeout;

    public Task<AccountUsageObservation> ObserveAsync(string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
        CodexAppServerSession.RunAsync(
            executablePath,
            scriptPath,
            _invocationTimeout,
            MaxTotalCapturedBytes,
            MaxLineBytes,
            AccountUsageObservation.Unavailable,
            ExchangeAsync,
            cancellationToken);

    private async Task<AccountUsageObservation> ExchangeAsync(CodexAppServerChannel channel, CancellationToken cancellationToken)
    {
        var response = await channel.SendAccountRateLimitsReadAsync(AccountRateLimitsReadRequestId, cancellationToken).ConfigureAwait(false);
        return response is { } element && CodexAppServerSession.IsWellFormedSuccessResponse(element)
            ? Project(element, timeProvider.GetUtcNow())
            : AccountUsageObservation.Unavailable;
    }

    internal static AccountUsageObservation Project(JsonElement response, DateTimeOffset retrievedAtUtc)
    {
        if (CodexRateLimitsDuplicateScan.HasDuplicate(response.GetRawText())
            || !response.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            return AccountUsageObservation.Unavailable;
        }

        var buckets = new List<AccountUsageBucket>();
        var reached = false;

        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) && byLimitId.ValueKind != JsonValueKind.Null)
        {
            if (byLimitId.ValueKind != JsonValueKind.Object)
            {
                return AccountUsageObservation.Unavailable;
            }

            foreach (var entry in byLimitId.EnumerateObject())
            {
                if (buckets.Count >= AccountUsageObservation.MaxBuckets
                    || !IsSafeIdentifier(entry.Name)
                    || !TryReadBucket(entry.Name, entry.Value, out var bucket, out var bucketReached))
                {
                    return AccountUsageObservation.Unavailable;
                }

                buckets.Add(bucket);
                reached |= bucketReached;
            }
        }
        else
        {
            if (!result.TryGetProperty("rateLimits", out var legacy)
                || !TryReadBucket(null, legacy, out var bucket, out reached))
            {
                return AccountUsageObservation.Unavailable;
            }

            buckets.Add(bucket);
        }

        return AccountUsageObservation.Create(retrievedAtUtc, buckets, reached);
    }

    private static bool TryReadBucket(string? id, JsonElement snapshot, out AccountUsageBucket bucket, out bool reached)
    {
        bucket = null!;
        reached = false;
        if (snapshot.ValueKind != JsonValueKind.Object
            || !TryReadWindow(snapshot, "primary", out var primary)
            || !TryReadWindow(snapshot, "secondary", out var secondary)
            || (primary is null && secondary is null))
        {
            return false;
        }

        if (snapshot.TryGetProperty("rateLimitReachedType", out var reachedType) && reachedType.ValueKind != JsonValueKind.Null)
        {
            if (reachedType.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            reached = true;
        }

        bucket = new AccountUsageBucket(id, primary, secondary);
        return true;
    }

    /// <summary>An absent or null window is the documented absence. A present window must be an object with an integer
    /// <c>usedPercent</c> from 0 to 100, and any present optional duration or reset must itself be valid: an invalid value is never
    /// normalized into absence.</summary>
    private static bool TryReadWindow(JsonElement container, string key, out AccountUsageWindow? window)
    {
        window = null;
        if (!container.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("usedPercent", out var used)
            || used.ValueKind != JsonValueKind.Number
            || !used.TryGetInt32(out var usedPercent)
            || usedPercent is < 0 or > 100
            || !IsValidOptionalDuration(element)
            || !TryReadOptionalReset(element, out var resetsAt))
        {
            return false;
        }

        window = new AccountUsageWindow(usedPercent, resetsAt);
        return true;
    }

    private static bool IsValidOptionalDuration(JsonElement window) =>
        !window.TryGetProperty("windowDurationMins", out var duration)
        || duration.ValueKind == JsonValueKind.Null
        || (duration.ValueKind == JsonValueKind.Number && duration.TryGetInt64(out var minutes) && minutes >= 0);

    private static bool TryReadOptionalReset(JsonElement window, out DateTimeOffset? resetsAt)
    {
        resetsAt = null;
        if (!window.TryGetProperty("resetsAt", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var unixSeconds))
        {
            return false;
        }

        try
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length is > 0 and <= AccountUsageObservation.MaxBucketIdLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
