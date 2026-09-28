using System.Text.Json;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Observes a Codex ChatGPT account-allowance snapshot by speaking the documented Codex App
/// Server JSON-RPC protocol (stdio JSONL, the required <c>initialize</c>/<c>initialized</c>
/// handshake, then the sole <c>account/rateLimits/read</c> read method) directly against the
/// same already-vetted local Codex CLI launch target every other Codex adapter uses.
///
/// <para>
/// The shared <c>CodexProcessInvoker</c> cannot be reused here: it is a deliberately one-shot
/// contract (write the complete stdin payload, close it, then wait for the process to exit on
/// its own), while the App Server is a long-running duplex JSON-RPC peer that must itself be
/// terminated once its bounded read-only exchange completes. The launch, handshake, correlated-
/// read, and cleanup mechanics that contract requires are shared with the Codex model/reasoning-
/// effort catalog observation through <see cref="CodexAppServerSession"/>; only the
/// <c>account/rateLimits/read</c> request and its response parsing remain owned here.
/// </para>
///
/// <para>
/// Never passes an experimental, bypass, or mutating flag; never opens a listening socket;
/// never reads a CLI auth file or supplies a token — the App Server locates the CLI's own
/// existing local authentication itself, exactly as every other Codex invocation already does.
/// This is a read-only observation of an existing snapshot, never resume eligibility, invocation
/// eligibility, or authority to invoke anything else.
/// </para>
/// </summary>
public sealed class CodexAccountAllowanceAdapter(TimeProvider timeProvider, TimeSpan? invocationTimeout = null) : ICodexAccountAllowanceAdapter
{
    private const int AccountRateLimitsReadRequestId = 1;

    private const int MaxTotalCapturedBytes = 64 * 1024;
    private const int MaxLineBytes = 16 * 1024;
    private const int MaxBuckets = 16;

    /// <summary>The most limit-id characters this projection trusts. Generous for a short
    /// provider-assigned identifier, conservative against an oversized or adversarial key ever
    /// reaching API, logs, or UI.</summary>
    private const int MaxLimitIdLength = 64;

    private readonly TimeSpan _invocationTimeout = invocationTimeout ?? CodexAppServerSession.DefaultInvocationTimeout;

    public Task<CodexAccountAllowanceObservation> ObserveAsync(
        string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
        CodexAppServerSession.RunAsync(
            executablePath,
            scriptPath,
            _invocationTimeout,
            MaxTotalCapturedBytes,
            MaxLineBytes,
            CodexAccountAllowanceObservation.Unknown,
            ExchangeAsync,
            cancellationToken);

    private async Task<CodexAccountAllowanceObservation> ExchangeAsync(CodexAppServerChannel channel, CancellationToken cancellationToken)
    {
        var rateLimitsResponse =
            await channel.SendAccountRateLimitsReadAsync(AccountRateLimitsReadRequestId, cancellationToken).ConfigureAwait(false);
        if (rateLimitsResponse is not { } rateLimitsElement || !CodexAppServerSession.IsWellFormedSuccessResponse(rateLimitsElement))
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        return ExtractObservation(rateLimitsElement, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Projects only the documented, bounded fields this slice displays. Prefers
    /// <c>rateLimitsByLimitId</c> when it is present as a non-empty object — a map from each
    /// bounded, validated limit id to its own snapshot (an object carrying <c>primary</c> and/or
    /// <c>secondary</c>) — over the legacy <c>rateLimits</c> object, which is itself one such
    /// snapshot with no id of its own; never both, so a bucket is never counted twice and the two
    /// views are never combined into one invented aggregate. Every bucket is represented on its
    /// own; a malformed, duplicated, or excessive bucket map fails closed instead of silently
    /// omitting provider-reported buckets. A response that yields no usable window is reported as
    /// <see cref="CodexAccountAllowanceObservation.Unknown"/> rather than an <c>Observed</c>
    /// snapshot with nothing to show.
    /// </summary>
    private static CodexAccountAllowanceObservation ExtractObservation(JsonElement response, DateTimeOffset retrievedAtUtc)
    {
        if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        var buckets = new List<CodexAllowanceBucket>();

        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId)
            && byLimitId.ValueKind != JsonValueKind.Null)
        {
            if (byLimitId.ValueKind != JsonValueKind.Object)
            {
                return CodexAccountAllowanceObservation.Unknown;
            }

            var limitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in byLimitId.EnumerateObject())
            {
                if (buckets.Count >= MaxBuckets
                    || !IsAcceptedLimitId(entry.Name)
                    || !limitIds.Add(entry.Name)
                    || entry.Value.ValueKind != JsonValueKind.Object)
                {
                    return CodexAccountAllowanceObservation.Unknown;
                }

                buckets.Add(ReadBucket(entry.Name, entry.Value));
            }
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            buckets.Add(ReadBucket(limitId: null, legacy));
        }

        return buckets.Count == 0 || !buckets.Any(bucket => bucket.Primary is not null || bucket.Secondary is not null)
            ? CodexAccountAllowanceObservation.Unknown
            : new CodexAccountAllowanceObservation(true, retrievedAtUtc, buckets);
    }

    /// <summary>A provider-assigned limit id is untrusted text: bounded in length and restricted
    /// to a safe identifier character set before it is ever kept, so a malformed or adversarial
    /// key can never reach API, logs, or UI.</summary>
    private static bool IsAcceptedLimitId(string limitId) =>
        limitId.Length is > 0 and <= MaxLimitIdLength
        && limitId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static CodexAllowanceBucket ReadBucket(string? limitId, JsonElement snapshot)
    {
        var primary = TryReadWindow(snapshot, "primary");
        var secondary = TryReadWindow(snapshot, "secondary");

        return new CodexAllowanceBucket(limitId, primary, secondary);
    }

    /// <summary>
    /// A window's <c>usedPercent</c> is the one field required for the window to exist at all:
    /// missing or out of the inclusive [0, 100] range means no window, not a guessed or clamped
    /// value. <c>windowDurationMins</c> (nullable integer) and <c>resetsAt</c> (nullable Unix-
    /// seconds integer) are independently optional per the documented response: each is
    /// projected as <see langword="null"/> when absent or reported in a shape this projection
    /// does not trust, without invalidating the rest of the window.
    /// </summary>
    private static CodexAllowanceWindow? TryReadWindow(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!window.TryGetProperty("usedPercent", out var usedPercentElement)
            || usedPercentElement.ValueKind != JsonValueKind.Number
            || !usedPercentElement.TryGetInt32(out var usedPercent)
            || usedPercent < 0
            || usedPercent > 100)
        {
            return null;
        }

        var windowDurationMins = TryReadOptionalNonNegativeInteger(window, "windowDurationMins");
        var resetsAtUtc = TryReadOptionalUnixSecondsInstant(window, "resetsAt");

        return new CodexAllowanceWindow(usedPercent, windowDurationMins, resetsAtUtc);
    }

    /// <summary>Absent, JSON <c>null</c>, or a value this reader does not trust all resolve to
    /// <see langword="null"/> alike — an optional field's own absence and its malformed presence
    /// are indistinguishable to a reader that must not guess either way.</summary>
    private static int? TryReadOptionalNonNegativeInteger(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < 0)
        {
            return null;
        }

        return value;
    }

    private static DateTimeOffset? TryReadOptionalUnixSecondsInstant(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var unixSeconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
