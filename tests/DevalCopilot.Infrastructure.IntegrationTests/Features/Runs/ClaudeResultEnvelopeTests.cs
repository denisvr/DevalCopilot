using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0072 acceptance A at the shared reader: documented envelopes (<see cref="ClaudeDocumentedEnvelope"/>) and every negative shape,
/// classified with the process facts. Semantic success is only ever an exit zero with complete I/O and an object
/// <c>structured_output</c>; nothing on a non-zero exit is success; the narrative <c>result</c> is never returned.
/// </summary>
public sealed class ClaudeResultEnvelopeTests
{
    [Fact]
    public void A_documented_success_returns_only_the_structured_output_object()
    {
        var reading = Classify(ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(
            new JsonObject { ["verdict"] = "accept" }, narrative: "{\"verdict\":\"challenge\"}")));

        Assert.True(reading.IsSuccess);
        Assert.Null(reading.Diagnostic);
        Assert.Equal("{\"verdict\":\"accept\"}", reading.StructuredOutputJson);
        Assert.Equal("11111111-1111-4111-8111-111111111111", reading.SessionId);
    }

    [Fact]
    public void A_narrative_result_different_from_the_structured_output_is_not_a_contradiction()
    {
        var reading = Classify(ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(
            new JsonObject { ["verdict"] = "accept" }, narrative: "Something entirely different.")));

        Assert.True(reading.IsSuccess);
    }

    public static TheoryData<string, AgentInvocationDiagnostic> FailureShapes()
    {
        var data = new TheoryData<string, AgentInvocationDiagnostic>
        {
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.SuccessWithoutStructuredOutput()), AgentInvocationDiagnostic.StructuredOutputMissing },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(null)), AgentInvocationDiagnostic.StructuredOutputMissing },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(JsonValue.Create("{\"verdict\":\"accept\"}"))), AgentInvocationDiagnostic.InvalidStructuredOutput },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(new JsonArray())), AgentInvocationDiagnostic.InvalidStructuredOutput },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(JsonValue.Create(42))), AgentInvocationDiagnostic.InvalidStructuredOutput },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ReportedApiError(401)), AgentInvocationDiagnostic.AuthenticationRequired },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ReportedApiError(429)), AgentInvocationDiagnostic.ProviderLimitReached },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ReportedApiError(403)), AgentInvocationDiagnostic.ProviderReportedError },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ReportedApiError(null)), AgentInvocationDiagnostic.ProviderReportedError },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ErrorArm("error_max_turns")), AgentInvocationDiagnostic.ProviderReportedError },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ErrorArm("error_during_execution")), AgentInvocationDiagnostic.ProviderReportedError },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ErrorArm("error_max_budget_usd")), AgentInvocationDiagnostic.ProviderReportedError },
            { ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ErrorArm("error_max_structured_output_retries")), AgentInvocationDiagnostic.ProviderReportedError },
            { "", AgentInvocationDiagnostic.InvalidEnvelope },
            { "not json", AgentInvocationDiagnostic.InvalidEnvelope },
            { "[]", AgentInvocationDiagnostic.InvalidEnvelope },
            { "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"x\",\"structured_output\":{}", AgentInvocationDiagnostic.InvalidEnvelope },
            // The previous, undocumented shape is no longer accepted.
            { "{\"is_error\":false,\"result\":\"{}\",\"session_id\":null}", AgentInvocationDiagnostic.InvalidEnvelope },
        };

        data.Add(Mutate(envelope => envelope["type"] = "assistant"), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["subtype"] = "error_unknown"), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["subtype"] = 7), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["is_error"] = "false"), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope.Remove("is_error")), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope.Remove("result")), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["result"] = new JsonObject()), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["session_id"] = 12), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["session_id"] = new string('s', 257)), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["api_error_status"] = 401), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["api_error_status"] = 99), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["errors"] = new JsonArray("x")), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["errors"] = "x"), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(Mutate(envelope => envelope["is_error"] = true), AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(
            ClaudeDocumentedEnvelope.Text(WithError(ClaudeDocumentedEnvelope.ErrorArm("error_max_turns"), envelope => envelope["is_error"] = false)),
            AgentInvocationDiagnostic.InvalidEnvelope);
        data.Add(
            ClaudeDocumentedEnvelope.Text(WithError(
                ClaudeDocumentedEnvelope.ErrorArm("error_during_execution"), envelope => envelope["structured_output"] = new JsonObject())),
            AgentInvocationDiagnostic.InvalidEnvelope);
        return data;
    }

    [Theory]
    [MemberData(nameof(FailureShapes))]
    public void Every_other_shape_is_a_closed_failure_and_never_success(string standardOutput, AgentInvocationDiagnostic expected)
    {
        var reading = Classify(standardOutput);

        Assert.False(reading.IsSuccess);
        Assert.Null(reading.StructuredOutputJson);
        Assert.Equal(expected, reading.Diagnostic);
    }

    [Fact]
    public void A_duplicated_contract_property_rejects_the_envelope()
    {
        var text = ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(new JsonObject { ["verdict"] = "accept" }));
        var duplicated = text.Replace("\"is_error\":false", "\"is_error\":false,\"is_error\":true", StringComparison.Ordinal);

        Assert.Equal(AgentInvocationDiagnostic.InvalidEnvelope, Classify(duplicated).Diagnostic);
    }

    [Fact]
    public void A_duplicated_property_inside_the_structured_output_is_invalid_structured_output()
    {
        var text = ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(
            new JsonObject { ["nested"] = new JsonObject { ["verdict"] = "accept" } }));
        var duplicated = text.Replace("{\"verdict\":\"accept\"}", "{\"verdict\":\"accept\",\"verdict\":\"challenge\"}", StringComparison.Ordinal);

        Assert.Equal(AgentInvocationDiagnostic.InvalidStructuredOutput, Classify(duplicated).Diagnostic);
    }

    [Fact]
    public void An_envelope_deeper_than_the_bound_is_invalid()
    {
        var deep = new string('[', 80) + new string(']', 80);
        var text = ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(new JsonObject { ["v"] = "x" }))
            .Replace("\"x\"", deep, StringComparison.Ordinal);

        Assert.Equal(AgentInvocationDiagnostic.InvalidEnvelope, Classify(text).Diagnostic);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(143)]
    public void A_non_zero_exit_is_never_success_even_with_a_documented_success_envelope(int exitCode)
    {
        var success = ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(new JsonObject { ["verdict"] = "accept" }));

        var reading = Classify(success, exitCode: exitCode);

        Assert.False(reading.IsSuccess);
        Assert.Equal(AgentInvocationDiagnostic.InvalidEnvelope, reading.Diagnostic);
        Assert.Null(reading.TokenUsage);
    }

    [Fact]
    public void A_documented_failure_report_is_still_interpreted_on_a_non_zero_exit()
    {
        Assert.Equal(
            AgentInvocationDiagnostic.AuthenticationRequired,
            Classify(ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ReportedApiError(401)), exitCode: 1).Diagnostic);
        Assert.Equal(AgentInvocationDiagnostic.NonZeroExit, Classify("free text the provider printed", exitCode: 1).Diagnostic);
    }

    [Fact]
    public void Timeout_cancellation_incomplete_io_and_truncation_win_over_any_envelope()
    {
        var success = ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.Success(new JsonObject { ["verdict"] = "accept" }));

        Assert.Equal(AgentInvocationDiagnostic.TimedOut, Classify(success, outcome: ProcessExecutionOutcome.TimedOut, exitCode: null).Diagnostic);
        Assert.Equal(AgentInvocationDiagnostic.Cancelled, Classify(success, outcome: ProcessExecutionOutcome.Cancelled, exitCode: null).Diagnostic);
        Assert.Equal(
            AgentInvocationDiagnostic.CaptureDeadlineExceeded,
            Classify(success, io: new ProcessIoCompletion(
                OutputCaptureCompletion.DeadlineExceeded, OutputCaptureCompletion.Complete, InputDeliveryCompletion.Complete)).Diagnostic);
        Assert.Equal(
            AgentInvocationDiagnostic.CaptureFault,
            Classify(success, io: new ProcessIoCompletion(
                OutputCaptureCompletion.Complete, OutputCaptureCompletion.Complete, InputDeliveryCompletion.Faulted)).Diagnostic);
        Assert.Equal(AgentInvocationDiagnostic.CaptureFault, Classify(success, io: ProcessIoCompletion.Unknown).Diagnostic);
        Assert.Equal(AgentInvocationDiagnostic.InvalidEnvelope, Classify(success, truncated: true).Diagnostic);
    }

    [Fact]
    public void Usage_is_kept_only_from_a_valid_envelope_of_a_zero_exit_and_independently_of_success()
    {
        var reported = Classify(ClaudeDocumentedEnvelope.Text(ClaudeDocumentedEnvelope.ErrorArm("error_max_turns")));
        Assert.NotNull(reported.TokenUsage);

        var malformedUsage = ClaudeDocumentedEnvelope.Success(new JsonObject { ["verdict"] = "accept" });
        malformedUsage["usage"] = "not usage";
        var success = Classify(ClaudeDocumentedEnvelope.Text(malformedUsage));
        Assert.True(success.IsSuccess);
        Assert.Null(success.TokenUsage);
    }

    private static string Mutate(Action<JsonObject> mutation) =>
        ClaudeDocumentedEnvelope.Text(WithError(ClaudeDocumentedEnvelope.Success(new JsonObject { ["verdict"] = "accept" }), mutation));

    private static JsonObject WithError(JsonObject envelope, Action<JsonObject> mutation)
    {
        mutation(envelope);
        return envelope;
    }

    private static ClaudeInvocationReading Classify(
        string standardOutput,
        ProcessExecutionOutcome outcome = ProcessExecutionOutcome.Exited,
        int? exitCode = 0,
        ProcessIoCompletion? io = null,
        bool truncated = false) =>
        ClaudeResultEnvelope.Classify(new ProcessExecutionResult
        {
            Outcome = outcome,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardOutputTruncated = truncated,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromSeconds(1),
            IoCompletion = io ?? ProcessIoCompletion.CompleteWithInput,
        });
}
