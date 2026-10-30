using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <see cref="CodexVerificationDiagnosisAdapter"/>'s own contract construction through the
/// shared <see cref="CodexProcessInvoker"/> with a fake process adapter: argument shape, the
/// Windows NodeScript <c>SystemRoot</c> rule, and closed outcome mapping. The shared machinery
/// itself is covered by <see cref="CodexPlanningAdapterTests"/> and
/// <see cref="CodexProcessInvokerEnvironmentTests"/>; no real provider is ever launched.
/// </summary>
public sealed class CodexVerificationDiagnosisAdapterTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public CodexVerificationDiagnosisAdapterTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-adapter-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-adapter-workspace-{Guid.NewGuid():N}");
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
        var executablePath = CreateLaunchFile("fake-codex-direct.exe");
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexVerificationDiagnosisAdapter(fake, _artifactStore);
        var request = new VerificationDiagnosisInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, LaunchScriptPath: null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(VerificationDiagnosisInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        string[] expectedArguments =
        [
            "exec",
            "--json",
            "--output-schema", Path.Combine(scratchDirectory, "schema.json"),
            "--output-last-message", _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse),
            "--sandbox", "read-only",
            "--cd", _workspacePath,
            "--ephemeral",
            "--ignore-user-config",
            "-",
        ];
        Assert.Equal(expectedArguments, fake.CapturedRequest!.Arguments.ToArray());
        Assert.Equal(executablePath, fake.CapturedRequest.ExecutablePath);
        Assert.DoesNotContain("SystemRoot", fake.CapturedRequest.EnvironmentVariables.Keys);
    }

    [WindowsPlatformFact]
    public async Task A_windows_node_script_launch_adds_only_SystemRoot_derived_from_the_system_directory()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var nodeExecutablePath = CreateLaunchFile("fake-node-systemroot.exe");
        var scriptPath = CreateLaunchFile("fake-codex-cli-systemroot.js");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexVerificationDiagnosisAdapter(fake, _artifactStore);
        var request = new VerificationDiagnosisInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            nodeExecutablePath, scriptPath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(VerificationDiagnosisInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        Assert.Equal(scriptPath, fake.CapturedRequest!.Arguments[0]);
        Assert.Equal(nodeExecutablePath, fake.CapturedRequest.ExecutablePath);
        var expectedSystemRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Environment.SystemDirectory));
        Assert.Equal(expectedSystemRoot, fake.CapturedRequest.EnvironmentVariables["SystemRoot"]);
        Assert.DoesNotContain("PATH", fake.CapturedRequest.EnvironmentVariables.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("NODE_OPTIONS", fake.CapturedRequest.EnvironmentVariables.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_non_zero_exit_is_a_closed_failure_that_still_carries_the_measured_process_evidence()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-failing.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 3,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexVerificationDiagnosisAdapter(fake, _artifactStore);
        var request = new VerificationDiagnosisInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(VerificationDiagnosisInvocationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.ProcessEvidence);
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

        return new SeededManifest(sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash);
    }

    private sealed record SeededManifest(string RelativePath, long ByteLength, string ContentHash);

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
