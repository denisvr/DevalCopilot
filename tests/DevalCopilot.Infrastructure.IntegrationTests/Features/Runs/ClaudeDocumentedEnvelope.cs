using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0072: result-envelope fixtures built only from the documented Claude Code <c>--output-format json</c> result message
/// (code.claude.com "Run Claude Code programmatically" and the Agent SDK <c>SDKResultMessage</c> type), independently of the reader
/// under test: the success arm carries <c>type</c>, <c>subtype: "success"</c>, <c>is_error</c>, <c>result</c>, <c>session_id</c>,
/// usage metadata and, for a valid structured run, <c>structured_output</c>; the error arm carries one of the four documented
/// <c>error_*</c> subtypes and an <c>errors</c> string array; <c>api_error_status</c> is the documented optional HTTP status.
/// No real provider output is copied.
/// </summary>
internal static class ClaudeDocumentedEnvelope
{
    public static JsonObject Success(JsonNode? structuredOutput, string narrative = "Done.") => new()
    {
        ["type"] = "result",
        ["subtype"] = "success",
        ["uuid"] = "00000000-0000-4000-8000-000000000001",
        ["session_id"] = "11111111-1111-4111-8111-111111111111",
        ["duration_ms"] = 1200,
        ["duration_api_ms"] = 1100,
        ["is_error"] = false,
        ["num_turns"] = 1,
        ["result"] = narrative,
        ["stop_reason"] = "end_turn",
        ["total_cost_usd"] = 0.01,
        ["usage"] = new JsonObject
        {
            ["input_tokens"] = 10,
            ["output_tokens"] = 20,
            ["cache_creation_input_tokens"] = 0,
            ["cache_read_input_tokens"] = 0,
        },
        ["modelUsage"] = new JsonObject(),
        ["permission_denials"] = new JsonArray(),
        ["structured_output"] = structuredOutput,
    };

    public static JsonObject SuccessWithoutStructuredOutput()
    {
        var envelope = Success(null);
        envelope.Remove("structured_output");
        return envelope;
    }

    /// <summary>The success arm reporting an API error, as for a failed authentication or a rate limit.</summary>
    public static JsonObject ReportedApiError(int? apiErrorStatus, string narrative = "Provider error text that is never read.")
    {
        var envelope = Success(null, narrative);
        envelope.Remove("structured_output");
        envelope["is_error"] = true;
        envelope["api_error_status"] = apiErrorStatus is { } status ? JsonValue.Create(status) : null;
        return envelope;
    }

    public static JsonObject ErrorArm(string subtype) => new()
    {
        ["type"] = "result",
        ["subtype"] = subtype,
        ["uuid"] = "00000000-0000-4000-8000-000000000002",
        ["session_id"] = "11111111-1111-4111-8111-111111111111",
        ["duration_ms"] = 1200,
        ["duration_api_ms"] = 1100,
        ["is_error"] = true,
        ["num_turns"] = 1,
        ["stop_reason"] = null,
        ["total_cost_usd"] = 0.01,
        ["usage"] = new JsonObject
        {
            ["input_tokens"] = 1,
            ["output_tokens"] = 1,
            ["cache_creation_input_tokens"] = 0,
            ["cache_read_input_tokens"] = 0,
        },
        ["modelUsage"] = new JsonObject(),
        ["permission_denials"] = new JsonArray(),
        ["errors"] = new JsonArray("Error text that is never read."),
    };

    public static string Text(JsonObject envelope) => envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    /// <summary>A successful envelope whose structured output is <paramref name="structuredOutputJson"/> parsed as an object.</summary>
    public static string SuccessText(string structuredOutputJson) => Text(Success(JsonNode.Parse(structuredOutputJson)));

    /// <summary>The same, reporting <paramref name="sessionId"/> (null is the documented-tolerated absence of a persisted session).</summary>
    public static string SuccessText(string structuredOutputJson, string? sessionId)
    {
        var envelope = Success(JsonNode.Parse(structuredOutputJson));
        envelope["session_id"] = sessionId;
        return Text(envelope);
    }
}
