using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Proves the three Claude adapters read provider-reported token usage from the proven
/// <c>--output-format json</c> envelope contract, fail closed to "no usage" for every malformed
/// <c>usage</c> shape without rejecting an otherwise valid business result, and that the three
/// Codex adapters never report usage at all — Codex has no proven usage contract. Every case uses
/// a scripted process adapter with deterministic stdout; no real provider or model is invoked.
/// </summary>
public sealed class AgentTokenUsageAdapterTests : IDisposable
{
    private const string ValidResult = "\"result\":\"{\\\"summary\\\":\\\"ok\\\"}\"";
    private const string ValidUsage =
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}";

    private static readonly AgentTokenUsage ExpectedUsage = new(1200, 345, 67, 890, "claude-cli-usage-v1");

    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-usage-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-usage-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public AgentTokenUsageAdapterTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_workspacePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task Every_claude_adapter_reports_the_envelope_usage_alongside_a_successful_result()
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage, "\"session_id\":null", "\"total_cost_usd\":0.5",
            "\"modelUsage\":{\"some-model\":{\"inputTokens\":999}}");

        foreach (var result in await InvokeClaudeAdaptersAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.Equal(ExpectedUsage, result.Usage);
            Assert.NotNull(result.ProcessEvidence);
        }
    }

    [Fact]
    public async Task Every_claude_adapter_records_the_default_zero_usage_literal_as_reported_zeros()
    {
        var stdout = Envelope(isError: false, ValidResult,
            "\"usage\":{\"input_tokens\":0,\"output_tokens\":0,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}");

        foreach (var result in await InvokeClaudeAdaptersAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.Equal(new AgentTokenUsage(0, 0, 0, 0, "claude-cli-usage-v1"), result.Usage);
        }
    }

    public static TheoryData<string> MalformedUsage => new()
    {
        // Missing entirely.
        string.Empty,
        // Wrong JSON types for the object itself.
        "\"usage\":null",
        "\"usage\":[]",
        "\"usage\":\"1200\"",
        // A missing member.
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67}",
        // A negative member.
        "\"usage\":{\"input_tokens\":-1,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        // A non-integer member.
        "\"usage\":{\"input_tokens\":1200.5,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        // A string member.
        "\"usage\":{\"input_tokens\":\"1200\",\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        // A null member.
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":null,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        // Outside the int range.
        "\"usage\":{\"input_tokens\":3000000000,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        // Duplicate required members are ambiguous in either order; never accept the last value.
        "\"usage\":{\"input_tokens\":1200,\"input_tokens\":1,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        "\"usage\":{\"input_tokens\":1,\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"output_tokens\":1,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890}",
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_creation_input_tokens\":1,\"cache_read_input_tokens\":890}",
        "\"usage\":{\"input_tokens\":1200,\"output_tokens\":345,\"cache_creation_input_tokens\":67,\"cache_read_input_tokens\":890,\"cache_read_input_tokens\":1}",
        // Duplicate root usage objects are equally ambiguous.
        ValidUsage + ",\"usage\":{\"input_tokens\":1,\"output_tokens\":1,\"cache_creation_input_tokens\":1,\"cache_read_input_tokens\":1}",
        "\"usage\":{\"input_tokens\":1,\"output_tokens\":1,\"cache_creation_input_tokens\":1,\"cache_read_input_tokens\":1}," + ValidUsage,
    };

    // Discriminating: if a usage-parsing failure were wired to reject the whole envelope, every
    // adapter below would report Failed instead of Exited.
    [Theory]
    [MemberData(nameof(MalformedUsage))]
    public async Task A_malformed_or_missing_usage_omits_usage_without_failing_an_otherwise_valid_result(string usageMember)
    {
        var stdout = usageMember.Length == 0 ? Envelope(isError: false, ValidResult) : Envelope(isError: false, ValidResult, usageMember);

        foreach (var result in await InvokeClaudeAdaptersAsync(Clean(stdout)))
        {
            Assert.True(result.Succeeded, result.Adapter);
            Assert.Null(result.Usage);
        }
    }

    [Fact]
    public async Task A_structurally_valid_error_envelope_still_carries_the_usage_the_provider_reported()
    {
        var stdout = Envelope(isError: true, ValidResult, ValidUsage);

        foreach (var result in await InvokeClaudeAdaptersAsync(Clean(stdout)))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Equal(ExpectedUsage, result.Usage);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"result\":\"x\"," + ValidUsage + "}")]
    [InlineData("{\"is_error\":\"false\",\"result\":\"x\"," + ValidUsage + "}")]
    [InlineData("{\"is_error\":false,\"result\":\"x\",\"session_id\":42," + ValidUsage + "}")]
    public async Task A_rejected_envelope_never_yields_usage(string stdout)
    {
        foreach (var result in await InvokeClaudeAdaptersAsync(Clean(stdout)))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Usage);
        }
    }

    [Fact]
    public async Task A_truncated_stdout_capture_never_yields_usage_even_when_the_retained_text_parses()
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage);
        var truncated = new ScriptedProcessExecutionAdapter(_ => Result(ProcessExecutionOutcome.Exited, 0, stdout) with { StandardOutputTruncated = true });

        foreach (var result in await InvokeClaudeAdaptersAsync(truncated))
        {
            Assert.Null(result.Usage);
        }
    }

    [Theory]
    [InlineData(ProcessExecutionOutcome.Exited, 1)]
    [InlineData(ProcessExecutionOutcome.TimedOut, null)]
    [InlineData(ProcessExecutionOutcome.Cancelled, null)]
    public async Task A_non_clean_process_result_never_yields_usage_even_when_stdout_contains_an_envelope(
        ProcessExecutionOutcome outcome, int? exitCode)
    {
        var stdout = Envelope(isError: false, ValidResult, ValidUsage);
        var fake = new ScriptedProcessExecutionAdapter(_ => Result(outcome, exitCode, stdout));

        foreach (var result in await InvokeClaudeAdaptersAsync(fake))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Usage);
            Assert.NotNull(result.ProcessEvidence);
        }
    }

    // Codex has no proven token-usage contract in this environment, so even stdout that contains
    // a Claude-shaped usage object, or a third-party-described Codex token_count event, never
    // becomes usage evidence for any Codex-produced attempt.
    [Theory]
    [InlineData("{\"is_error\":false," + ValidResult + "," + ValidUsage + "}")]
    [InlineData("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"cached_input_tokens\":2,\"output_tokens\":3}}")]
    [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":10,\"output_tokens\":3}}}}")]
    public async Task Every_codex_adapter_always_reports_unknown_usage(string stdout)
    {
        var fake = Clean(stdout);
        var executable = CreateLaunchFile();
        var timeout = TimeSpan.FromSeconds(30);

        var (runId, attemptId, manifest) = await NewInvocationAsync();
        var planning = await new CodexPlanningAdapter(fake, _artifactStore).InvokeAsync(
            new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var resolution = await new CodexChallengeResolutionAdapter(fake, _artifactStore).InvokeAsync(
            new ChallengeResolutionInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var codeReview = await new CodexImplementationReviewAdapter(fake, _artifactStore).InvokeAsync(
            new ImplementationReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);

        Assert.Equal(3, fake.CallCount);
        Assert.NotNull(planning.ProcessEvidence);
        Assert.Null(planning.TokenUsage);
        Assert.NotNull(resolution.ProcessEvidence);
        Assert.Null(resolution.TokenUsage);
        Assert.NotNull(codeReview.ProcessEvidence);
        Assert.Null(codeReview.TokenUsage);
    }

    [Fact]
    public async Task Every_claude_adapter_reports_no_usage_when_no_process_result_exists()
    {
        var throwing = new ScriptedProcessExecutionAdapter(_ => throw new InvalidOperationException("start failure"));

        foreach (var result in await InvokeClaudeAdaptersAsync(throwing))
        {
            Assert.False(result.Succeeded, result.Adapter);
            Assert.Null(result.Usage);
            Assert.Null(result.ProcessEvidence);
        }
    }

    private async Task<IReadOnlyList<(string Adapter, bool Succeeded, AgentTokenUsage? Usage, AgentProcessEvidence? ProcessEvidence)>>
        InvokeClaudeAdaptersAsync(IProcessExecutionAdapter processAdapter)
    {
        var executable = CreateLaunchFile();
        var timeout = TimeSpan.FromSeconds(30);
        var results = new List<(string, bool, AgentTokenUsage?, AgentProcessEvidence?)>();

        var (runId, attemptId, manifest) = await NewInvocationAsync();
        var criticalReview = await new ClaudeCriticalReviewAdapter(processAdapter, _artifactStore).InvokeAsync(
            new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("CriticalReviewer", criticalReview.Outcome == CriticalReviewInvocationOutcome.Exited,
            criticalReview.TokenUsage, criticalReview.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var implementation = await new ClaudeImplementationAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ImplementationInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("Implementer", implementation.Outcome == ImplementationInvocationOutcome.Exited,
            implementation.TokenUsage, implementation.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var correction = await new ClaudeReviewCorrectionAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ReviewCorrectionInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("ReviewCorrection", correction.Outcome == ImplementationInvocationOutcome.Exited,
            correction.TokenUsage, correction.ProcessEvidence));

        Assert.Equal(3, results.Count);
        return results;
    }

    private static string Envelope(bool isError, params string[] members) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":" + (isError ? "true" : "false") + ","
        + string.Join(",", members) + "}";

    private static ScriptedProcessExecutionAdapter Clean(string stdout) =>
        new(_ => Result(ProcessExecutionOutcome.Exited, 0, stdout));

    private async Task<(Guid RunId, Guid AttemptId, SealedOutputFile Manifest)> NewInvocationAsync()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes("bounded manifest"));
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

    private sealed class ScriptedProcessExecutionAdapter(Func<ProcessExecutionRequest, ProcessExecutionResult> onExecute)
        : IProcessExecutionAdapter
    {
        public int CallCount { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(onExecute(request));
        }
    }
}
