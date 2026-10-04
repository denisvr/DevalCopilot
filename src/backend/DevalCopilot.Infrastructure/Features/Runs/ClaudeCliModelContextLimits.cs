using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Reads the optional <c>modelUsage</c> map from Claude Code's single <c>--print --output-format json</c> stdout envelope —
/// the same envelope every Claude adapter already parses for <c>is_error</c>, <c>result</c>, <c>session_id</c>, and
/// <c>usage</c>. Shared by the three Claude adapters because they parse one and the same provider CLI contract and must
/// change together with it.
///
/// The contract was evidenced from the official programmatic CLI guide and the Agent SDK TypeScript reference (which declare
/// <c>modelUsage</c> with <c>contextWindow</c> and <c>maxOutputTokens</c> members) and from the installed
/// <c>@anthropic-ai/claude-code@2.1.276</c> binary's public result-envelope schema, never from an authenticated invocation.
/// Only each map key (the model identifier) and its <c>contextWindow</c> and <c>maxOutputTokens</c> are read. Costs, usage
/// totals, <c>canonicalModel</c>, routing, and every other field are deliberately ignored, and no entry is interpreted as the
/// main model or a fallback.
///
/// Admission is all-or-unknown and bounded, and it fails closed to <see langword="null"/> — meaning "no evidence", never an
/// exception and never a partial or invented value — unless the envelope holds exactly one <c>modelUsage</c> object whose
/// every entry is valid. The entry shape is judged by the single Domain rule
/// <see cref="AgentModelContextLimitsEvidence.Validate"/> (1 to 16 unique ordinal identifiers of 1 to 128 ASCII characters
/// matching <c>[A-Za-z0-9][A-Za-z0-9._-]*</c>, positive Int32 limits, output not above the window); this reader adds only what
/// the raw JSON can show and the Domain rule cannot: a duplicated required member inside an entry and a limit that is not a JSON
/// integer fitting Int32. A missing or malformed map never rejects the envelope itself: the
/// caller's business result and token usage are judged independently.
/// </summary>
internal static class ClaudeCliModelContextLimits
{
    /// <summary>The parsing-contract tag recorded with every value this reader produces.</summary>
    internal const string Source = AgentModelContextLimitsEvidencePolicy.ClaudeCliSource;

    internal static AgentModelContextLimits? TryRead(JsonElement envelopeRoot)
    {
        if (envelopeRoot.ValueKind != JsonValueKind.Object
            || !TryGetUniqueMap(envelopeRoot, out var map)
            || map.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entries = new List<AgentModelContextLimitEntry>();
        foreach (var property in map.EnumerateObject())
        {
            if (entries.Count >= AgentModelContextLimitsEvidence.MaxModels
                || property.Value.ValueKind != JsonValueKind.Object
                || !TryReadEntry(property.Value, out var contextWindow, out var maxOutputTokens))
            {
                return null;
            }

            entries.Add(new AgentModelContextLimitEntry(property.Name, contextWindow, maxOutputTokens));
        }

        var shape = entries.Select(entry => new AgentModelContextLimit(entry.ModelId, entry.ContextWindowTokens, entry.MaxOutputTokens)).ToList();
        return AgentModelContextLimitsEvidence.Validate(Source, shape) is null ? new AgentModelContextLimits(Source, entries) : null;
    }

    /// <summary>A truncated stdout capture is never a complete provider report, so limits read from it stay unknown even in
    /// the unlikely case the retained prefix still parsed.</summary>
    internal static AgentModelContextLimits? UnlessTruncated(AgentModelContextLimits? limits, bool standardOutputTruncated) =>
        standardOutputTruncated ? null : limits;

    private static bool TryGetUniqueMap(JsonElement envelopeRoot, out JsonElement map)
    {
        map = default;
        var found = false;
        foreach (var property in envelopeRoot.EnumerateObject())
        {
            if (!property.NameEquals("modelUsage"))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            map = property.Value;
            found = true;
        }

        return found;
    }

    private static bool TryReadEntry(JsonElement entry, out int contextWindow, out int maxOutputTokens)
    {
        contextWindow = 0;
        maxOutputTokens = 0;
        var seenContextWindow = false;
        var seenMaxOutputTokens = false;
        foreach (var property in entry.EnumerateObject())
        {
            if (property.NameEquals("contextWindow"))
            {
                if (seenContextWindow || !TryReadLimit(property.Value, out contextWindow))
                {
                    return false;
                }

                seenContextWindow = true;
            }
            else if (property.NameEquals("maxOutputTokens"))
            {
                if (seenMaxOutputTokens || !TryReadLimit(property.Value, out maxOutputTokens))
                {
                    return false;
                }

                seenMaxOutputTokens = true;
            }
        }

        return seenContextWindow && seenMaxOutputTokens;
    }

    private static bool TryReadLimit(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
