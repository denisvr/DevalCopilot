using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Proves every Agent adapter preserves host-measured child-process evidence instead of collapsing
/// it into its closed Exited/Failed classification. Real-process cases run the real
/// <see cref="ChildProcessExecutionAdapter"/> against the deterministic <c>ProcessExecutionFixture</c>
/// executable (never a provider CLI); contract cases use a scripted process adapter to cover
/// shapes a real fixture cannot produce deterministically. No real provider or model is invoked.
/// </summary>
public sealed class AgentProcessEvidenceAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-evidence-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-evidence-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public AgentProcessEvidenceAdapterTests()
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
    public async Task A_real_timed_out_codex_process_reports_timed_out_evidence_with_a_host_measured_duration()
    {
        var result = await InvokeCodexPlanningWithRealProcessAsync("sleep-ms 30000", TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.ProcessEvidence);
        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.ProcessEvidence.Outcome);
        Assert.Null(result.ProcessEvidence.ExitCode);
        Assert.True(result.ProcessEvidence.Duration >= TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task A_real_cancelled_codex_process_reports_cancelled_evidence_rather_than_throwing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));

        var result = await InvokeCodexPlanningWithRealProcessAsync("sleep-ms 30000", TimeSpan.FromSeconds(30), cancellation.Token);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.ProcessEvidence);
        Assert.Equal(ProcessExecutionOutcome.Cancelled, result.ProcessEvidence.Outcome);
        Assert.Null(result.ProcessEvidence.ExitCode);
        Assert.True(result.ProcessEvidence.Duration > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0, CodexPlanningInvocationOutcome.Exited)]
    [InlineData(3, CodexPlanningInvocationOutcome.Failed)]
    public async Task A_real_exited_codex_process_reports_its_exact_exit_code(int exitCode, CodexPlanningInvocationOutcome expectedOutcome)
    {
        var result = await InvokeCodexPlanningWithRealProcessAsync($"exit-code {exitCode}", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.NotNull(result.ProcessEvidence);
        Assert.Equal(ProcessExecutionOutcome.Exited, result.ProcessEvidence.Outcome);
        Assert.Equal(exitCode, result.ProcessEvidence.ExitCode);
        Assert.True(result.ProcessEvidence.Duration > TimeSpan.Zero);
        Assert.Equal(exitCode == 0, result.ProcessEvidence.IsCleanExit);
    }

    [Fact]
    public async Task A_real_claude_shaped_invocation_that_exits_non_zero_reports_its_exit_code()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId);
        var adapter = new ClaudeCriticalReviewAdapter(new ChildProcessExecutionAdapter(), _artifactStore);

        // The fixture treats Claude's fixed first flag as an unknown mode and exits 64.
        var result = await adapter.InvokeAsync(
            new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                FixtureExecutablePath, TimeSpan.FromSeconds(30), 65536, 131072),
            CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Equal(ProcessExecutionOutcome.Exited, result.ProcessEvidence?.Outcome);
        Assert.Equal(64, result.ProcessEvidence?.ExitCode);
    }

    public static TheoryData<ProcessExecutionOutcome, int?> NonCleanResults => new()
    {
        { ProcessExecutionOutcome.Exited, 2 },
        { ProcessExecutionOutcome.TimedOut, null },
        { ProcessExecutionOutcome.Cancelled, null },
    };

    [Theory]
    [MemberData(nameof(NonCleanResults))]
    public async Task Every_adapter_preserves_a_non_clean_process_result_as_evidence(ProcessExecutionOutcome outcome, int? exitCode)
    {
        var expected = new AgentProcessEvidence(outcome, exitCode, TimeSpan.FromMilliseconds(1234));
        var fake = new ScriptedProcessExecutionAdapter(_ => Result(outcome, exitCode, TimeSpan.FromMilliseconds(1234), string.Empty));

        foreach (var evidence in await InvokeEveryAdapterAsync(fake))
        {
            Assert.Equal(expected, evidence.Evidence);
            Assert.False(evidence.Succeeded);
        }
    }

    [Fact]
    public async Task Every_adapter_reports_clean_exit_evidence_for_a_zero_exit_even_when_its_own_output_is_rejected()
    {
        // A zero exit with an unparseable Claude envelope is still a provider failure, but the
        // host-measured process fact remains a clean exit — the two are never conflated.
        var fake = new ScriptedProcessExecutionAdapter(_ => Result(ProcessExecutionOutcome.Exited, 0, TimeSpan.FromMilliseconds(77), "not json"));

        foreach (var evidence in await InvokeEveryAdapterAsync(fake))
        {
            Assert.Equal(new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 0, TimeSpan.FromMilliseconds(77)), evidence.Evidence);
        }
    }

    [Fact]
    public async Task Every_adapter_reports_no_evidence_when_no_process_result_exists()
    {
        var throwing = new ScriptedProcessExecutionAdapter(_ => throw new InvalidOperationException("start failure"));

        foreach (var evidence in await InvokeEveryAdapterAsync(throwing))
        {
            Assert.Null(evidence.Evidence);
            Assert.False(evidence.Succeeded);
        }

        var neverCalled = new ScriptedProcessExecutionAdapter(_ => throw new InvalidOperationException("must not start"));
        foreach (var evidence in await InvokeEveryAdapterAsync(neverCalled, launchExecutablePath: Path.Combine(_workspacePath, "missing.exe")))
        {
            Assert.Null(evidence.Evidence);
            Assert.False(evidence.Succeeded);
        }

        Assert.Equal(0, neverCalled.CallCount);
    }

    private async Task<CodexPlanningInvocationResult> InvokeCodexPlanningWithRealProcessAsync(
        string fixtureScript, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId);
        var scriptPath = Path.Combine(_workspacePath, $"{Guid.NewGuid():N}.fixture-script");
        await File.WriteAllTextAsync(scriptPath, fixtureScript, cancellationToken);
        var adapter = new CodexPlanningAdapter(new ChildProcessExecutionAdapter(), _artifactStore);

        return await adapter.InvokeAsync(
            new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                FixtureExecutablePath, scriptPath, timeout, 65536, 131072),
            cancellationToken);
    }

    private async Task<IReadOnlyList<(string Adapter, bool Succeeded, AgentProcessEvidence? Evidence)>> InvokeEveryAdapterAsync(
        IProcessExecutionAdapter processAdapter, string? launchExecutablePath = null)
    {
        var executable = launchExecutablePath ?? CreateLaunchFile();
        var timeout = TimeSpan.FromSeconds(30);
        var results = new List<(string, bool, AgentProcessEvidence?)>();

        var (runId, attemptId, manifest) = await NewInvocationAsync();
        var planning = await new CodexPlanningAdapter(processAdapter, _artifactStore).InvokeAsync(
            new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("Planner", planning.Outcome == CodexPlanningInvocationOutcome.Exited, planning.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var resolution = await new CodexChallengeResolutionAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ChallengeResolutionInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("Resolver", resolution.Outcome == ChallengeResolutionInvocationOutcome.Exited, resolution.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var codeReview = await new CodexImplementationReviewAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ImplementationReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, null, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("CodeReviewer", codeReview.Outcome == ImplementationReviewInvocationOutcome.Exited, codeReview.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var criticalReview = await new ClaudeCriticalReviewAdapter(processAdapter, _artifactStore).InvokeAsync(
            new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("CriticalReviewer", criticalReview.Outcome == CriticalReviewInvocationOutcome.Exited, criticalReview.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var implementation = await new ClaudeImplementationAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ImplementationInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("Implementer", implementation.Outcome == ImplementationInvocationOutcome.Exited, implementation.ProcessEvidence));

        (runId, attemptId, manifest) = await NewInvocationAsync();
        var correction = await new ClaudeReviewCorrectionAdapter(processAdapter, _artifactStore).InvokeAsync(
            new ReviewCorrectionInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                executable, timeout, 65536, 131072),
            CancellationToken.None);
        results.Add(("ReviewCorrection", correction.Outcome == ImplementationInvocationOutcome.Exited, correction.ProcessEvidence));

        Assert.Equal(6, results.Count);
        return results;
    }

    private async Task<(Guid RunId, Guid AttemptId, SealedOutputFile Manifest)> NewInvocationAsync()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        return (runId, attemptId, await SeedSealedManifestAsync(runId, attemptId));
    }

    private string CreateLaunchFile()
    {
        var path = Path.Combine(_workspacePath, $"fake-provider-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private async Task<SealedOutputFile> SeedSealedManifestAsync(Guid runId, Guid attemptId)
    {
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes("bounded manifest"));
        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        return sealedFile;
    }

    private static ProcessExecutionResult Result(ProcessExecutionOutcome outcome, int? exitCode, TimeSpan duration, string standardOutput) => new()
    {
        Outcome = outcome,
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardOutputTruncated = false,
        StandardError = string.Empty,
        StandardErrorTruncated = false,
        Duration = duration,
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
