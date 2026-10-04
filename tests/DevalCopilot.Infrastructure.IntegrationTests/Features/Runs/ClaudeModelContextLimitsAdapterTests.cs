using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Proves the three Claude adapters read the model identifiers and each one's reported context-window and
/// maximum-output limits from the optional <c>modelUsage</c> member of the single <c>--output-format json</c> envelope,
/// admitting the whole map or none of it, independently of the business result and of token usage, and without changing
/// any invocation argument. Every case uses a scripted process adapter with deterministic stdout; no real provider or
/// model is invoked.
/// </summary>
public sealed class ClaudeModelContextLimitsAdapterTests : IDisposable
{
    private const string ValidResult = "\"result\":\"{\\\"summary\\\":\\\"ok\\\"}\"";
    private const string ValidUsage =
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}";
    private const string Source = "claude-cli-model-usage-v1";

    private static readonly AgentTokenUsage ExpectedUsage = new(1200, 345, 67, 890, "claude-cli-usage-v1");

    private static string Entry(string id, string window = "200000", string output = "32000") =>
        $"\"{id}\":{{\"contextWindow\":{window},\"maxOutputTokens\":{output}}}";

    private static string Map(params string[] entries) => "\"modelUsage\":{" + string.Join(",", entries) + "}";

    private static readonly string SingleMap = Map(Entry("claude-journey-a1"));

    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-limits-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-limits-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public ClaudeModelContextLimitsAdapterTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_workspacePath);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _artifactRoot, _workspacePath })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Every_claude_adapter_reports_the_one_listed_model_with_its_reported_limits()
    {
        var stdout = Envelope(isError: false, ValidResult, Map(Entry("claude-journey-a1", "200000", "32000")));

        foreach (var result in await InvokeAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            var limits = Assert.IsType<AgentModelContextLimits>(result.Limits);
            Assert.Equal(Source, limits.Source);
            Assert.Equal(new AgentModelContextLimitEntry("claude-journey-a1", 200000, 32000), Assert.Single(limits.Models));
        }
    }

    [Fact]
    public async Task Every_claude_adapter_reports_every_listed_model_with_its_own_limits_and_ignores_unrelated_metadata()
    {
        var stdout = Envelope(
            isError: false,
            ValidResult,
            "\"total_cost_usd\":1.5",
            "\"canonicalModel\":\"claude-not-a-limit\"",
            "\"modelUsage\":{"
            + "\"claude-journey-b2\":{\"inputTokens\":9,\"costUSD\":0.1,\"webSearchRequests\":0,\"contextWindow\":1000000,\"maxOutputTokens\":64000},"
            + "\"claude-journey-a1\":{\"contextWindow\":200000,\"maxOutputTokens\":200000,\"extra\":{\"contextWindow\":1}}}");

        foreach (var result in await InvokeAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            var limits = Assert.IsType<AgentModelContextLimits>(result.Limits);
            Assert.Equal(Source, limits.Source);
            Assert.Equal(
                [
                    new AgentModelContextLimitEntry("claude-journey-b2", 1000000, 64000),
                    new AgentModelContextLimitEntry("claude-journey-a1", 200000, 200000),
                ],
                limits.Models.AsEnumerable());
        }
    }

    [Fact]
    public async Task Every_claude_adapter_admits_the_largest_valid_identifier_and_the_sixteen_entry_bound_and_int32_limits()
    {
        var longest = "a" + new string('b', 127);
        var sixteen = Enumerable.Range(0, 16).Select(index => Entry($"model-{index:00}")).ToArray();
        Assert.Equal(128, longest.Length);

        foreach (var map in new[]
                 {
                     Map(Entry(longest)), Map(sixteen), Map(Entry("m", "2147483647", "2147483647")), Map(Entry("A0._-z", "1", "1")),
                     Map(Entry("claude-x", "100", "100")),
                 })
        {
            foreach (var result in await InvokeAsync(Clean(Envelope(isError: false, ValidResult, map))))
            {
                Assert.True(result.Succeeded, result.Adapter);
                Assert.NotNull(result.Limits);
            }
        }
    }

    public static TheoryData<string> RejectedMaps => new()
    {
        // Wrong shapes for the map itself and an empty map.
        "\"modelUsage\":null", "\"modelUsage\":[]", "\"modelUsage\":\"x\"", "\"modelUsage\":7", "\"modelUsage\":{}",
        // Unsupported identifiers: empty, leading punctuation, outside ASCII, a space, a bracket suffix, too long, an escape that
        // decodes to an unsupported character.
        Map(Entry("")), Map(Entry("-model")), Map(Entry(".model")), Map(Entry("_model")), Map(Entry("modèl")), Map(Entry("a b")),
        Map(Entry("claude-sonnet-4-5[1m]")), Map(Entry("a" + new string('b', 128))), Map(Entry("a\\u0020b")), Map(Entry("a/b")),
        // More than sixteen entries.
        Map(Enumerable.Range(0, 17).Select(index => Entry($"model-{index:00}")).ToArray()),
        // Entries that are not objects.
        "\"modelUsage\":{\"m\":null}", "\"modelUsage\":{\"m\":5}", "\"modelUsage\":{\"m\":[]}", "\"modelUsage\":{\"m\":\"x\"}",
        // A missing required member.
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000}}", "\"modelUsage\":{\"m\":{\"maxOutputTokens\":32000}}",
        "\"modelUsage\":{\"m\":{\"inputTokens\":5}}",
        // Duplicate required members are ambiguous in either order; never accept the last or the first value.
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"contextWindow\":1,\"maxOutputTokens\":1}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":1,\"contextWindow\":200000,\"maxOutputTokens\":1}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":1,\"maxOutputTokens\":1}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000,\"maxOutputTokens\":32000}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"contextWindow\":200000,\"maxOutputTokens\":32000}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"\\u0063ontextWindow\":200000,\"maxOutputTokens\":32000}}",
        // Duplicate identifiers, literal or by escape, even when every copy is valid.
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000},\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}",
        "\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000},\"\\u006d\":{\"contextWindow\":1,\"maxOutputTokens\":1}}",
        // Invalid numbers: zero, negative, fractional, exponent, string, null, boolean, beyond Int32.
        Map(Entry("m", "0", "1")), Map(Entry("m", "1", "0")), Map(Entry("m", "-1", "1")), Map(Entry("m", "200000", "-1")),
        Map(Entry("m", "200000.5", "32000")), Map(Entry("m", "200000", "32000.5")), Map(Entry("m", "200000.0", "32000")),
        Map(Entry("m", "2e5", "32000")), Map(Entry("m", "\"200000\"", "32000")), Map(Entry("m", "200000", "\"32000\"")),
        Map(Entry("m", "null", "32000")), Map(Entry("m", "200000", "null")), Map(Entry("m", "true", "32000")),
        Map(Entry("m", "2147483648", "1")), Map(Entry("m", "200000", "2147483648")), Map(Entry("m", "99999999999999999999", "1")),
        // Contradictory limits: the output cannot exceed the window.
        Map(Entry("m", "1000", "1001")), Map(Entry("m", "1", "2")),
        // All-or-unknown: one valid entry never survives beside a malformed one, in either order.
        Map(Entry("good"), Entry("bad", "-1", "1")), Map(Entry("bad", "-1", "1"), Entry("good")), Map(Entry("good"), Entry("bad", "5", "6")),
        Map(Entry("good"), Entry("bad id")),
        // A duplicate root map is ambiguous.
        SingleMap + "," + Map(Entry("other")), Map(Entry("other")) + "," + SingleMap,
    };

    // Discriminating: if a map-parsing failure were wired to reject the whole envelope, every adapter below would report Failed
    // instead of Exited, and the token usage would be lost.
    [Theory]
    [MemberData(nameof(RejectedMaps))]
    public async Task A_malformed_or_unsupported_map_is_absent_without_failing_a_valid_result_or_losing_usage(string modelUsage)
    {
        foreach (var result in await InvokeAsync(Clean(Envelope(isError: false, ValidResult, ValidUsage, modelUsage))))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.Null(result.Limits);
            Assert.Equal(ExpectedUsage, result.Usage);
            Assert.NotNull(result.ProcessEvidence);
        }
    }

    [Fact]
    public async Task A_missing_map_is_absent_and_leaves_the_result_and_usage_intact()
    {
        foreach (var result in await InvokeAsync(Clean(Envelope(isError: false, ValidResult, ValidUsage))))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.Null(result.Limits);
            Assert.Equal(ExpectedUsage, result.Usage);
        }
    }

    [Fact]
    public async Task Missing_usage_never_discards_otherwise_admitted_model_limits()
    {
        foreach (var usage in new[] { string.Empty, "\"usage\":null", "\"usage\":{\"input_tokens\":-1}" })
        {
            var members = usage.Length == 0 ? new[] { ValidResult, SingleMap } : new[] { ValidResult, usage, SingleMap };
            foreach (var result in await InvokeAsync(Clean(Envelope(isError: false, members))))
            {
                Assert.True(result.Succeeded, result.Adapter);
                Assert.Null(result.Usage);
                var limits = Assert.IsType<AgentModelContextLimits>(result.Limits);
                Assert.Equal(new AgentModelContextLimitEntry("claude-journey-a1", 200000, 32000), Assert.Single(limits.Models));
            }
        }
    }

    [Fact]
    public async Task Reported_limits_are_never_taken_for_the_observed_model_or_substituted_by_the_requested_model()
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage, Map(Entry("claude-journey-a1", "123456", "7890")));
        var observations = await InvokeAsync(Clean(stdout));

        foreach (var result in observations)
        {
            Assert.NotNull(result.Limits);
            Assert.Null(result.ObservedModel);
            Assert.Equal(new AgentModelContextLimitEntry("claude-journey-a1", 123456, 7890), Assert.Single(result.Limits!.Models));
        }
    }

    [Fact]
    public async Task A_structurally_valid_error_envelope_still_carries_the_limits_the_provider_reported()
    {
        foreach (var result in await InvokeAsync(Clean(Envelope(isError: true, ValidResult, ValidUsage, SingleMap))))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Equal(ExpectedUsage, result.Usage);
            Assert.Equal(new AgentModelContextLimitEntry("claude-journey-a1", 200000, 32000), Assert.Single(result.Limits!.Models));
        }
    }

    [Fact]
    public async Task A_valid_envelope_whose_business_output_will_not_pass_the_schema_still_carries_the_limits()
    {
        // The adapters only seal the final response; the semantic judgement happens later and must not decide this evidence.
        var stdout = Envelope(isError: false, "\"result\":\"not the contract at all\"", SingleMap);

        foreach (var result in await InvokeAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.NotNull(result.Limits);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"result\":\"x\",\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}}")]
    [InlineData("{\"is_error\":\"false\",\"result\":\"x\",\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}}")]
    [InlineData("{\"is_error\":false,\"result\":\"x\",\"session_id\":42,\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}}")]
    [InlineData("[{\"is_error\":false,\"result\":\"x\",\"modelUsage\":{\"m\":{\"contextWindow\":200000,\"maxOutputTokens\":32000}}}]")]
    public async Task A_rejected_envelope_never_yields_limits(string stdout)
    {
        foreach (var result in await InvokeAsync(Clean(stdout)))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Limits);
        }
    }

    [Fact]
    public async Task A_truncated_stdout_capture_never_yields_limits_even_when_the_retained_text_parses()
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage, SingleMap);
        var truncated = new RecordingProcessAdapter(_ => Result(ProcessExecutionOutcome.Exited, 0, stdout) with { StandardOutputTruncated = true });

        foreach (var result in await InvokeAsync(truncated))
        {
            Assert.Null(result.Limits);
            Assert.Null(result.Usage);
        }
    }

    [Theory]
    [InlineData(ProcessExecutionOutcome.Exited, 1)]
    [InlineData(ProcessExecutionOutcome.TimedOut, null)]
    [InlineData(ProcessExecutionOutcome.Cancelled, null)]
    public async Task A_non_clean_process_result_never_yields_limits_even_when_stdout_contains_an_envelope(
        ProcessExecutionOutcome outcome, int? exitCode)
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage, SingleMap);

        foreach (var result in await InvokeAsync(new RecordingProcessAdapter(_ => Result(outcome, exitCode, stdout))))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Limits);
            Assert.NotNull(result.ProcessEvidence);
        }
    }

    [Fact]
    public async Task Every_claude_adapter_reports_no_limits_when_no_process_result_exists()
    {
        var throwing = new RecordingProcessAdapter(_ => throw new InvalidOperationException("start failure"));

        foreach (var result in await InvokeAsync(throwing))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Limits);
            Assert.Null(result.ProcessEvidence);
        }
    }

    [Fact]
    public async Task Reporting_limits_never_changes_a_single_invocation_argument_or_the_standard_input()
    {
        var plain = await InvokeAsync(Clean(Envelope(isError: false, ValidResult, ValidUsage)));
        var withLimits = await InvokeAsync(Clean(Envelope(isError: false, ValidResult, ValidUsage, SingleMap)));

        Assert.Equal(plain.Count, withLimits.Count);
        for (var index = 0; index < plain.Count; index++)
        {
            Assert.Equal(plain[index].Adapter, withLimits[index].Adapter);
            Assert.NotEmpty(withLimits[index].Arguments);
            Assert.Equal(plain[index].Arguments, withLimits[index].Arguments);
            Assert.Equal(plain[index].StandardInput, withLimits[index].StandardInput);
            Assert.Null(plain[index].Limits);
            Assert.NotNull(withLimits[index].Limits);
        }
    }

    private sealed record Observation(
        string Adapter,
        bool Succeeded,
        AgentModelContextLimits? Limits,
        AgentTokenUsage? Usage,
        AgentProcessEvidence? ProcessEvidence,
        string? ObservedModel,
        IReadOnlyList<string> Arguments,
        string StandardInput);

    private async Task<IReadOnlyList<Observation>> InvokeAsync(RecordingProcessAdapter processAdapter)
    {
        var executable = CreateLaunchFile();
        var timeout = TimeSpan.FromSeconds(30);
        var results = new List<Observation>();

        var (runId, attemptId, manifest) = await NewInvocationAsync();
        var criticalReview = await new ClaudeCriticalReviewAdapter(processAdapter, _artifactStore).InvokeAsync(
            new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(Observe("CriticalReviewer", criticalReview.Outcome == CriticalReviewInvocationOutcome.Exited,
            criticalReview.ModelContextLimits, criticalReview.TokenUsage, criticalReview.ProcessEvidence, null, processAdapter));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var implementation = await new ClaudeImplementationAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ImplementationInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(Observe("Implementer", implementation.Outcome == ImplementationInvocationOutcome.Exited,
            implementation.ModelContextLimits, implementation.TokenUsage, implementation.ProcessEvidence, implementation.ObservedModel,
            processAdapter));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var correction = await new ClaudeReviewCorrectionAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ReviewCorrectionInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(Observe("ReviewCorrection", correction.Outcome == ImplementationInvocationOutcome.Exited,
            correction.ModelContextLimits, correction.TokenUsage, correction.ProcessEvidence, null, processAdapter));

        Assert.Equal(3, results.Count);
        return results;
    }

    private static Observation Observe(
        string adapter, bool succeeded, AgentModelContextLimits? limits, AgentTokenUsage? usage, AgentProcessEvidence? process,
        string? observedModel, RecordingProcessAdapter processAdapter)
    {
        var request = processAdapter.Requests.LastOrDefault();
        // The one argument that legitimately differs between invocations is the fresh session identifier.
        var arguments = request is null ? [] : NormalizeSessionId(request.Arguments);
        return new Observation(
            adapter, succeeded, limits, usage, process, observedModel, arguments,
            request?.StandardInput is { } input ? Encoding.UTF8.GetString(input) : string.Empty);
    }

    private static IReadOnlyList<string> NormalizeSessionId(IReadOnlyList<string> arguments)
    {
        var normalized = arguments.ToList();
        var index = normalized.IndexOf("--session-id");
        if (index >= 0 && index + 1 < normalized.Count)
        {
            normalized[index + 1] = "<session>";
        }

        return normalized;
    }

    private static string Envelope(bool isError, params string[] members) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":" + (isError ? "true" : "false") + ","
        + string.Join(",", members) + "}";

    private static RecordingProcessAdapter Clean(string stdout) => new(_ => Result(ProcessExecutionOutcome.Exited, 0, stdout));

    private async Task<(Guid RunId, Guid AttemptId, SealedOutputFile Manifest)> NewInvocationAsync()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes("{\"objective\":\"bounded manifest\"}"));
        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        return (runId, attemptId, sealedFile);
    }

    private string CreateLaunchFile()
    {
        var path = Path.Combine(_workspacePath, $"fake-provider-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static ProcessExecutionResult Result(ProcessExecutionOutcome outcome, int? exitCode, string standardOutput) => new()
    {
        Outcome = outcome,
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardOutputTruncated = false,
        StandardError = string.Empty,
        StandardErrorTruncated = false,
        Duration = TimeSpan.FromMilliseconds(250),
    };

    private sealed class RecordingProcessAdapter(Func<ProcessExecutionRequest, ProcessExecutionResult> onExecute) : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(onExecute(request));
        }
    }
}
