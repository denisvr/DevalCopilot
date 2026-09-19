using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <see cref="CodexImplementationReviewAdapter"/>'s own contract construction — CLI
/// argument shape (including the implementation-review output schema written to disk),
/// stdin-only manifest delivery, and closed outcome mapping. The shared bounded invocation
/// machinery itself (reparse-point rejection, launch-target revalidation, environment
/// allowlisting, scratch-directory cleanup) is <see cref="CodexProcessInvoker"/>, already fully
/// exercised by <see cref="CodexPlanningAdapterTests"/> and <see cref="CodexChallengeResolutionAdapterTests"/>
/// against the identical code path — this suite does not re-duplicate that coverage, only what is
/// genuinely specific to this adapter: which schema it asks Codex to constrain its response to,
/// and that it never exposes a shell, PATH lookup, inherited environment, repository-mutation
/// capability, prompt, command line, or local path through public state.
/// </summary>
public sealed class CodexImplementationReviewAdapterTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public CodexImplementationReviewAdapterTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-review-adapter-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-review-adapter-workspace-{Guid.NewGuid():N}");
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
    public async Task A_direct_executable_launch_target_produces_the_exact_contracted_argument_list_and_writes_the_implementation_review_schema()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-direct.exe");
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);

        // The schema file only exists on disk for the lifetime of the invocation — the adapter's
        // scratch directory is cleaned up as soon as InvokeAsync returns — so its content is
        // captured from inside the fake's own ExecuteAsync, before that cleanup runs.
        string? capturedSchemaContent = null;
        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ =>
            {
                var schemaPath = Path.Combine(scratchDirectory, "schema.json");
                capturedSchemaContent = File.Exists(schemaPath) ? File.ReadAllText(schemaPath) : null;
                return new ProcessExecutionResult
                {
                    Outcome = ProcessExecutionOutcome.Exited,
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardOutputTruncated = false,
                    StandardError = string.Empty,
                    StandardErrorTruncated = false,
                    Duration = TimeSpan.Zero,
                };
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, LaunchScriptPath: null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        var expectedSchemaPath = Path.Combine(scratchDirectory, "schema.json");
        var expectedResultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        string[] expectedArguments =
        [
            "exec",
            "--json",
            "--output-schema", expectedSchemaPath,
            "--output-last-message", expectedResultPath,
            "--sandbox", "read-only",
            "--cd", _workspacePath,
            "--ephemeral",
            "--ignore-user-config",
            "-",
        ];
        Assert.Equal(expectedArguments, fake.CapturedRequest!.Arguments.ToArray());
        Assert.Equal(executablePath, fake.CapturedRequest.ExecutablePath);
        Assert.Equal(_workspacePath, fake.CapturedRequest.WorkingDirectory);
        Assert.Equal(_workspacePath, fake.CapturedRequest.ApprovedRoot);

        // The written schema file is genuinely ImplementationReviewOutputSchema — proven by its
        // distinguishing "outcome"/"findings" shape, never asserted merely by trusting the
        // adapter's own claim, and never the sibling adapters' own proposal/decision schemas.
        Assert.NotNull(capturedSchemaContent);
        using var schemaDocument = JsonDocument.Parse(capturedSchemaContent!);
        Assert.True(schemaDocument.RootElement.GetProperty("properties").TryGetProperty("outcome", out _));
        Assert.True(schemaDocument.RootElement.GetProperty("properties").TryGetProperty("findings", out _));
    }

    [Fact]
    public async Task A_node_script_launch_target_prepends_the_script_path_and_keeps_the_same_flag_contract()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var nodeExecutablePath = CreateLaunchFile("fake-node.exe");
        var scriptPath = CreateLaunchFile("fake-codex-cli.js");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            nodeExecutablePath, scriptPath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        Assert.Equal(scriptPath, fake.CapturedRequest!.Arguments.First());
        Assert.Equal(nodeExecutablePath, fake.CapturedRequest.ExecutablePath);
    }

    [Fact]
    public async Task The_context_manifest_is_supplied_only_via_stdin_and_never_appears_as_a_command_line_argument()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string ManifestMarker = "MANIFEST-CONTENT-MUST-ONLY-ARRIVE-VIA-STDIN-7b21f3";
        var manifest = await SeedSealedManifestAsync(runId, attemptId, $$"""{"marker":"{{ManifestMarker}}"}""");
        var executablePath = CreateLaunchFile("fake-codex-stdin.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(fake.CapturedRequest);
        Assert.All(fake.CapturedRequest!.Arguments, argument => Assert.DoesNotContain(ManifestMarker, argument));
        Assert.NotNull(fake.CapturedRequest.StandardInput);
        Assert.Equal(manifest.Text, Encoding.UTF8.GetString(fake.CapturedRequest.StandardInput!));

        // No shell command string (arguments are discrete literal values passed to the OS
        // process-creation API), and the working directory is bounded to the workspace itself —
        // the same shape every sibling adapter uses. The environment allowlist itself is owned
        // by the shared CodexProcessInvoker and already proven bounded by the sibling adapters'
        // own tests; this suite proves only that this adapter's request is scoped the same way.
        Assert.Equal(_workspacePath, fake.CapturedRequest.ApprovedRoot);
        Assert.Equal(_workspacePath, fake.CapturedRequest.WorkingDirectory);
    }

    [Fact]
    public async Task A_launch_executable_that_no_longer_exists_at_invocation_time_fails_closed_without_invoking_a_process()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var missingExecutablePath = Path.Combine(_workspacePath, $"does-not-exist-{Guid.NewGuid():N}.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            missingExecutablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task A_non_zero_exit_from_the_provider_produces_a_safe_closed_failed_outcome_preserving_truncation_flags()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-authfail.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardOutputTruncated = true,
                StandardError = "not authenticated",
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Failed, result.Outcome);
        Assert.True(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task A_timeout_produces_a_safe_closed_failed_outcome()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-timeout.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.TimedOut,
                ExitCode = null,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(30),
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_cancelled_invocation_produces_a_safe_closed_failed_outcome()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-cancelled.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Cancelled,
                ExitCode = null,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_thrown_execution_exception_becomes_a_safe_closed_failed_outcome_never_the_exceptions_own_text()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-throws.exe");

        const string SensitiveDetail = "sensitive-failure-detail-must-not-leak-91cd4a";
        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new InvalidOperationException(SensitiveDetail) };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Failed, result.Outcome);
        foreach (var property in typeof(ImplementationReviewInvocationResult).GetProperties())
        {
            if (property.GetValue(result) is string value)
            {
                Assert.DoesNotContain(SensitiveDetail, value);
            }
        }
    }

    [Fact]
    public async Task A_session_id_reported_on_a_json_stdout_line_is_surfaced_on_a_successful_exit()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-session.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = """{"type":"session_meta","session_id":"session-def-456"}""" + "\n",
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationReviewInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-def-456", result.ProviderSessionId);
    }

    [Fact]
    public async Task Standard_output_and_error_never_appear_on_the_public_invocation_result()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-output.exe");

        const string StdoutMarker = "STDOUT-MUST-NEVER-REACH-PUBLIC-STATE-33aa21";
        const string StderrMarker = "STDERR-MUST-NEVER-REACH-PUBLIC-STATE-44bb32";
        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = StdoutMarker,
                StandardOutputTruncated = false,
                StandardError = StderrMarker,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexImplementationReviewAdapter(fake, _artifactStore);
        var request = new ImplementationReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        // Nothing on the public result type ever carries the raw text — only a closed outcome,
        // truncation flags, and an optional provider session id. Where the bounded invocation
        // machinery actually seals this output is CodexProcessInvoker's own concern, already
        // proven by the sibling planning/challenge-resolution adapter suites; this test proves
        // only that this adapter's own public contract never leaks it.
        foreach (var property in typeof(ImplementationReviewInvocationResult).GetProperties())
        {
            if (property.GetValue(result) is string value)
            {
                Assert.DoesNotContain(StdoutMarker, value);
                Assert.DoesNotContain(StderrMarker, value);
            }
        }
    }

    private string CreateLaunchFile(string fileName)
    {
        var path = Path.Combine(_workspacePath, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private async Task<SeededManifest> SeedSealedManifestAsync(Guid runId, Guid attemptId, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, bytes);

        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);

        return new SeededManifest(sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, text);
    }

    private sealed record SeededManifest(string RelativePath, long ByteLength, string ContentHash, string Text);

    /// <summary>Mirrors <c>CodexChallengeResolutionAdapterTests</c>'s identically named fake
    /// exactly — this test suite has no mocking framework dependency.</summary>
    private sealed class FakeProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? CapturedRequest { get; private set; }

        public Func<ProcessExecutionRequest, ProcessExecutionResult>? OnExecute { get; set; }

        public Exception? ThrowOnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            if (ThrowOnExecute is { } exception)
            {
                throw exception;
            }

            var result = OnExecute?.Invoke(request) ?? new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            };
            return Task.FromResult(result);
        }
    }
}
