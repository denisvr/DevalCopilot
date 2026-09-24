using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Reads the <c>usage</c> object from Claude Code's single <c>--print --output-format json</c>
/// stdout envelope — the same envelope every Claude adapter already parses for <c>is_error</c>,
/// <c>result</c>, and <c>session_id</c>. Shared by the three Claude adapters because they parse one
/// and the same provider CLI contract and must change together with it.
///
/// The contract was evidenced from the installed <c>@anthropic-ai/claude-code@2.1.276</c> native
/// binary's own embedded strings, never from an authenticated invocation: its result-envelope
/// schema declares <c>usage</c> as a sibling of <c>is_error</c>/<c>result</c>/<c>session_id</c>,
/// its usage schema documentation names exactly <c>input_tokens</c>, <c>output_tokens</c>,
/// <c>cache_creation_input_tokens</c>, and <c>cache_read_input_tokens</c> as numbers, and its
/// default usage literal initializes all four to <c>0</c>. Only those four members are read;
/// <c>modelUsage</c>, <c>total_cost_usd</c>, and every other field are deliberately ignored.
///
/// Parsing is defensive and fails closed to <see langword="null"/> — meaning "no usage evidence",
/// never an exception and never an invented or partial value — when <c>usage</c> is missing or
/// not a JSON object, or when any of the four members is missing, not a JSON number, not an
/// integer, negative, outside the <see cref="int"/> range, or duplicated at the envelope or
/// required-member level. A missing or malformed <c>usage</c>
/// never rejects the envelope itself: the caller's business result is judged independently.
/// </summary>
internal static class ClaudeCliTokenUsage
{
    /// <summary>The parsing-contract tag recorded with every usage value this reader produces.</summary>
    internal const string SchemaVersion = AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion;

    internal static AgentTokenUsage? TryRead(JsonElement envelopeRoot)
    {
        if (envelopeRoot.ValueKind != JsonValueKind.Object
            || !TryGetUniqueUsage(envelopeRoot, out var usage)
            || usage.ValueKind != JsonValueKind.Object
            || !HasUniqueCountMembers(usage)
            || !TryReadCount(usage, "input_tokens", out var inputTokens)
            || !TryReadCount(usage, "output_tokens", out var outputTokens)
            || !TryReadCount(usage, "cache_creation_input_tokens", out var cacheCreationInputTokens)
            || !TryReadCount(usage, "cache_read_input_tokens", out var cacheReadInputTokens))
        {
            return null;
        }

        return new AgentTokenUsage(inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens, SchemaVersion);
    }

    /// <summary>A truncated stdout capture is never a complete provider report, so any usage read
    /// from it stays unknown even in the unlikely case the retained prefix still parsed.</summary>
    internal static AgentTokenUsage? UnlessTruncated(AgentTokenUsage? usage, bool standardOutputTruncated) =>
        standardOutputTruncated ? null : usage;

    private static bool TryGetUniqueUsage(JsonElement envelopeRoot, out JsonElement usage)
    {
        usage = default;
        var found = false;
        foreach (var property in envelopeRoot.EnumerateObject())
        {
            if (!property.NameEquals("usage"))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            usage = property.Value;
            found = true;
        }

        return found;
    }

    private static bool HasUniqueCountMembers(JsonElement usage)
    {
        var seen = 0;
        foreach (var property in usage.EnumerateObject())
        {
            var bit = property.Name switch
            {
                "input_tokens" => 1,
                "output_tokens" => 2,
                "cache_creation_input_tokens" => 4,
                "cache_read_input_tokens" => 8,
                _ => 0,
            };
            if (bit != 0 && (seen & bit) != 0)
            {
                return false;
            }

            seen |= bit;
        }

        return true;
    }

    private static bool TryReadCount(JsonElement usage, string propertyName, out int value)
    {
        value = 0;
        return usage.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value)
            && value >= 0;
    }
}
