using System.Text;
using System.Text.Json;
using System.Diagnostics;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

public sealed class ClaudeReviewCorrectionAdapterTests : IDisposable
{
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-correction-adapter-{Guid.NewGuid():N}");
    private readonly string workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-correction-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore artifactStore;

    public ClaudeReviewCorrectionAdapterTests()
    {
        artifactStore = new FilesystemArtifactStore(artifactRoot);
        Directory.CreateDirectory(workspacePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        if (Directory.Exists(workspacePath)) Directory.Delete(workspacePath, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_uses_the_fixed_mutation_contract_and_allowlisted_environment()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"bounded manifest\"}");
        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeReviewCorrectionAdapter(fake, artifactStore);

        var result = await adapter.InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.Request);
        var request = fake.Request!;
        Assert.Equal(23, request.Arguments.Count);
        // These fixed flags are exactly the arguments GetReviewCorrectionAttemptStatusQueryHandler
        // discloses as configured facts (permission prompts, built-in tools, permission mode, and
        // provider-session persistence/resume eligibility) for a coherent current ReviewCorrection
        // assignment — this test is their sole source of truth that the disclosed literals still
        // match what the adapter actually passes.
        Assert.Equal("--permission-prompts", request.Arguments[11]);
        Assert.Equal("none", request.Arguments[12]);
        Assert.Equal("--tools", request.Arguments[15]);
        Assert.Equal("Read,Edit,Write,Glob,Grep", request.Arguments[16]);
        Assert.Equal("--permission-mode", request.Arguments[18]);
        Assert.Equal("acceptEdits", request.Arguments[19]);
        Assert.Equal("--no-session-persistence", request.Arguments[20]);
        Assert.DoesNotContain("--max-turns", request.Arguments);
        Assert.Equal(manifest.Text, Encoding.UTF8.GetString(request.StandardInput!));
        Assert.DoesNotContain("PATH", request.EnvironmentVariables.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.All(request.EnvironmentVariables.Keys, key => Assert.Contains(key, new[] { "USERPROFILE", "CLAUDE_CONFIG_DIR" }));
    }

    [Fact]
    public async Task InvokeAsync_seals_the_final_response_and_accepts_absent_or_null_session_ids()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest\"}");
        var fake = new FakeProcessExecutionAdapter
        {
            Output = JsonSerializer.Serialize(new { is_error = false, result = "{\"ok\":true}", session_id = (string?)null }),
        };
        var adapter = new ClaudeReviewCorrectionAdapter(fake, artifactStore);

        var result = await adapter.InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var finalPath = artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(finalPath));
        Assert.Equal("{\"ok\":true}", await File.ReadAllTextAsync(finalPath));
    }

    [Fact]
    public async Task InvokeAsync_rejects_blank_or_oversized_session_ids()
    {
        foreach (var sessionId in new[] { " ", new string('x', 257) })
        {
            var runId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest\"}");
            var fake = new FakeProcessExecutionAdapter
            {
                Output = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = sessionId }),
            };

            var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore)
                .InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);

            Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
            Assert.False(File.Exists(artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse)));
        }
    }

    [Fact]
    public async Task InvokeAsync_rejects_a_non_string_session_id()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest\"}");
        var fake = new FakeProcessExecutionAdapter
        {
            Output = "{\"is_error\":false,\"result\":\"{}\",\"session_id\":1}",
        };

        var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore)
            .InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse)));
    }

    [Fact]
    public async Task InvokeAsync_preserves_nonzero_exit_and_cancellation_as_failures()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest\"}");
        var fake = new FakeProcessExecutionAdapter
        {
            Result = new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 1,
                StandardOutput = "{}",
                StandardOutputTruncated = false,
                StandardError = "failure",
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            },
        };
        var adapter = new ClaudeReviewCorrectionAdapter(fake, artifactStore);
        var result = await adapter.InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);
        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);

        fake.ThrowCancellation = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            adapter.InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None));
    }

    [WindowsOnlyFact]
    public async Task InvokeAsync_rejects_a_reparse_point_ancestor_of_the_launch_path_without_process_or_artifact()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest\"}");
        var target = Path.Combine(workspacePath, "real-launch-target");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "must remain untouched");
        await File.WriteAllTextAsync(Path.Combine(target, "claude.exe"), string.Empty);
        var junction = Path.Combine(workspacePath, "launch-junction");
        var junctionCreation = TryCreateJunction(junction, target);
        try
        {
            Assert.True(junctionCreation.Succeeded, $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");
            var fake = new FakeProcessExecutionAdapter();
            var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore)
                .InvokeAsync(CreateRequest(runId, attemptId, manifest) with { LaunchExecutablePath = Path.Combine(junction, "claude.exe") }, CancellationToken.None);

            Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
            Assert.Null(fake.Request);
            Assert.Equal("must remain untouched", await File.ReadAllTextAsync(sentinel));
            Assert.False(File.Exists(artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse)));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    private async Task<IReadOnlyList<string>> CaptureArgumentsAsync(string? requestedClaudeModel, string? requestedClaudeEffort = null)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest content\"}");
        var fake = new FakeProcessExecutionAdapter();
        var request = CreateRequest(runId, attemptId, manifest) with { RequestedClaudeModel = requestedClaudeModel, RequestedClaudeEffort = requestedClaudeEffort };

        var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore).InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        return fake.Request!.Arguments.ToArray();
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("haiku")]
    public async Task A_requested_alias_adds_only_the_model_argument_and_leaves_every_other_argument_unchanged(string alias)
    {
        var baseline = await CaptureArgumentsAsync(null);
        var requested = await CaptureArgumentsAsync(alias);

        Assert.DoesNotContain("--model", baseline);
        Assert.Equal(baseline.Count + 2, requested.Count);
        Assert.Equal(["--model", alias], requested.Skip(baseline.Count));
        var sessionIdValue = Array.IndexOf(baseline.ToArray(), "--session-id") + 1;
        for (var index = 0; index < baseline.Count; index++)
        {
            if (index != sessionIdValue)
            {
                Assert.Equal(baseline[index], requested[index]);
            }
        }

        Assert.DoesNotContain("--effort", requested);
        Assert.Contains("Read,Edit,Write,Glob,Grep", requested);
        Assert.Contains("acceptEdits", requested);
    }

    [Theory]
    [InlineData("Opus")]
    [InlineData("fable")]
    [InlineData("claude-opus-5-5")]
    [InlineData("opus --dangerously-skip-permissions")]
    [InlineData("")]
    public async Task A_model_request_outside_the_closed_alias_set_fails_closed_without_starting_a_process(string malformed)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest content\"}");
        var fake = new FakeProcessExecutionAdapter();
        var request = CreateRequest(runId, attemptId, manifest) with { RequestedClaudeModel = malformed };

        var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore).InvokeAsync(request, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.Request);
    }

    [Theory]
    [InlineData("sonnet", "low")]
    [InlineData("sonnet", "medium")]
    [InlineData("opus", "high")]
    public async Task A_requested_pair_appends_exactly_discrete_model_then_effort_arguments(string alias, string effort)
    {
        var baseline = await CaptureArgumentsAsync(null);
        var modelOnly = await CaptureArgumentsAsync(alias);
        var requested = await CaptureArgumentsAsync(alias, effort);

        Assert.DoesNotContain("--effort", baseline);
        Assert.DoesNotContain("--effort", modelOnly);
        Assert.Equal(baseline.Count + 4, requested.Count);
        Assert.Equal(["--model", alias, "--effort", effort], requested.Skip(baseline.Count));
        var sessionIdValue = Array.IndexOf(baseline.ToArray(), "--session-id") + 1;
        for (var index = 0; index < baseline.Count; index++)
        {
            if (index != sessionIdValue)
            {
                Assert.Equal(baseline[index], requested[index]);
            }
        }
    }

    [Theory]
    [InlineData(null, "low")]
    [InlineData("haiku", "low")]
    [InlineData("sonnet", "Low")]
    [InlineData("sonnet", "HIGH")]
    [InlineData("opus", "max")]
    [InlineData("opus", "")]
    [InlineData("opus", "high --dangerously-skip-permissions")]
    public async Task An_invalid_model_effort_snapshot_fails_closed_without_starting_a_process(string? model, string effort)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(runId, attemptId, "{\"objective\":\"manifest content\"}");
        var fake = new FakeProcessExecutionAdapter();
        var manifestRequest = CreateRequest(runId, attemptId, manifest) with { RequestedClaudeModel = model, RequestedClaudeEffort = effort };

        var result = await new ClaudeReviewCorrectionAdapter(fake, artifactStore).InvokeAsync(manifestRequest, CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.Request);
    }

    private ReviewCorrectionInvocationRequest CreateRequest(Guid runId, Guid attemptId, SeededManifest manifest) =>
        new(runId, attemptId, workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            CreateLaunchFile(), TimeSpan.FromMinutes(5), 65536, 131072);

    private string CreateLaunchFile()
    {
        var path = Path.Combine(workspacePath, $"claude-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private async Task<SeededManifest> SeedManifestAsync(Guid runId, Guid attemptId, string text)
    {
        var partialPath = artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, text);
        var sealedFile = await artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        return new SeededManifest(sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, text);
    }

    private static JunctionCreationResult TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);
            using var process = Process.Start(startInfo)!;
            process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new JunctionCreationResult(process.ExitCode == 0 && Directory.Exists(junctionPath), process.ExitCode, standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JunctionCreationResult(false, -1, exception.GetType().Name);
        }
    }

    private readonly record struct JunctionCreationResult(bool Succeeded, int ExitCode, string StandardError);

    private sealed record SeededManifest(string RelativePath, long ByteLength, string ContentHash, string Text);

    private sealed class FakeProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? Request { get; private set; }
        public string Output { get; set; } = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null });
        public ProcessExecutionResult? Result { get; set; }
        public bool ThrowCancellation { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            if (ThrowCancellation) throw new OperationCanceledException();
            return Task.FromResult(Result ?? new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = Output,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            });
        }
    }
}
