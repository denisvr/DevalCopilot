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
/// Mirrors <see cref="ClaudeCriticalReviewAdapterTests"/>'s fixture/fake style exactly, focused on
/// the properties unique to <see cref="ClaudeImplementationAdapter"/>: the mutating tool
/// allowlist, <c>acceptEdits</c> permission mode, absence of <c>--max-turns</c>, and — unlike the
/// read-only critical-review adapter — that a failed or thrown invocation is never short-circuited
/// in a way that would prevent the caller from still capturing post-invocation Git evidence (that
/// behavior lives in the caller/supervisor, not this adapter, but this adapter's own contract
/// — never rolling back, never touching Git itself — is what makes that safe).
/// </summary>
public sealed class ClaudeImplementationAdapterTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public ClaudeImplementationAdapterTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-impl-adapter-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-impl-adapter-workspace-{Guid.NewGuid():N}");
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
    public async Task A_direct_executable_launch_target_produces_the_exact_contracted_argument_list()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-impl-direct.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        var arguments = fake.CapturedRequest!.Arguments.ToArray();

        var expectedSchemaJson = JsonSerializer.Serialize(ImplementationReportOutputSchema.BuildSchemaDocument());
        Assert.Equal(23, arguments.Length);
        Assert.Equal("--print", arguments[0]);
        Assert.Equal("--input-format", arguments[1]);
        Assert.Equal("text", arguments[2]);
        Assert.Equal("--output-format", arguments[3]);
        Assert.Equal("json", arguments[4]);
        Assert.Equal("--json-schema", arguments[5]);
        Assert.Equal(expectedSchemaJson, arguments[6]);
        Assert.Equal("--safe-mode", arguments[7]);
        Assert.Equal("--restricted", arguments[8]);
        Assert.Equal("--disable-slash-commands", arguments[9]);
        Assert.Equal("--no-chrome", arguments[10]);
        Assert.Equal("--permission-prompts", arguments[11]);
        Assert.Equal("none", arguments[12]);
        Assert.Equal("--prompt-suggestions", arguments[13]);
        Assert.Equal("false", arguments[14]);
        Assert.Equal("--tools", arguments[15]);
        Assert.Equal("Read,Edit,Write,Glob,Grep", arguments[16]);
        Assert.Equal("--strict-mcp-config", arguments[17]);
        Assert.Equal("--permission-mode", arguments[18]);
        Assert.Equal("acceptEdits", arguments[19]);
        Assert.Equal("--no-session-persistence", arguments[20]);
        Assert.Equal("--session-id", arguments[21]);
        Assert.True(Guid.TryParse(arguments[22], out _));

        // No --max-turns: implementation is deliberately unbounded in turn count, unlike the
        // single-turn critical-review adapter.
        Assert.DoesNotContain("--max-turns", arguments);

        Assert.Equal(executablePath, fake.CapturedRequest.ExecutablePath);
        Assert.Equal(_workspacePath, fake.CapturedRequest.WorkingDirectory);
        Assert.Equal(_workspacePath, fake.CapturedRequest.ApprovedRoot);
    }

    [Fact]
    public async Task No_shell_web_browser_mcp_or_dangerous_permission_argument_is_ever_passed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-impl-isolation.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(fake.CapturedRequest);
        var arguments = fake.CapturedRequest!.Arguments;

        string[] forbiddenArguments =
        [
            "--bare",
            "--continue", "-c",
            "--resume", "-r",
            "--fork-session",
            "--dangerously-skip-permissions",
            "--allow-dangerously-skip-permissions",
            "--mcp-config",
            "--add-dir",
            "--model",
            "--settings",
            "--plugin-dir",
            "--plugin-url",
            "--agent", "--agents",
            "--system-prompt", "--system-prompt-file", "--append-system-prompt", "--append-system-prompt-file",
            "--chrome",
            "--ide",
            "--cloud",
            "--remote-control",
            "--teleport",
            "--worktree", "-w",
            "--from-pr",
            "--file",
            "--betas",
            "--setting-sources",
            "--effort",
        ];
        foreach (var forbidden in forbiddenArguments)
        {
            Assert.DoesNotContain(forbidden, arguments);
        }

        // The tool allowlist grants only file-editing tools — Bash, WebFetch, WebSearch,
        // NotebookEdit, Task/Agent, and ExitPlanMode are all absent from it.
        var toolsIndex = Array.IndexOf(arguments.ToArray(), "--tools");
        Assert.True(toolsIndex >= 0);
        var toolsValue = arguments[toolsIndex + 1];
        foreach (var forbiddenTool in new[] { "Bash", "WebFetch", "WebSearch", "NotebookEdit", "Task", "Agent", "ExitPlanMode" })
        {
            Assert.DoesNotContain(forbiddenTool, toolsValue);
        }

        Assert.DoesNotContain("bypassPermissions", arguments);
        Assert.DoesNotContain("dontAsk", arguments);
        Assert.DoesNotContain("host", arguments);
    }

    [Fact]
    public async Task The_context_manifest_is_supplied_only_via_stdin_and_never_appears_as_a_command_line_argument()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string ManifestMarker = "MANIFEST-CONTENT-MUST-ONLY-ARRIVE-VIA-STDIN-implementation-4f21bc";
        var manifest = await SeedSealedManifestAsync(runId, attemptId, $$"""{"marker":"{{ManifestMarker}}"}""");
        var executablePath = CreateLaunchFile("fake-claude-impl-stdin.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(fake.CapturedRequest);
        Assert.All(fake.CapturedRequest!.Arguments, argument => Assert.DoesNotContain(ManifestMarker, argument));
        Assert.NotNull(fake.CapturedRequest.StandardInput);
        Assert.Equal(manifest.Text, Encoding.UTF8.GetString(fake.CapturedRequest.StandardInput!));
    }

    [Fact]
    public async Task A_launch_executable_that_no_longer_exists_at_invocation_time_fails_closed_without_re_searching()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var missingExecutablePath = Path.Combine(_workspacePath, $"does-not-exist-{Guid.NewGuid():N}.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            missingExecutablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task A_non_zero_exit_from_the_provider_produces_a_safe_closed_failed_outcome_preserving_truncation_flags()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-impl-nonzero.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardOutputTruncated = true,
                StandardError = "timed out",
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
        Assert.True(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task A_result_field_is_extracted_and_sealed_as_the_final_response_artifact()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-impl-result.exe");

        const string FinalResponseJson =
            """{"summary":"done","changedRelativePaths":["src/Foo.cs"],"implementationNotes":"added the file","unexpectedDiscoveries":"","remainingRisks":"","recommendedVerification":"run tests"}""";
        var envelope = JsonSerializer.Serialize(new { is_error = false, result = FinalResponseJson, session_id = "session-impl" });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeImplementationAdapter(fake, _artifactStore);
        var request = new ImplementationInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromMinutes(20), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-impl", result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(resultPath));
        Assert.Equal(FinalResponseJson, await File.ReadAllTextAsync(resultPath));
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

    private sealed class FakeProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? CapturedRequest { get; private set; }

        public Func<ProcessExecutionRequest, ProcessExecutionResult>? OnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            var result = OnExecute?.Invoke(request) ?? new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            };
            return Task.FromResult(result);
        }
    }
}
