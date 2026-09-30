using System.Globalization;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The optional agentic-turn-limit request on the two mutating Claude adapters: exactly one discrete
/// <c>--max-turns N</c> pair appended after the model and effort arguments, an unchanged argument list
/// when no limit is requested, and a fail-closed outcome (before any process starts, with no retry
/// without the flag) for any invalid or version-incoherent request. The read-only critical-review
/// adapter keeps its own fixed single-turn value.
/// </summary>
public sealed class ClaudeMutationTurnLimitAdapterTests : IDisposable
{
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-turn-limit-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-turn-limit-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public ClaudeMutationTurnLimitAdapterTests()
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

    public enum MutationPath
    {
        Implementation,
        ReviewCorrection,
    }

    private sealed record Invocation(
        bool Failed, IReadOnlyList<string> Arguments, int Starts, bool HasProcessEvidence, ProcessExecutionRequest? Request);

    private static string V1(MutationPath path) => path == MutationPath.Implementation
        ? ClaudeMutationAdapterContract.ImplementationV1
        : ClaudeMutationAdapterContract.ReviewCorrectionV1;

    private static string V2(MutationPath path) => path == MutationPath.Implementation
        ? ClaudeMutationAdapterContract.ImplementationV2
        : ClaudeMutationAdapterContract.ReviewCorrectionV2;

    private static string OtherPathV2(MutationPath path) => path == MutationPath.Implementation
        ? ClaudeMutationAdapterContract.ReviewCorrectionV2
        : ClaudeMutationAdapterContract.ImplementationV2;

    private async Task<Invocation> InvokeAsync(
        MutationPath path,
        int? maxTurns,
        string? version,
        string? model = null,
        string? effort = null,
        Func<ProcessExecutionRequest, ProcessExecutionResult>? onExecute = null,
        bool throwOnExecute = false)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = Path.Combine(_workspacePath, $"fake-claude-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(executablePath, string.Empty);
        var fake = new CountingProcessExecutionAdapter { OnExecute = onExecute, ThrowOnExecute = throwOnExecute };

        bool failed;
        bool hasEvidence;
        if (path == MutationPath.Implementation)
        {
            var result = await new ClaudeImplementationAdapter(fake, _artifactStore).InvokeAsync(
                new ImplementationInvocationRequest(
                    runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                    executablePath, TimeSpan.FromMinutes(20), 65536, 131072, model, effort, maxTurns, version),
                CancellationToken.None);
            failed = result.Outcome == ImplementationInvocationOutcome.Failed;
            hasEvidence = result.ProcessEvidence is not null;
        }
        else
        {
            var result = await new ClaudeReviewCorrectionAdapter(fake, _artifactStore).InvokeAsync(
                new ReviewCorrectionInvocationRequest(
                    runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                    executablePath, TimeSpan.FromMinutes(20), 65536, 131072, model, effort, maxTurns, version),
                CancellationToken.None);
            failed = result.Outcome == ImplementationInvocationOutcome.Failed;
            hasEvidence = result.ProcessEvidence is not null;
        }

        return new Invocation(
            failed, fake.Request?.Arguments.ToArray() ?? [], fake.Starts, hasEvidence, fake.Request);
    }

    private static int SessionIdValueIndex(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().IndexOf("--session-id");
        Assert.True(index >= 0);
        return index + 1;
    }

    private static void AssertEqualExceptSessionId(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        var sessionIdValue = SessionIdValueIndex(expected);
        for (var index = 0; index < expected.Count; index++)
        {
            if (index == sessionIdValue)
            {
                Assert.True(Guid.TryParse(actual[index], out _));
                continue;
            }

            Assert.Equal(expected[index], actual[index]);
        }
    }

    [Theory]
    [InlineData(MutationPath.Implementation, 1)]
    [InlineData(MutationPath.Implementation, 12)]
    [InlineData(MutationPath.Implementation, 100)]
    [InlineData(MutationPath.ReviewCorrection, 1)]
    [InlineData(MutationPath.ReviewCorrection, 12)]
    [InlineData(MutationPath.ReviewCorrection, 100)]
    public async Task A_requested_limit_appends_exactly_one_max_turns_pair_after_the_model_and_effort_arguments(
        MutationPath path, int limit)
    {
        var baseline = await InvokeAsync(path, null, V2(path), "opus", "high");
        var requested = await InvokeAsync(path, limit, V2(path), "opus", "high");

        Assert.False(requested.Failed);
        Assert.Equal(1, requested.Starts);
        Assert.Equal(1, requested.Arguments.Count(argument => argument == "--max-turns"));
        var index = requested.Arguments.ToList().IndexOf("--max-turns");
        Assert.Equal(limit.ToString(CultureInfo.InvariantCulture), requested.Arguments[index + 1]);
        Assert.Equal(["--model", "opus", "--effort", "high", "--max-turns", limit.ToString(CultureInfo.InvariantCulture)], requested.Arguments.TakeLast(6));
        Assert.Equal(baseline.Arguments.Count + 2, requested.Arguments.Count);
        AssertEqualExceptSessionId(baseline.Arguments, requested.Arguments.Take(baseline.Arguments.Count).ToArray());
    }

    [Theory]
    [InlineData(MutationPath.Implementation)]
    [InlineData(MutationPath.ReviewCorrection)]
    public async Task A_requested_limit_leaves_the_fixed_contract_arguments_unchanged(MutationPath path)
    {
        var baseline = await InvokeAsync(path, null, V2(path));
        var requested = await InvokeAsync(path, 25, V2(path));

        Assert.Equal(baseline.Arguments.Count + 2, requested.Arguments.Count);
        AssertEqualExceptSessionId(baseline.Arguments, requested.Arguments.Take(baseline.Arguments.Count).ToArray());
        Assert.Equal(["--max-turns", "25"], requested.Arguments.TakeLast(2));
        Assert.DoesNotContain("--model", requested.Arguments);
        Assert.DoesNotContain("--effort", requested.Arguments);
        var arguments = requested.Arguments;
        Assert.Equal("Read,Edit,Write,Glob,Grep", arguments[arguments.ToList().IndexOf("--tools") + 1]);
        Assert.Equal("acceptEdits", arguments[arguments.ToList().IndexOf("--permission-mode") + 1]);
        Assert.Contains("--no-session-persistence", arguments);
        Assert.Contains("--json-schema", arguments);
        Assert.Equal(Encoding.UTF8.GetBytes("manifest content"), requested.Request!.StandardInput);
        Assert.Equal(_workspacePath, requested.Request.WorkingDirectory);
        Assert.Equal(_workspacePath, requested.Request.ApprovedRoot);
    }

    [Theory]
    [InlineData(MutationPath.Implementation, "tr-TR")]
    [InlineData(MutationPath.Implementation, "ar-SA")]
    [InlineData(MutationPath.Implementation, "fa-IR")]
    [InlineData(MutationPath.ReviewCorrection, "tr-TR")]
    [InlineData(MutationPath.ReviewCorrection, "ar-SA")]
    [InlineData(MutationPath.ReviewCorrection, "fa-IR")]
    public async Task The_number_is_formatted_with_invariant_ascii_digits_under_a_non_invariant_culture(
        MutationPath path, string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            foreach (var limit in new[] { 7, 100 })
            {
                var result = await InvokeAsync(path, limit, V2(path));

                Assert.False(result.Failed);
                var index = result.Arguments.ToList().IndexOf("--max-turns");
                Assert.Equal(limit.ToString(CultureInfo.InvariantCulture), result.Arguments[index + 1]);
                Assert.All(result.Arguments[index + 1], character => Assert.InRange(character, '0', '9'));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Theory]
    [InlineData(MutationPath.Implementation, false)]
    [InlineData(MutationPath.Implementation, true)]
    [InlineData(MutationPath.ReviewCorrection, false)]
    [InlineData(MutationPath.ReviewCorrection, true)]
    public async Task A_null_request_keeps_the_exact_historical_argument_list_for_both_contract_versions(
        MutationPath path, bool v2)
    {
        var version = v2 ? V2(path) : V1(path);

        var result = await InvokeAsync(path, null, version);

        Assert.False(result.Failed);
        Assert.Equal(1, result.Starts);
        Assert.DoesNotContain("--max-turns", result.Arguments);
        var schemaJson = path == MutationPath.Implementation
            ? JsonSerializer.Serialize(ImplementationReportOutputSchema.BuildSchemaDocument())
            : JsonSerializer.Serialize(ReviewCorrectionOutputSchema.BuildSchemaDocument());
        var arguments = result.Arguments;
        Assert.Equal(23, arguments.Count);
        Assert.Equal(
            [
                "--print", "--input-format", "text", "--output-format", "json",
                "--json-schema", schemaJson,
                "--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome",
                "--permission-prompts", "none", "--prompt-suggestions", "false",
                "--tools", "Read,Edit,Write,Glob,Grep", "--strict-mcp-config",
                "--permission-mode", "acceptEdits", "--no-session-persistence",
                "--session-id",
            ],
            arguments.Take(22));
        Assert.True(Guid.TryParse(arguments[22], out _));
    }

    [Theory]
    [InlineData(MutationPath.Implementation)]
    [InlineData(MutationPath.ReviewCorrection)]
    public async Task A_null_request_with_no_recorded_version_keeps_the_historical_argument_list(MutationPath path)
    {
        var baseline = await InvokeAsync(path, null, V1(path));

        var unversioned = await InvokeAsync(path, null, null);

        Assert.False(unversioned.Failed);
        Assert.DoesNotContain("--max-turns", unversioned.Arguments);
        AssertEqualExceptSessionId(baseline.Arguments, unversioned.Arguments);
    }

    [Theory]
    [InlineData(MutationPath.Implementation, 0)]
    [InlineData(MutationPath.Implementation, -1)]
    [InlineData(MutationPath.Implementation, 101)]
    [InlineData(MutationPath.Implementation, int.MinValue)]
    [InlineData(MutationPath.Implementation, int.MaxValue)]
    [InlineData(MutationPath.ReviewCorrection, 0)]
    [InlineData(MutationPath.ReviewCorrection, -1)]
    [InlineData(MutationPath.ReviewCorrection, 101)]
    [InlineData(MutationPath.ReviewCorrection, int.MinValue)]
    [InlineData(MutationPath.ReviewCorrection, int.MaxValue)]
    public async Task An_out_of_range_request_fails_closed_before_any_process_starts(MutationPath path, int limit)
    {
        var result = await InvokeAsync(path, limit, V2(path));

        Assert.True(result.Failed);
        Assert.Equal(0, result.Starts);
        Assert.Null(result.Request);
    }

    [Theory]
    [InlineData(MutationPath.Implementation)]
    [InlineData(MutationPath.ReviewCorrection)]
    public async Task A_version_incoherent_request_fails_closed_before_any_process_starts(MutationPath path)
    {
        var incoherentVersions = new string?[]
        {
            V1(path),
            path == MutationPath.Implementation ? "claude-implementation-v3" : "claude-review-correction-v3",
            V2(path).ToUpperInvariant(),
            V2(path) + " ",
            null,
            string.Empty,
            OtherPathV2(path),
            "claude-critical-review-v1",
        };

        foreach (var version in incoherentVersions)
        {
            var result = await InvokeAsync(path, 10, version);

            Assert.True(result.Failed, $"version '{version}' must fail closed");
            Assert.Equal(0, result.Starts);
            Assert.Null(result.Request);
        }
    }

    [Theory]
    [InlineData(MutationPath.Implementation)]
    [InlineData(MutationPath.ReviewCorrection)]
    public async Task A_provider_non_zero_exit_is_an_ordinary_failure_with_preserved_evidence_and_no_retry_without_the_flag(
        MutationPath path)
    {
        var result = await InvokeAsync(
            path,
            3,
            V2(path),
            onExecute: _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = "error: --max-turns reached or unsupported",
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            });

        Assert.True(result.Failed);
        Assert.True(result.HasProcessEvidence);
        Assert.Equal(1, result.Starts);
        Assert.Equal(["--max-turns", "3"], result.Arguments.TakeLast(2));
    }

    [Theory]
    [InlineData(MutationPath.Implementation)]
    [InlineData(MutationPath.ReviewCorrection)]
    public async Task A_process_start_failure_is_an_ordinary_failure_with_no_retry_without_the_flag(MutationPath path)
    {
        var result = await InvokeAsync(path, 3, V2(path), throwOnExecute: true);

        Assert.True(result.Failed);
        Assert.Equal(1, result.Starts);
        Assert.Equal(["--max-turns", "3"], result.Arguments.TakeLast(2));
    }

    [Fact]
    public async Task The_read_only_critical_review_adapter_still_passes_exactly_one_fixed_single_turn_limit()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = Path.Combine(_workspacePath, $"fake-claude-critical-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(executablePath, string.Empty);

        foreach (var (model, effort) in new (string?, string?)[] { (null, null), ("opus", "high") })
        {
            var fake = new CountingProcessExecutionAdapter();
            await new ClaudeCriticalReviewAdapter(fake, _artifactStore).InvokeAsync(
                new CriticalReviewInvocationRequest(
                    runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                    executablePath, TimeSpan.FromMinutes(10), 65536, 131072, model, effort),
                CancellationToken.None);

            var arguments = fake.Request!.Arguments.ToList();
            Assert.Equal(1, arguments.Count(argument => argument == "--max-turns"));
            Assert.Equal("1", arguments[arguments.IndexOf("--max-turns") + 1]);
            Assert.DoesNotContain(arguments, argument => argument != "--max-turns" && argument.StartsWith("--max-turn", StringComparison.Ordinal));
        }
    }

    private async Task<SeededManifest> SeedSealedManifestAsync(Guid runId, Guid attemptId, string text)
    {
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes(text));

        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        return new SeededManifest(sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash);
    }

    private sealed record SeededManifest(string RelativePath, long ByteLength, string ContentHash);

    private sealed class CountingProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? Request { get; private set; }

        public int Starts { get; private set; }

        public Func<ProcessExecutionRequest, ProcessExecutionResult>? OnExecute { get; set; }

        public bool ThrowOnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Starts++;
            if (ThrowOnExecute)
            {
                throw new InvalidOperationException("Simulated process start failure.");
            }

            return Task.FromResult(OnExecute?.Invoke(request) ?? new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            });
        }
    }
}
