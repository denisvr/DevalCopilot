using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Reads provider-reported token usage from the unique terminal <c>turn.completed</c> JSONL event
/// on Codex's <c>exec --json</c> non-interactive stdout stream — the same already-captured stdout
/// <see cref="CodexProcessInvoker"/> already scans for the provider session identifier.
///
/// The contract was verified from the installed Codex CLI (<c>codex-cli 0.155.0-alpha.16.4</c>,
/// <c>codex exec --help</c> documents <c>--json</c> as "Print events to stdout as JSONL") against
/// the official non-interactive-mode documentation
/// (<c>https://developers.openai.com/codex/noninteractive</c>), never from an authenticated model
/// invocation: the documented terminal-turn event is tagged <c>"type":"turn.completed"</c> and
/// carries a <c>usage</c> object with exactly <c>input_tokens</c>, <c>cached_input_tokens</c>,
/// <c>output_tokens</c>, and <c>reasoning_output_tokens</c>, e.g.
/// <c>{"type":"turn.completed","usage":{"input_tokens":24763,"cached_input_tokens":24448,"output_tokens":122,"reasoning_output_tokens":0}}</c>.
/// The installed executable's own embedded strings independently confirm the <c>turn.completed</c>
/// event tag (alongside <c>thread.started</c>, <c>turn.started</c>, <c>turn.failed</c>, and
/// <c>item.*</c>) and every one of those four member names, alongside a richer internal
/// <c>TokenUsage</c>/<c>TokenUsageInfo</c> shape this documented stream does not expose.
///
/// Only <c>input_tokens</c> and <c>output_tokens</c> become <see cref="AgentTokenUsage"/>'s
/// required counts. <c>cached_input_tokens</c> and <c>reasoning_output_tokens</c> are read only to
/// confirm the documented shape is complete, then discarded: <c>cached_input_tokens</c> is
/// deliberately never written into <see cref="AgentTokenUsage.CacheCreationInputTokens"/> or
/// <see cref="AgentTokenUsage.CacheReadInputTokens"/>, because Codex's single "served from cache"
/// count has no proven correspondence to Claude's separate cache-creation/cache-read breakdown, and
/// this evidence type has no reasoning-token member. Both cache fields stay <see langword="null"/>
/// for every Codex-produced usage value.
///
/// Parsing is defensive and fails closed to <see langword="null"/> — never an exception, zero, or a
/// partially-trusted value — for malformed JSONL, an absent, duplicated, or contradictory terminal
/// turn event (more than one <c>turn.completed</c>, or a <c>turn.completed</c> alongside a
/// <c>turn.failed</c>), a missing or malformed <c>usage</c> object, a missing, duplicated,
/// non-integer, negative, or out-of-<see cref="int"/>-range count, or a truncated stdout capture
/// even when the retained prefix still parses. Any non-empty line that is not valid JSON, whose
/// root is not an object, whose root <c>type</c> member is missing or not a string, or whose root
/// declares <c>type</c> more than once (ambiguous — a naive single-value read could silently select
/// one declared type while concealing a conflicting one, such as a second <c>turn.failed</c>
/// declaration on the very event this reader would otherwise trust) makes the <em>entire</em>
/// stdout capture untrustworthy, never just that one line: this reader never learns whether the
/// unreadable or ambiguous line was itself hiding, duplicating, or truncating the real terminal
/// event. Only a line that is valid JSON, an object, and carries exactly one string <c>type</c>
/// naming an event this reader does not otherwise recognize is safely ignored — Codex's JSONL
/// stream freely interleaves other event types this reader has no reason to parse.
/// </summary>
internal static class CodexCliTokenUsage
{
    /// <summary>The parsing-contract tag recorded with every usage value this reader produces.</summary>
    internal const string SchemaVersion = AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion;

    private const string TurnCompletedType = "turn.completed";
    private const string TurnFailedType = "turn.failed";

    internal static AgentTokenUsage? TryRead(string standardOutput)
    {
        if (!TryGetUniqueTerminalTurnCompletedEvent(standardOutput, out var turnCompleted)
            || !TryGetUniqueUsage(turnCompleted, out var usage)
            || usage.ValueKind != JsonValueKind.Object
            || !HasUniqueCountMembers(usage)
            || !TryReadCount(usage, "input_tokens", out var inputTokens)
            || !TryReadCount(usage, "cached_input_tokens", out _)
            || !TryReadCount(usage, "output_tokens", out var outputTokens)
            || !TryReadCount(usage, "reasoning_output_tokens", out _))
        {
            return null;
        }

        return new AgentTokenUsage(inputTokens, outputTokens, null, null, SchemaVersion);
    }

    /// <summary>A truncated stdout capture is never a complete provider report, so any usage read
    /// from it stays unknown even in the unlikely case the retained prefix still parsed.</summary>
    internal static AgentTokenUsage? UnlessTruncated(AgentTokenUsage? usage, bool standardOutputTruncated) =>
        standardOutputTruncated ? null : usage;

    /// <summary>Scans every JSONL line for the closed set of terminal turn events. Accepts usage
    /// only when every non-empty line parsed as a well-formed, unambiguously-typed event, and the
    /// stream contains exactly one terminal turn event in total and it is <c>turn.completed</c> —
    /// a second terminal event of either kind, or a lone <c>turn.failed</c>, is a duplicate or
    /// contradictory terminal signal and yields no usage. Any line that is not valid JSON, is not a
    /// JSON object, or does not declare <c>type</c> exactly once as a string aborts the entire scan
    /// immediately: it never merely skips that line, because a line this reader cannot safely read
    /// might be concealing or replacing the real terminal event rather than being harmless
    /// noise.</summary>
    private static bool TryGetUniqueTerminalTurnCompletedEvent(string standardOutput, out JsonElement turnCompleted)
    {
        turnCompleted = default;
        var terminalEventCount = 0;
        var sawTurnCompleted = false;

        foreach (var line in standardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !TryGetUniqueType(root, out var type))
                {
                    return false;
                }

                if (type == TurnCompletedType)
                {
                    terminalEventCount++;
                    sawTurnCompleted = true;
                    turnCompleted = root.Clone();
                }
                else if (type == TurnFailedType)
                {
                    terminalEventCount++;
                }

                // Any other well-formed, unambiguously-typed event is simply not one this reader
                // has any reason to inspect further.
            }
        }

        return terminalEventCount == 1 && sawTurnCompleted;
    }

    /// <summary>Requires exactly one <c>type</c> member with a string value. A missing member, a
    /// non-string value, or a second <c>type</c> member of any value is ambiguous or unreadable —
    /// never resolved by taking the first or last match, since either could be concealing the
    /// event's real, conflicting declared type.</summary>
    private static bool TryGetUniqueType(JsonElement root, out string type)
    {
        type = "";
        var found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.NameEquals("type"))
            {
                continue;
            }

            if (found || property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            type = property.Value.GetString() ?? "";
            found = true;
        }

        return found;
    }

    private static bool TryGetUniqueUsage(JsonElement turnCompleted, out JsonElement usage)
    {
        usage = default;
        var found = false;
        foreach (var property in turnCompleted.EnumerateObject())
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
                "cached_input_tokens" => 2,
                "output_tokens" => 4,
                "reasoning_output_tokens" => 8,
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
