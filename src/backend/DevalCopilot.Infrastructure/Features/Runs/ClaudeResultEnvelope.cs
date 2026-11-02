using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// ADR-0072: the one pure reader and classifier of the documented single result envelope that Claude Code prints with
/// <c>--print --output-format json --json-schema</c>, shared by the three role adapters. It owns no role, schema, argument,
/// authorization or artifact: each adapter keeps its own invocation and its own schema, and the role's recording handler still
/// validates the returned structured output against that schema. It never falls back to the narrative <c>result</c> text and never
/// reads provider text to classify anything.
///
/// The documented envelope (code.claude.com, "Run Claude Code programmatically" and the Agent SDK result message) is one object with
/// <c>type: "result"</c>, a <c>subtype</c> of <c>success</c> or one of the four documented <c>error_*</c> values, a boolean
/// <c>is_error</c>, a <c>session_id</c>, the narrative <c>result</c> string on the success arm, an <c>errors</c> string array on the
/// error arm, an optional <c>api_error_status</c> HTTP status on the success arm, and, when a run produced valid structured output,
/// a <c>structured_output</c> field.
///
/// Semantic success requires all of: an observed exit code zero, complete I/O, untruncated stdout, exactly one occurrence of every
/// root property, <c>type</c> "result", <c>subtype</c> "success", <c>is_error</c> false, a string <c>result</c>, no errors, no API error
/// status and a <c>structured_output</c> JSON object without duplicate property names. Everything else is a closed
/// <see cref="AgentInvocationDiagnostic"/>. The only specific provider categories come from the documented
/// <c>api_error_status</c> field: 401 is <see cref="AgentInvocationDiagnostic.AuthenticationRequired"/> and 429 is
/// <see cref="AgentInvocationDiagnostic.ProviderLimitReached"/>; every other reported error stays generic. A category is what was
/// reported, never a verified cause. Usage and model limits are read only from a structurally valid envelope of an observed zero exit
/// with complete I/O, each independently validated by its own reader.
/// </summary>
internal static class ClaudeResultEnvelope
{
    /// <summary>The largest stdout text this reader parses; the capture caps already bound it, this is defense in depth.</summary>
    internal const int MaxEnvelopeCharacters = 4 * 1024 * 1024;

    private const int MaxSessionIdLength = 256;
    private const int MaxJsonDepth = 64;

    private static readonly HashSet<string> DocumentedErrorSubtypes = new(StringComparer.Ordinal)
    {
        "error_max_turns",
        "error_during_execution",
        "error_max_budget_usd",
        "error_max_structured_output_retries",
    };

    /// <summary>Classifies one completed process result. Never throws for any output.</summary>
    public static ClaudeInvocationReading Classify(ProcessExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Outcome)
        {
            case ProcessExecutionOutcome.TimedOut:
                return ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.TimedOut);
            case ProcessExecutionOutcome.Cancelled:
                return ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.Cancelled);
            case ProcessExecutionOutcome.Exited when result.ExitCode is null:
                return ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.InvalidEnvelope);
        }

        if (!result.IoCompletion.IsComplete)
        {
            return ClaudeInvocationReading.Failure(result.IoCompletion.DeadlineExceeded
                ? AgentInvocationDiagnostic.CaptureDeadlineExceeded
                : AgentInvocationDiagnostic.CaptureFault);
        }

        var exitedZero = result.ExitCode == 0;
        if (result.StandardOutputTruncated)
        {
            return ClaudeInvocationReading.Failure(exitedZero ? AgentInvocationDiagnostic.InvalidEnvelope : AgentInvocationDiagnostic.NonZeroExit);
        }

        var envelope = Read(result.StandardOutput);
        if (!exitedZero)
        {
            // A failure report is still interpreted on a non-zero exit, but nothing on a non-zero exit is ever success.
            return envelope.Shape switch
            {
                EnvelopeShape.ReportedError => ClaudeInvocationReading.Failure(envelope.ReportedCategory),
                EnvelopeShape.Success => ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.InvalidEnvelope),
                _ => ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.NonZeroExit),
            };
        }

        return envelope.Shape switch
        {
            EnvelopeShape.Invalid => ClaudeInvocationReading.Failure(AgentInvocationDiagnostic.InvalidEnvelope),
            EnvelopeShape.ReportedError => ClaudeInvocationReading.Failure(envelope.ReportedCategory, envelope.Usage, envelope.ModelLimits),
            _ when envelope.StructuredOutputJson is not null => ClaudeInvocationReading.Succeeded(
                envelope.StructuredOutputJson, envelope.SessionId, envelope.Usage, envelope.ModelLimits),
            _ => ClaudeInvocationReading.Failure(envelope.StructuredOutputCategory, envelope.Usage, envelope.ModelLimits),
        };
    }

    private static Envelope Read(string standardOutput)
    {
        if (standardOutput.Length == 0 || standardOutput.Length > MaxEnvelopeCharacters)
        {
            return Envelope.Invalid;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(standardOutput, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        }
        catch (JsonException)
        {
            return Envelope.Invalid;
        }

        using (document)
        {
            try
            {
                return ReadRoot(document.RootElement);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                return Envelope.Invalid;
            }
        }
    }

    private static Envelope ReadRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || HasDuplicatePropertyNames(root, recursive: false))
        {
            return Envelope.Invalid;
        }

        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "result"
            || !root.TryGetProperty("subtype", out var subtypeElement) || subtypeElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("is_error", out var isErrorElement)
            || isErrorElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !TryReadSessionId(root, out var sessionId)
            || !TryReadApiErrorStatus(root, out var apiErrorStatus)
            || !TryReadErrors(root, out var hasErrors))
        {
            return Envelope.Invalid;
        }

        var subtype = subtypeElement.GetString();
        var isError = isErrorElement.ValueKind == JsonValueKind.True;
        var hasStructuredOutput = root.TryGetProperty("structured_output", out var structuredOutput)
            && structuredOutput.ValueKind != JsonValueKind.Null;
        var usage = ClaudeCliTokenUsage.TryRead(root);
        var modelLimits = ClaudeCliModelContextLimits.TryRead(root);

        if (subtype is not null && DocumentedErrorSubtypes.Contains(subtype))
        {
            // The documented error arm: always an error. Claiming otherwise, or carrying structured output, contradicts it.
            if (!isError || hasStructuredOutput || apiErrorStatus is not null || root.TryGetProperty("result", out _))
            {
                return Envelope.Invalid;
            }

            return Envelope.Error(AgentInvocationDiagnostic.ProviderReportedError, sessionId, usage, modelLimits);
        }

        if (subtype != "success"
            || !root.TryGetProperty("result", out var narrative) || narrative.ValueKind != JsonValueKind.String
            || hasErrors)
        {
            return Envelope.Invalid;
        }

        if (isError)
        {
            if (hasStructuredOutput)
            {
                return Envelope.Invalid;
            }

            var category = apiErrorStatus switch
            {
                401 => AgentInvocationDiagnostic.AuthenticationRequired,
                429 => AgentInvocationDiagnostic.ProviderLimitReached,
                _ => AgentInvocationDiagnostic.ProviderReportedError,
            };
            return Envelope.Error(category, sessionId, usage, modelLimits);
        }

        if (apiErrorStatus is not null)
        {
            return Envelope.Invalid;
        }

        if (!hasStructuredOutput)
        {
            return Envelope.SuccessWithout(AgentInvocationDiagnostic.StructuredOutputMissing, sessionId, usage, modelLimits);
        }

        if (structuredOutput.ValueKind != JsonValueKind.Object || HasDuplicatePropertyNames(structuredOutput, recursive: true))
        {
            return Envelope.SuccessWithout(AgentInvocationDiagnostic.InvalidStructuredOutput, sessionId, usage, modelLimits);
        }

        return Envelope.Success(structuredOutput.GetRawText(), sessionId, usage, modelLimits);
    }

    /// <summary>An absent or JSON null session id is tolerated (the attempt never persists a session); any other value must be a
    /// non-blank, bounded string.</summary>
    private static bool TryReadSessionId(JsonElement root, out string? sessionId)
    {
        sessionId = null;
        if (!root.TryGetProperty("session_id", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSessionIdLength)
        {
            return false;
        }

        sessionId = value;
        return true;
    }

    /// <summary>The documented optional HTTP status: absent, null or a whole number from 100 to 599.</summary>
    private static bool TryReadApiErrorStatus(JsonElement root, out int? status)
    {
        status = null;
        if (!root.TryGetProperty("api_error_status", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value is < 100 or > 599)
        {
            return false;
        }

        status = value;
        return true;
    }

    /// <summary>The documented error list: absent, or an array of strings whose content is never read.</summary>
    private static bool TryReadErrors(JsonElement root, out bool hasErrors)
    {
        hasErrors = false;
        if (!root.TryGetProperty("errors", out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            hasErrors = true;
        }

        return true;
    }

    private static bool HasDuplicatePropertyNames(JsonElement element, bool recursive)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name) || (recursive && HasDuplicatePropertyNames(property.Value, recursive)))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array when recursive:
                foreach (var item in element.EnumerateArray())
                {
                    if (HasDuplicatePropertyNames(item, recursive))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private enum EnvelopeShape
    {
        Invalid,
        ReportedError,
        Success,
    }

    private sealed record Envelope(
        EnvelopeShape Shape,
        AgentInvocationDiagnostic ReportedCategory,
        AgentInvocationDiagnostic StructuredOutputCategory,
        string? StructuredOutputJson,
        string? SessionId,
        AgentTokenUsage? Usage,
        AgentModelContextLimits? ModelLimits)
    {
        public static Envelope Invalid { get; } = new(
            EnvelopeShape.Invalid, AgentInvocationDiagnostic.InvalidEnvelope, AgentInvocationDiagnostic.InvalidEnvelope, null, null, null, null);

        public static Envelope Error(
            AgentInvocationDiagnostic category, string? sessionId, AgentTokenUsage? usage, AgentModelContextLimits? limits) =>
            new(EnvelopeShape.ReportedError, category, category, null, sessionId, usage, limits);

        public static Envelope SuccessWithout(
            AgentInvocationDiagnostic category, string? sessionId, AgentTokenUsage? usage, AgentModelContextLimits? limits) =>
            new(EnvelopeShape.Success, category, category, null, sessionId, usage, limits);

        public static Envelope Success(
            string structuredOutputJson, string? sessionId, AgentTokenUsage? usage, AgentModelContextLimits? limits) =>
            new(EnvelopeShape.Success, AgentInvocationDiagnostic.InvalidEnvelope, AgentInvocationDiagnostic.InvalidEnvelope,
                structuredOutputJson, sessionId, usage, limits);
    }
}
