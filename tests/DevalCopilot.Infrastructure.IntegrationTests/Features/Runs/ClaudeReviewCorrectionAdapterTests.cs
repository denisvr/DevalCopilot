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
        var manifest = await SeedManifestAsync(runId, attemptId, "bounded manifest");
        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeReviewCorrectionAdapter(fake, artifactStore);

        var result = await adapter.InvokeAsync(CreateRequest(runId, attemptId, manifest), CancellationToken.None);

        Assert.Equal(ImplementationInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.Request);
        var request = fake.Request!;
        Assert.Equal(23, request.Arguments.Count);
        Assert.Equal("--tools", request.Arguments[15]);
        Assert.Equal("Read,Edit,Write,Glob,Grep", request.Arguments[16]);
        Assert.Equal("--permission-mode", request.Arguments[18]);
        Assert.Equal("acceptEdits", request.Arguments[19]);
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
        var manifest = await SeedManifestAsync(runId, attemptId, "manifest");
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
            var manifest = await SeedManifestAsync(runId, attemptId, "manifest");
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
        var manifest = await SeedManifestAsync(runId, attemptId, "manifest");
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
        var manifest = await SeedManifestAsync(runId, attemptId, "manifest");
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
        var manifest = await SeedManifestAsync(runId, attemptId, "manifest");
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
