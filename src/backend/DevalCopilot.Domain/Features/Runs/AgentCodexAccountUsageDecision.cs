using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The recorded decision that stopped one Codex attempt before it was dispatched because of the run's account-usage stop
/// (ADR-0025): the threshold the attempt claimed when it was valid, the host's own retrieval instant when an observation existed,
/// a fixed decision and reason, and only the validated bucket/window percentages the decision used. Never a raw provider payload,
/// account identifier, credential, path, credit or plan data, or label. It is a fact of a local guard over a provider-reported
/// percentage: it is never account access, provider readiness or quota availability, and an attempt without one has no recorded
/// decision (absence is never read as "below the threshold").
///
/// The persisted form is the single project-owned canonical snapshot <see cref="Serialize"/> writes (at most
/// <see cref="MaxSerializedBytes"/> UTF-8 bytes); <see cref="FromPersisted"/> accepts exactly that text and nothing else.
/// </summary>
public sealed record AgentCodexAccountUsageDecision
{
    /// <summary>The version of the persisted snapshot shape.</summary>
    public const int SnapshotVersion = 1;

    /// <summary>The fixed parsing-contract tag of the observation that produced the facts.</summary>
    public const string Source = "codex-account-rate-limits-v1";

    public const int MaxBuckets = 16;

    public const int MaxWindows = MaxBuckets * 2;

    public const int MaxBucketIdLength = 64;

    /// <summary>The largest accepted serialized snapshot, in UTF-8 bytes.</summary>
    public const int MaxSerializedBytes = 8192;

    // Fixed UTC form with seven fractional digits; it contains no character the JSON writer escapes.
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private AgentCodexAccountUsageDecision(
        CodexAccountUsageDecisionKind kind,
        CodexAccountUsageDecisionReason reason,
        int? thresholdPercent,
        DateTimeOffset? retrievedAtUtc,
        ImmutableArray<CodexAccountUsageWindowFact> windows)
    {
        Kind = kind;
        Reason = reason;
        ThresholdPercent = thresholdPercent;
        RetrievedAtUtc = retrievedAtUtc;
        Windows = windows;
    }

    public CodexAccountUsageDecisionKind Kind { get; }

    public CodexAccountUsageDecisionReason Reason { get; }

    /// <summary>The attempt's claimed threshold when it was a valid setting; null only for
    /// <see cref="CodexAccountUsageDecisionReason.ThresholdUnusable"/>.</summary>
    public int? ThresholdPercent { get; }

    /// <summary>The host's own retrieval instant of the observation the decision rests on (a host freshness fact, not the age of the
    /// provider's data), when an observation existed.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; }

    /// <summary>The validated windows the decision used, ordered by bucket then window. An immutable snapshot this value owns: no
    /// collection the caller supplied is shared with it and nothing exposed here can be written through.</summary>
    public ImmutableArray<CodexAccountUsageWindowFact> Windows { get; }

    /// <summary>Creates validated evidence, throwing <see cref="ArgumentException"/> for any shape <see cref="Validate"/> rejects.</summary>
    public static AgentCodexAccountUsageDecision Create(
        CodexAccountUsageDecisionKind kind,
        CodexAccountUsageDecisionReason reason,
        int? thresholdPercent,
        DateTimeOffset? retrievedAtUtc,
        IReadOnlyList<CodexAccountUsageWindowFact> windows)
    {
        var snapshot = Copy(windows, out var tooMany);
        var violation = tooMany ? "too many windows" : Validate(kind, reason, thresholdPercent, retrievedAtUtc, snapshot);
        if (violation is not null)
        {
            throw new ArgumentException($"Invalid Codex account-usage decision: {violation}.", nameof(windows));
        }

        return new AgentCodexAccountUsageDecision(kind, reason, thresholdPercent, Normalize(retrievedAtUtc), Order(snapshot));
    }

    /// <summary>The first shape violation as a fixed description, or <see langword="null"/> when the shape is valid.</summary>
    public static string? Validate(
        CodexAccountUsageDecisionKind kind,
        CodexAccountUsageDecisionReason reason,
        int? thresholdPercent,
        DateTimeOffset? retrievedAtUtc,
        IReadOnlyList<CodexAccountUsageWindowFact>? windows)
    {
        var snapshot = Copy(windows, out var tooMany);
        return tooMany ? "too many windows" : Validate(kind, reason, thresholdPercent, retrievedAtUtc, snapshot);
    }

    private static CodexAccountUsageWindowFact?[] Copy(IReadOnlyList<CodexAccountUsageWindowFact>? windows, out bool tooMany)
    {
        tooMany = false;
        if (windows is null || windows.Count == 0)
        {
            return [];
        }

        var count = windows.Count;
        if (count > MaxWindows)
        {
            tooMany = true;
            return [];
        }

        var copy = new CodexAccountUsageWindowFact?[count];
        for (var index = 0; index < count; index++)
        {
            copy[index] = windows[index];
        }

        return copy;
    }

    private static string? Validate(
        CodexAccountUsageDecisionKind kind,
        CodexAccountUsageDecisionReason reason,
        int? thresholdPercent,
        DateTimeOffset? retrievedAtUtc,
        CodexAccountUsageWindowFact?[] windows)
    {
        var allowedReason = (kind, reason) switch
        {
            (CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached) => true,
            (CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ProviderReportedLimitReached) => true,
            (CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceUnavailable) => true,
            (CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceExpired) => true,
            (CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.ThresholdUnusable) => true,
            _ => false,
        };
        if (!allowedReason)
        {
            return "decision and reason do not agree";
        }

        if (reason == CodexAccountUsageDecisionReason.ThresholdUnusable)
        {
            if (thresholdPercent is not null)
            {
                return "an unusable threshold has no number";
            }
        }
        else if (thresholdPercent is null || !CodexAccountUsageStop.IsValid(thresholdPercent))
        {
            return "a threshold from 1 to 100 is required";
        }

        var seen = new HashSet<(string?, CodexAccountUsageWindowKind)>();
        var buckets = new HashSet<string?>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            if (window is null || !Enum.IsDefined(window.Window) || window.UsedPercent is < 0 or > 100
                || (window.BucketId is not null && !IsValidBucketId(window.BucketId)))
            {
                return "an invalid window";
            }

            if (!seen.Add((window.BucketId, window.Window)))
            {
                return "a duplicated window";
            }

            buckets.Add(window.BucketId);
        }

        if (buckets.Count > MaxBuckets)
        {
            return "too many buckets";
        }

        var usesObservation = reason is CodexAccountUsageDecisionReason.ThresholdReached
            or CodexAccountUsageDecisionReason.ProviderReportedLimitReached;
        if (usesObservation)
        {
            if (windows.Length == 0 || retrievedAtUtc is null)
            {
                return "a reached decision needs its observed windows and retrieval instant";
            }

            if (reason == CodexAccountUsageDecisionReason.ThresholdReached
                && !windows.Any(window => window!.UsedPercent >= thresholdPercent))
            {
                return "no window reaches the threshold";
            }
        }
        else if (windows.Length != 0)
        {
            return "an unavailable decision carries no windows";
        }

        if (reason is CodexAccountUsageDecisionReason.EvidenceUnavailable or CodexAccountUsageDecisionReason.ThresholdUnusable
            && retrievedAtUtc is not null)
        {
            return "this reason carries no retrieval instant";
        }

        return Encoding.UTF8.GetByteCount(Write(kind, reason, thresholdPercent, Normalize(retrievedAtUtc), Order(windows))) > MaxSerializedBytes
            ? "too large"
            : null;
    }

    /// <summary>Reconstructs the decision from the one persisted text, returning <see langword="null"/> (unknown, never partially
    /// trusted) unless that text is exactly the canonical <see cref="Serialize"/> form of a valid decision for a Codex attempt: not
    /// oversized, not malformed, not another version or provider, not reordered, and with no extra, missing or duplicated member.
    /// Never throws.</summary>
    public static AgentCodexAccountUsageDecision? FromPersisted(AgentProvider? provider, string? stored)
    {
        if (provider != AgentProvider.Codex || stored is null || Encoding.UTF8.GetByteCount(stored) > MaxSerializedBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(stored);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryInt(root, "version", out var version) || version != SnapshotVersion
                || !TryString(root, "source", out var source) || !string.Equals(source, Source, StringComparison.Ordinal)
                || !TryString(root, "decision", out var kindText) || !TryKind(kindText, out var kind)
                || !TryString(root, "reason", out var reasonText) || !TryReason(reasonText, out var reason)
                || !root.TryGetProperty("thresholdPercent", out var thresholdElement)
                || !root.TryGetProperty("retrievedAtUtc", out var retrievedElement)
                || !root.TryGetProperty("windows", out var windowsElement) || windowsElement.ValueKind != JsonValueKind.Array
                || windowsElement.GetArrayLength() > MaxWindows)
            {
                return null;
            }

            int? threshold = null;
            if (thresholdElement.ValueKind == JsonValueKind.Number && thresholdElement.TryGetInt32(out var thresholdNumber))
            {
                threshold = thresholdNumber;
            }
            else if (thresholdElement.ValueKind != JsonValueKind.Null)
            {
                return null;
            }

            DateTimeOffset? retrieved = null;
            if (retrievedElement.ValueKind == JsonValueKind.String)
            {
                if (!DateTimeOffset.TryParseExact(
                        retrievedElement.GetString(), InstantFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    return null;
                }

                retrieved = parsed;
            }
            else if (retrievedElement.ValueKind != JsonValueKind.Null)
            {
                return null;
            }

            var windows = new List<CodexAccountUsageWindowFact>();
            foreach (var entry in windowsElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("bucket", out var bucket)
                    || !TryString(entry, "window", out var windowText) || !TryWindow(windowText, out var windowKind)
                    || !TryInt(entry, "usedPercent", out var used))
                {
                    return null;
                }

                string? bucketId = null;
                if (bucket.ValueKind == JsonValueKind.String)
                {
                    bucketId = bucket.GetString();
                }
                else if (bucket.ValueKind != JsonValueKind.Null)
                {
                    return null;
                }

                windows.Add(new CodexAccountUsageWindowFact(bucketId, windowKind, used));
            }

            if (Validate(kind, reason, threshold, retrieved, windows) is not null)
            {
                return null;
            }

            var decision = Create(kind, reason, threshold, retrieved, windows);
            return string.Equals(decision.Serialize(), stored, StringComparison.Ordinal) ? decision : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The one canonical persisted text, written compactly with fixed member names and order.</summary>
    public string Serialize() => Write(Kind, Reason, ThresholdPercent, RetrievedAtUtc, Windows);

    private static DateTimeOffset? Normalize(DateTimeOffset? value) => value?.ToUniversalTime();

    private static ImmutableArray<CodexAccountUsageWindowFact> Order(IEnumerable<CodexAccountUsageWindowFact?> windows) =>
        [.. windows
            .OrderBy(window => window!.BucketId is not null)
            .ThenBy(window => window!.BucketId, StringComparer.Ordinal)
            .ThenBy(window => window!.Window)
            .Select(window => window!)];

    private static bool IsValidBucketId(string value)
    {
        if (value.Length is < 1 or > MaxBucketIdLength)
        {
            return false;
        }

        return value.All(character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.');
    }

    private static string Write(
        CodexAccountUsageDecisionKind kind,
        CodexAccountUsageDecisionReason reason,
        int? threshold,
        DateTimeOffset? retrievedAtUtc,
        IEnumerable<CodexAccountUsageWindowFact> windows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", SnapshotVersion);
            writer.WriteString("source", Source);
            writer.WriteString("decision", KindText(kind));
            writer.WriteString("reason", ReasonText(reason));
            if (threshold is { } number)
            {
                writer.WriteNumber("thresholdPercent", number);
            }
            else
            {
                writer.WriteNull("thresholdPercent");
            }

            if (retrievedAtUtc is { } instant)
            {
                writer.WriteString("retrievedAtUtc", instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNull("retrievedAtUtc");
            }

            writer.WriteStartArray("windows");
            foreach (var window in windows)
            {
                writer.WriteStartObject();
                if (window.BucketId is null)
                {
                    writer.WriteNull("bucket");
                }
                else
                {
                    writer.WriteString("bucket", window.BucketId);
                }

                writer.WriteString("window", window.Window == CodexAccountUsageWindowKind.Primary ? "primary" : "secondary");
                writer.WriteNumber("usedPercent", window.UsedPercent);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string KindText(CodexAccountUsageDecisionKind kind) =>
        kind == CodexAccountUsageDecisionKind.Reached ? "reached" : "unavailable";

    private static string ReasonText(CodexAccountUsageDecisionReason reason) => reason switch
    {
        CodexAccountUsageDecisionReason.ThresholdReached => "threshold_reached",
        CodexAccountUsageDecisionReason.ProviderReportedLimitReached => "provider_limit_reached",
        CodexAccountUsageDecisionReason.EvidenceUnavailable => "evidence_unavailable",
        CodexAccountUsageDecisionReason.EvidenceExpired => "evidence_expired",
        _ => "threshold_unusable",
    };

    private static bool TryKind(string? text, out CodexAccountUsageDecisionKind kind)
    {
        kind = text switch
        {
            "reached" => CodexAccountUsageDecisionKind.Reached,
            "unavailable" => CodexAccountUsageDecisionKind.Unavailable,
            _ => 0,
        };
        return kind != 0;
    }

    private static bool TryReason(string? text, out CodexAccountUsageDecisionReason reason)
    {
        reason = text switch
        {
            "threshold_reached" => CodexAccountUsageDecisionReason.ThresholdReached,
            "provider_limit_reached" => CodexAccountUsageDecisionReason.ProviderReportedLimitReached,
            "evidence_unavailable" => CodexAccountUsageDecisionReason.EvidenceUnavailable,
            "evidence_expired" => CodexAccountUsageDecisionReason.EvidenceExpired,
            "threshold_unusable" => CodexAccountUsageDecisionReason.ThresholdUnusable,
            _ => 0,
        };
        return reason != 0;
    }

    private static bool TryWindow(string? text, out CodexAccountUsageWindowKind window)
    {
        window = text switch
        {
            "primary" => CodexAccountUsageWindowKind.Primary,
            "secondary" => CodexAccountUsageWindowKind.Secondary,
            _ => 0,
        };
        return window != 0;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }
}
