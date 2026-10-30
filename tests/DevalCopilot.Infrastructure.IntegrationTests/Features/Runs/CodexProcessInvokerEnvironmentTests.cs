using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises the environment construction of the one shared <see cref="CodexProcessInvoker"/>
/// through its injectable-input overload, so the Windows NodeScript rule, the unchanged
/// USERPROFILE/CODEX_HOME inclusion semantics and the refuse-before-dispatch control flow are
/// proven with fabricated profile values and system directories — no ambient variable is read or
/// mutated, no real provider or process is started, and no actual profile location is inspected.
/// </summary>
public sealed class CodexProcessInvokerEnvironmentTests : IDisposable
{
    private const string SensitiveMarker = "SENSITIVE-ENVIRONMENT-MARKER-91be";
    private const string FabricatedProfile = @"C:\fabricated-profile-for-tests";
    private const string FabricatedCodexHome = @"C:\fabricated-codex-home-for-tests";

    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public CodexProcessInvokerEnvironmentTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-codex-env-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-codex-env-workspace-{Guid.NewGuid():N}");
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

    [WindowsPlatformFact]
    public async Task A_windows_node_script_gets_SystemRoot_plus_exactly_the_admitted_profile_entries_unchanged()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();

        var outcome = await CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request, Inputs(FabricatedProfile, FabricatedCodexHome, @"C:\FabricatedOs\System32"),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var environment = Assert.Single(fake.Requests).EnvironmentVariables;
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["USERPROFILE"] = FabricatedProfile,
                ["CODEX_HOME"] = FabricatedCodexHome,
                ["SystemRoot"] = @"C:\FabricatedOs",
            },
            environment.ToDictionary(pair => pair.Key, pair => pair.Value));
        Assert.Equal(invocation.ScriptPath, fake.Requests[0].Arguments[0]);
        Assert.DoesNotContain(FabricatedProfile, outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(FabricatedCodexHome, outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(@"FabricatedOs", outcome.ToString(), StringComparison.Ordinal);
    }

    [WindowsPlatformFact]
    public async Task Profile_inclusion_semantics_are_unchanged_null_and_empty_omitted_whitespace_kept_verbatim()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();

        await CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request, Inputs("  ", string.Empty, @"C:\FabricatedOs\System32"), CancellationToken.None);

        var environment = Assert.Single(fake.Requests).EnvironmentVariables;
        Assert.Equal(2, environment.Count);
        Assert.Equal("  ", environment["USERPROFILE"]);
        Assert.Equal(@"C:\FabricatedOs", environment["SystemRoot"]);

        var secondInvocation = await PrepareAsync(nodeScript: true);
        var secondFake = new FakeProcessExecutionAdapter();

        await CodexProcessInvoker.InvokeAsync(
            secondFake, _artifactStore, secondInvocation.Request, Inputs(null, null, @"C:\FabricatedOs\System32"), CancellationToken.None);

        var onlySystemRoot = Assert.Single(secondFake.Requests).EnvironmentVariables;
        var entry = Assert.Single(onlySystemRoot);
        Assert.Equal("SystemRoot", entry.Key);
    }

    [Fact]
    public async Task A_native_launch_on_windows_keeps_its_original_map_and_never_evaluates_the_reader()
    {
        var invocation = await PrepareAsync(nodeScript: false);
        var fake = new FakeProcessExecutionAdapter();
        var evaluations = 0;
        var inputs = new CodexProcessInvoker.EnvironmentInputs(
            Variables(FabricatedProfile, FabricatedCodexHome),
            new(true, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }));

        var outcome = await CodexProcessInvoker.InvokeAsync(fake, _artifactStore, invocation.Request, inputs, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, evaluations);
        var environment = Assert.Single(fake.Requests).EnvironmentVariables;
        Assert.Equal(
            new Dictionary<string, string> { ["USERPROFILE"] = FabricatedProfile, ["CODEX_HOME"] = FabricatedCodexHome },
            environment.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    [Fact]
    public async Task A_node_script_launch_off_windows_keeps_its_original_map_and_never_evaluates_the_reader()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();
        var evaluations = 0;
        var inputs = new CodexProcessInvoker.EnvironmentInputs(
            Variables(FabricatedProfile, null),
            new(false, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }));

        var outcome = await CodexProcessInvoker.InvokeAsync(fake, _artifactStore, invocation.Request, inputs, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, evaluations);
        var entry = Assert.Single(Assert.Single(fake.Requests).EnvironmentVariables);
        Assert.Equal("USERPROFILE", entry.Key);
        Assert.Equal(FabricatedProfile, entry.Value);
    }

    [WindowsOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Windows\.\System32")]
    [InlineData(@"C:\Wind|ows\System32")]
    public async Task An_unusable_system_directory_refuses_before_dispatch_and_creates_no_invocation_artifact(string? systemDirectory)
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();

        var outcome = await CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request, Inputs(FabricatedProfile, FabricatedCodexHome, systemDirectory),
            CancellationToken.None);

        AssertClosedFailureWithNothingCreated(outcome, fake, invocation);
    }

    [WindowsPlatformFact]
    public async Task A_control_character_system_directory_refuses_before_dispatch()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();

        var outcome = await CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request,
            Inputs(FabricatedProfile, FabricatedCodexHome, @"C:\Win" + (char)0 + @"dows\System32"), CancellationToken.None);

        AssertClosedFailureWithNothingCreated(outcome, fake, invocation);
    }

    [WindowsPlatformFact]
    public async Task A_throwing_reader_refuses_before_dispatch_without_leaking_its_failure()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();
        var inputs = new CodexProcessInvoker.EnvironmentInputs(
            Variables(FabricatedProfile, FabricatedCodexHome),
            new(true, () => throw new InvalidOperationException(SensitiveMarker)));

        var outcome = await CodexProcessInvoker.InvokeAsync(fake, _artifactStore, invocation.Request, inputs, CancellationToken.None);

        AssertClosedFailureWithNothingCreated(outcome, fake, invocation);
        Assert.DoesNotContain(SensitiveMarker, outcome.ToString(), StringComparison.Ordinal);
    }

    [WindowsPlatformFact]
    public async Task Cancellation_of_a_windows_node_script_invocation_still_propagates()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request, Inputs(FabricatedProfile, null, @"C:\FabricatedOs\System32"),
            CancellationToken.None));
    }

    [WindowsPlatformFact]
    public async Task The_launch_contract_is_otherwise_unchanged_for_a_windows_node_script()
    {
        var invocation = await PrepareAsync(nodeScript: true);
        var fake = new FakeProcessExecutionAdapter();

        await CodexProcessInvoker.InvokeAsync(
            fake, _artifactStore, invocation.Request, Inputs(FabricatedProfile, null, @"C:\FabricatedOs\System32"),
            CancellationToken.None);

        var executionRequest = Assert.Single(fake.Requests);
        Assert.Equal(invocation.NodePath, executionRequest.ExecutablePath);
        Assert.Equal(_workspacePath, executionRequest.WorkingDirectory);
        Assert.Equal(_workspacePath, executionRequest.ApprovedRoot);
        Assert.Equal(TimeSpan.FromSeconds(30), executionRequest.Timeout);
        Assert.Equal(65536, executionRequest.MaxBytesPerStream);
        Assert.Equal(131072, executionRequest.MaxTotalCapturedBytes);
        Assert.Equal("manifest content", Encoding.UTF8.GetString(executionRequest.StandardInput!));
        Assert.Equal(
            [invocation.ScriptPath!, "exec", "--json", "--output-schema"],
            executionRequest.Arguments.Take(4).ToArray());
        Assert.Equal("-", executionRequest.Arguments[^1]);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", executionRequest.Arguments);
    }

    private static CodexProcessInvoker.EnvironmentInputs Inputs(string? userProfile, string? codexHome, string? systemDirectory) =>
        new(Variables(userProfile, codexHome), new(true, () => systemDirectory));

    private static Func<string, string?> Variables(string? userProfile, string? codexHome) =>
        name => name switch
        {
            "USERPROFILE" => userProfile,
            "CODEX_HOME" => codexHome,
            _ => throw new InvalidOperationException("Only the existing allowlisted variables may ever be read."),
        };

    private void AssertClosedFailureWithNothingCreated(
        CodexProcessInvoker.Outcome outcome, FakeProcessExecutionAdapter fake, PreparedInvocation invocation)
    {
        Assert.Equal(CodexProcessInvoker.Outcome.Failed, outcome);
        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.ProcessEvidence);
        Assert.Empty(fake.Requests);
        Assert.False(Directory.Exists(invocation.ScratchDirectory));
        foreach (var purpose in new[] { ArtifactPurpose.AgentFinalResponse, ArtifactPurpose.AgentStandardOutput, ArtifactPurpose.AgentStandardError })
        {
            Assert.False(File.Exists(_artifactStore.GetPartialPath(invocation.RunId, invocation.AttemptId, purpose)));
        }
    }

    private async Task<PreparedInvocation> PrepareAsync(bool nodeScript)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        // A sealed manifest and valid launch files are seeded for every case, so a closed failure
        // can only be explained by the environment rule, never by an earlier gate.
        const string ManifestText = "manifest content";
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes(ManifestText));
        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);

        var nodePath = Path.Combine(_workspacePath, "fake-node.exe");
        File.WriteAllText(nodePath, string.Empty);
        string? scriptPath = null;
        if (nodeScript)
        {
            scriptPath = Path.Combine(_workspacePath, "fake-codex-cli.js");
            File.WriteAllText(scriptPath, string.Empty);
        }

        // Discovers the scratch location without leaving it behind, so the test can later prove the
        // invocation never created it.
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);
        AgentInvocationScratchDirectory.TryDelete(runId, attemptId);
        Assert.False(Directory.Exists(scratchDirectory));

        var request = new CodexProcessInvoker.Request(
            runId, attemptId, _workspacePath, sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash,
            nodePath, scriptPath, TimeSpan.FromSeconds(30), 65536, 131072, new { type = "object" });

        return new PreparedInvocation(request, runId, attemptId, nodePath, scriptPath, scratchDirectory);
    }

    private sealed record PreparedInvocation(
        CodexProcessInvoker.Request Request, Guid RunId, Guid AttemptId, string NodePath, string? ScriptPath, string ScratchDirectory);

    private sealed class FakeProcessExecutionAdapter : IProcessExecutionAdapter
    {
        private readonly List<ProcessExecutionRequest> _requests = [];

        public IReadOnlyList<ProcessExecutionRequest> Requests => _requests;

        public Exception? ThrowOnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            if (ThrowOnExecute is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            });
        }
    }
}
