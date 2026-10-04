using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The model identifiers a provider listed in its own result for one Agent attempt's invocation, each with the
/// context-window and maximum-output token limits the provider reported, and the fixed parsing-contract
/// <see cref="Source"/> that produced them. Historical, provider-reported observation only: never remaining context,
/// next-invocation capacity, a live capability, an eligibility decision, or proof that a listed model was used, and
/// never a semantic classification of the attempt (that remains <see cref="AgentOutcome"/>). An attempt whose provider
/// did not report the limits in a proven, safely parsed form has no instance of this type at all; absence is never
/// replaced by a requested, default, or inferred value.
///
/// The persisted form is the single project-owned canonical snapshot <see cref="Serialize"/> writes, independent of
/// any provider's field names. <see cref="FromPersisted"/> accepts exactly that text and nothing else.
/// </summary>
public sealed record AgentModelContextLimitsEvidence
{
    /// <summary>The version of the persisted snapshot shape, written into and required from the stored text.</summary>
    public const int SnapshotVersion = 1;

    /// <summary>The most model entries one snapshot may hold.</summary>
    public const int MaxModels = 16;

    /// <summary>The longest accepted model identifier.</summary>
    public const int MaxModelIdLength = 128;

    /// <summary>Matches the existing bound for version-tag strings such as
    /// <see cref="Attempt.AgentAdapterContractVersion"/>.</summary>
    public const int MaxSourceLength = 128;

    /// <summary>The largest accepted serialized snapshot, in UTF-8 bytes.</summary>
    public const int MaxSerializedBytes = 4096;

    private AgentModelContextLimitsEvidence(string source, ImmutableArray<AgentModelContextLimit> models)
    {
        Source = source;
        Models = models;
    }

    /// <summary>The adapter-owned parsing-contract tag that produced this evidence, so a future provider schema change
    /// is attributable rather than silently reinterpreted. Internal provenance, never a UI-facing value.</summary>
    public string Source { get; }

    /// <summary>The listed models, ordered ordinally by identifier. An immutable snapshot this value owns: no collection the
    /// caller supplied is shared with it, and nothing exposed here can be written through, so what was validated is exactly what
    /// is reported, serialized and recorded.</summary>
    public ImmutableArray<AgentModelContextLimit> Models { get; }

    /// <summary>Creates validated evidence with its entries ordered ordinally by identifier, throwing
    /// <see cref="ArgumentException"/> for any shape <see cref="Validate"/> rejects.</summary>
    public static AgentModelContextLimitsEvidence Create(string source, IReadOnlyList<AgentModelContextLimit> models)
    {
        // Copied once, boundedly, and only the copy is judged and kept: a collection the caller still holds can neither change
        // between the check and the use nor be shared with the evidence.
        var violation = Snapshot(models, out var snapshot) ?? ValidateSnapshot(source, snapshot);
        if (violation is not null)
        {
            throw new ArgumentException($"Invalid agent model context-limit evidence: {violation}.", nameof(models));
        }

        return new AgentModelContextLimitsEvidence(source, Order(snapshot!));
    }

    /// <summary>Returns the first shape violation, or <see langword="null"/> when the shape is valid.</summary>
    public static AgentModelContextLimitsEvidenceViolation? Validate(string? source, IReadOnlyList<AgentModelContextLimit>? models) =>
        Snapshot(models, out var snapshot) ?? ValidateSnapshot(source, snapshot);

    /// <summary>The count is judged before any entry is read, so a null, empty or excessive collection is refused without a
    /// traversal or a copy; an acceptable one is copied once into storage this type owns.</summary>
    private static AgentModelContextLimitsEvidenceViolation? Snapshot(
        IReadOnlyList<AgentModelContextLimit>? models, out AgentModelContextLimit?[] snapshot)
    {
        snapshot = [];
        if (models is null)
        {
            return AgentModelContextLimitsEvidenceViolation.NoModels;
        }

        var count = models.Count;
        if (count == 0)
        {
            return AgentModelContextLimitsEvidenceViolation.NoModels;
        }

        if (count > MaxModels)
        {
            return AgentModelContextLimitsEvidenceViolation.TooManyModels;
        }

        snapshot = new AgentModelContextLimit?[count];
        for (var index = 0; index < count; index++)
        {
            snapshot[index] = models[index];
        }

        return null;
    }

    private static AgentModelContextLimitsEvidenceViolation? ValidateSnapshot(string? source, AgentModelContextLimit?[] models)
    {
        if (string.IsNullOrEmpty(source) || source.Length > MaxSourceLength || !source.All(character => character is > ' ' and <= '~'))
        {
            return AgentModelContextLimitsEvidenceViolation.InvalidSource;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (model is null || !IsValidModelId(model.ModelId))
            {
                return AgentModelContextLimitsEvidenceViolation.InvalidModelId;
            }

            if (!seen.Add(model.ModelId))
            {
                return AgentModelContextLimitsEvidenceViolation.DuplicateModelId;
            }

            if (model.ContextWindowTokens < 1)
            {
                return AgentModelContextLimitsEvidenceViolation.InvalidContextWindowTokens;
            }

            if (model.MaxOutputTokens < 1)
            {
                return AgentModelContextLimitsEvidenceViolation.InvalidMaxOutputTokens;
            }

            if (model.MaxOutputTokens > model.ContextWindowTokens)
            {
                return AgentModelContextLimitsEvidenceViolation.MaxOutputExceedsContextWindow;
            }
        }

        return Encoding.UTF8.GetByteCount(Write(source, Order(models))) > MaxSerializedBytes
            ? AgentModelContextLimitsEvidenceViolation.TooLarge
            : null;
    }

    /// <summary>Reconstructs evidence from the one persisted text, returning <see langword="null"/> (unknown, never
    /// partially trusted) unless that text is exactly the canonical <see cref="Serialize"/> form of valid evidence from
    /// a proven provider/source pair: not oversized, not malformed, not a different version, not another provider's,
    /// not reordered, and carrying no extra, missing, or duplicated member. Never throws.</summary>
    public static AgentModelContextLimitsEvidence? FromPersisted(AgentProvider? provider, string? stored)
    {
        if (stored is null || Encoding.UTF8.GetByteCount(stored) > MaxSerializedBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(stored);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != SnapshotVersion
                || !root.TryGetProperty("source", out var sourceElement)
                || sourceElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("models", out var modelsElement)
                || modelsElement.ValueKind != JsonValueKind.Array
                || modelsElement.GetArrayLength() > MaxModels)
            {
                return null;
            }

            var source = sourceElement.GetString();
            var models = new List<AgentModelContextLimit>();
            foreach (var entry in modelsElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("modelId", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || !entry.TryGetProperty("contextWindowTokens", out var window)
                    || window.ValueKind != JsonValueKind.Number
                    || !window.TryGetInt32(out var windowTokens)
                    || !entry.TryGetProperty("maxOutputTokens", out var maxOutput)
                    || maxOutput.ValueKind != JsonValueKind.Number
                    || !maxOutput.TryGetInt32(out var maxOutputTokens))
                {
                    return null;
                }

                models.Add(new AgentModelContextLimit(id.GetString()!, windowTokens, maxOutputTokens));
            }

            if (!AgentModelContextLimitsEvidencePolicy.IsSupportedSource(provider, source)
                || Validate(source, models) is not null)
            {
                return null;
            }

            var evidence = new AgentModelContextLimitsEvidence(source!, Order(models));
            return string.Equals(evidence.Serialize(), stored, StringComparison.Ordinal) ? evidence : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The one canonical persisted text: the snapshot version, the source tag, and the ordinally ordered
    /// entries, written compactly with fixed member names and order.</summary>
    public string Serialize() => Write(Source, Models);

    // Always a new array, so the result shares storage with nothing the caller could still reach. Every entry is already known
    // to be non-null when this is called.
    private static ImmutableArray<AgentModelContextLimit> Order(IEnumerable<AgentModelContextLimit?> models) =>
        [.. models.OrderBy(model => model!.ModelId, StringComparer.Ordinal).Select(model => model!)];

    private static bool IsValidModelId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxModelIdLength)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var alphanumeric = character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');
            if (!alphanumeric && (index == 0 || character is not ('.' or '_' or '-')))
            {
                return false;
            }
        }

        return true;
    }

    private static string Write(string source, IEnumerable<AgentModelContextLimit> models)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", SnapshotVersion);
            writer.WriteString("source", source);
            writer.WriteStartArray("models");
            foreach (var model in models)
            {
                writer.WriteStartObject();
                writer.WriteString("modelId", model.ModelId);
                writer.WriteNumber("contextWindowTokens", model.ContextWindowTokens);
                writer.WriteNumber("maxOutputTokens", model.MaxOutputTokens);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
