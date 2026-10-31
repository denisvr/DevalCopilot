using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Exercises <see cref="ToolDiscoveryAdapter"/> with a fake <see cref="IProcessExecutionAdapter"/>
/// rather than the real <c>ChildProcessExecutionAdapter</c> against a real system tool: the
/// fixed catalog's candidate names and fallback directories are real tool identities
/// (<c>git.exe</c>, <c>dotnet.exe</c>, ...), so asserting "found" vs. "not found" through a real
/// process would depend on what happens to be installed on the machine running the tests. A
/// dummy zero-byte file on a controlled PATH entry is enough to make resolution succeed
/// deterministically, since the fake process adapter below intercepts execution before any real
/// process is ever started from that path.
///
/// <para>
/// Shares the <see cref="EnvironmentPathMutationCollection"/> collection with
/// <c>GitRepositoryInspectorTests</c>, which resolves and invokes the real <c>git.exe</c> —
/// without this, xUnit's default cross-class parallelism could let this class's temporary
/// PATH override be in effect while that one resolves git, intermittently finding this
/// class's synthetic zero-byte stub instead.
/// </para>
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class ToolDiscoveryAdapterTests : IDisposable
{
    private readonly string _pathDirectory =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-discovery-path-{Guid.NewGuid():N}");
    private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

    public ToolDiscoveryAdapterTests()
    {
        Directory.CreateDirectory(_pathDirectory);
        File.WriteAllText(Path.Combine(_pathDirectory, "git.exe"), string.Empty);
        Environment.SetEnvironmentVariable("PATH", _pathDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);

        if (Directory.Exists(_pathDirectory))
        {
            Directory.Delete(_pathDirectory, recursive: true);
        }
    }

    private sealed class FakeProcessExecutionAdapter(
        Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> execute)
        : IProcessExecutionAdapter
    {
        public IReadOnlyList<ProcessExecutionRequest> Requests => _requests;
        private readonly List<ProcessExecutionRequest> _requests = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return execute(request, cancellationToken);
        }
    }

    private static ProcessExecutionResult Exited(int exitCode, string standardOutput) => new()
    {
        Outcome = ProcessExecutionOutcome.Exited,
        ExitCode = exitCode,
        StandardOutput = standardOutput,
        StandardOutputTruncated = false,
        StandardError = string.Empty,
        StandardErrorTruncated = false,
        Duration = TimeSpan.FromMilliseconds(5),
    };

    // "Not found" is deliberately not re-tested through the full adapter here: Capability.Git's
    // fixed fallback directories are real Program Files paths, so whether resolution fails
    // depends on what happens to be installed on the machine running the tests. The resolution
    // algorithm itself (including its "nothing found" case) is fully covered, deterministically,
    // by HostExecutableResolverTests with fabricated candidate names; the three-line pass-through
    // from "resolution failed" to CapabilityProbeReason.ExecutableNotFound in DiscoverAsync is a
    // direct, easily-inspected mapping on top of that.

    [Fact]
    public async Task DiscoverAsync_returns_the_parsed_version_on_a_successful_probe()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "git version 2.43.0.windows.1\n")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal("2.43.0", result.Version);
        Assert.NotNull(result.ResolvedExecutablePath);
        Assert.EndsWith("git.exe", result.ResolvedExecutablePath);
        Assert.Equal(CapabilityLaunchKind.DirectExecutable, result.LaunchKind);
        Assert.Null(result.ResolvedScriptPath);

        // Fixed probe arguments only — never anything project- or user-influenced.
        Assert.Equal(["--version"], fake.Requests[0].Arguments);
        Assert.Empty(fake.Requests[0].EnvironmentVariables);
    }

    [Fact]
    public async Task DiscoverAsync_returns_unparseable_when_the_output_has_no_version_shape_and_never_leaks_it()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "not a version string at all")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.VersionProbeUnparseable, result.Reason);
        Assert.Null(result.Version);
        Assert.Null(result.ResolvedExecutablePath);
    }

    [Fact]
    public async Task DiscoverAsync_returns_timed_out_when_the_probe_exceeds_its_bound()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.TimedOut,
            ExitCode = null,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromSeconds(5),
        }));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.ProbeTimedOut, result.Reason);
    }

    [Fact]
    public async Task DiscoverAsync_returns_inaccessible_when_the_process_exits_non_zero()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(1, "permission denied")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.ExecutableInaccessible, result.Reason);
    }

    [Fact]
    public async Task DiscoverAsync_returns_inaccessible_when_the_process_adapter_throws()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => throw new InvalidOperationException("start failure"));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.ExecutableInaccessible, result.Reason);
    }

    [Fact]
    public async Task DiscoverAsync_throws_operation_cancelled_when_the_underlying_execution_was_cancelled()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Cancelled,
            ExitCode = null,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
        }));
        var adapter = new ToolDiscoveryAdapter(fake);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.DiscoverAsync(Capability.Git, CancellationToken.None));
    }

    [Fact]
    public async Task DiscoverAsync_reports_a_changed_version_across_two_successive_probes()
    {
        var responses = new Queue<string>(["2.43.0", "2.44.1"]);
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, $"git version {responses.Dequeue()}")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var first = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);
        var second = await adapter.DiscoverAsync(Capability.Git, CancellationToken.None);

        Assert.Equal("2.43.0", first.Version);
        Assert.Equal("2.44.1", second.Version);
    }

    // The exact ProcessExecutionRequest shape for each launch kind is deterministic and does not
    // depend on the filesystem at all, so it is verified directly against
    // ToolDiscoveryAdapter.BuildProbeRequest rather than through a real resolution — resolution
    // itself (direct .exe vs. npm-package entrypoint, including ambiguity) is fully covered by
    // HostExecutableResolverTests and PackageEntrypointResolverTests.

    [Fact]
    public void BuildProbeRequest_for_a_direct_executable_carries_no_argument_prefix()
    {
        var target = new ProviderLaunchTarget.DirectExecutable(@"C:\Program Files\Git\cmd\git.exe");

        var request = ToolDiscoveryAdapter.BuildProbeRequest(target, ["--version"], @"C:\scratch");

        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", request.ExecutablePath);
        Assert.Equal(["--version"], request.Arguments);
    }

    [Fact]
    public void BuildProbeRequest_for_a_node_script_puts_the_entrypoint_first_and_never_invokes_a_shell()
    {
        var target = new ProviderLaunchTarget.NodeScript(
            @"C:\Program Files\nodejs\node.exe", @"C:\npm\node_modules\@openai\codex\bin\codex.js");

        var request = ToolDiscoveryAdapter.BuildProbeRequest(target, ["--version"], @"C:\scratch");

        Assert.Equal(@"C:\Program Files\nodejs\node.exe", request.ExecutablePath);
        Assert.Equal([@"C:\npm\node_modules\@openai\codex\bin\codex.js", "--version"], request.Arguments);
    }

    [Fact]
    public void BuildProbeRequest_carries_exactly_the_environment_the_caller_supplies()
    {
        var target = new ProviderLaunchTarget.DirectExecutable(@"C:\Program Files\Git\cmd\git.exe");
        var environment = new Dictionary<string, string> { ["SystemRoot"] = @"C:\FabricatedOs" };

        var request = ToolDiscoveryAdapter.BuildProbeRequest(target, ["--version"], @"C:\scratch", environment);

        Assert.Same(environment, request.EnvironmentVariables);
    }

    // The Windows NodeScript environment rule is proven against ProbeResolvedAsync, the resolved-
    // target core DiscoverAsync delegates to, with fabricated targets and injected OS inputs: target
    // resolution is independent of it and already covered by the resolver tests above.

    private const string FabricatedNode = @"C:\Fabricated\nodejs\node.exe";
    private const string FabricatedScript = @"C:\Fabricated\npm\codex.js";
    private const string SensitiveMarker = "SENSITIVE-DISCOVERY-MARKER-2d7a";

    [WindowsPlatformFact]
    public async Task A_windows_node_script_probe_carries_only_the_derived_SystemRoot()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "codex-cli 0.34.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.ProbeResolvedAsync(
            Capability.CodexCli,
            new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript),
            ["--version"],
            new(true, () => @"C:\FabricatedOs\System32"),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(CapabilityLaunchKind.NodeScript, result.LaunchKind);
        var request = Assert.Single(fake.Requests);
        var entry = Assert.Single(request.EnvironmentVariables);
        Assert.Equal("SystemRoot", entry.Key);
        Assert.Equal(@"C:\FabricatedOs", entry.Value);
        Assert.Equal([FabricatedScript, "--version"], request.Arguments);
        Assert.Equal(FabricatedNode, request.ExecutablePath);
        Assert.Equal(@"C:\Fabricated\scratch", request.WorkingDirectory);
        Assert.Equal(@"C:\Fabricated\scratch", request.ApprovedRoot);
        Assert.DoesNotContain("FabricatedOs", $"{result.Version}{result.ResolvedExecutablePath}{result.ResolvedScriptPath}", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_native_or_non_windows_probe_keeps_an_empty_map_and_never_evaluates_the_reader(bool isWindows, bool isNodeScript)
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "git version 2.43.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var evaluations = 0;
        ProviderLaunchTarget target = isNodeScript
            ? new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript)
            : new ProviderLaunchTarget.DirectExecutable(@"C:\Fabricated\git.exe");

        var result = await adapter.ProbeResolvedAsync(
            isNodeScript ? Capability.CodexCli : Capability.Git,
            target,
            ["--version"],
            new(isWindows, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(0, evaluations);
        Assert.Empty(Assert.Single(fake.Requests).EnvironmentVariables);
    }

    [WindowsPlatformFact]
    public async Task A_windows_native_node_capability_probe_carries_only_the_derived_SystemRoot()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "v24.12.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);

        var result = await adapter.ProbeResolvedAsync(
            Capability.Node,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(true, () => @"C:\FabricatedOs\System32"),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(CapabilityLaunchKind.DirectExecutable, result.LaunchKind);
        Assert.Equal("24.12.0", result.Version);
        var request = Assert.Single(fake.Requests);
        var entry = Assert.Single(request.EnvironmentVariables);
        Assert.Equal("SystemRoot", entry.Key);
        Assert.Equal(@"C:\FabricatedOs", entry.Value);
        Assert.Equal(FabricatedNode, request.ExecutablePath);
        Assert.Equal(["--version"], request.Arguments);
        Assert.Equal(@"C:\Fabricated\scratch", request.WorkingDirectory);
        Assert.Equal(@"C:\Fabricated\scratch", request.ApprovedRoot);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
        Assert.Equal(4 * 1024, request.MaxBytesPerStream);
        Assert.Equal(4 * 1024, request.MaxTotalCapturedBytes);
        Assert.DoesNotContain("FabricatedOs", $"{result.Version}{result.ResolvedExecutablePath}{result.ResolvedScriptPath}", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Capability.Git)]
    [InlineData(Capability.CodexCli)]
    [InlineData(Capability.ClaudeCli)]
    [InlineData(Capability.GitHubCli)]
    [InlineData(Capability.DotNetSdk)]
    [InlineData(Capability.Docker)]
    public async Task A_native_probe_of_another_capability_keeps_an_empty_map_even_for_an_executable_named_node(
        Capability capability)
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "tool version 2.43.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var evaluations = 0;

        var result = await adapter.ProbeResolvedAsync(
            capability,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(true, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(0, evaluations);
        Assert.Empty(Assert.Single(fake.Requests).EnvironmentVariables);
    }

    // The Claude --print adapters add SystemRoot through ClaudeProcessEnvironment; native Claude version
    // discovery is a different launch contract and keeps its original empty map on Windows.
    [Fact]
    public async Task The_native_windows_claude_version_probe_keeps_an_empty_map_and_never_evaluates_the_reader()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "2.1.0 (Claude Code)\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var evaluations = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.ClaudeCli,
            new ProviderLaunchTarget.DirectExecutable(@"C:\Fabricated\claude\claude.exe"),
            ["--version"],
            new(true, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(0, evaluations);
        var request = Assert.Single(fake.Requests);
        Assert.Empty(request.EnvironmentVariables);
        Assert.Equal(["--version"], request.Arguments);
    }

    [Fact]
    public async Task A_non_windows_native_node_capability_probe_keeps_an_empty_map_and_never_evaluates_the_reader()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "v24.12.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var evaluations = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.Node,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(false, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None);

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(0, evaluations);
        Assert.Empty(Assert.Single(fake.Requests).EnvironmentVariables);
    }

    [WindowsOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Wind|ows\System32")]
    public async Task An_unusable_system_directory_refuses_a_windows_native_node_probe_before_scratch_and_dispatch(
        string? systemDirectory)
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "v24.12.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var scratchCalls = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.Node,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(true, () => systemDirectory),
            () =>
            {
                scratchCalls++;
                return @"C:\Fabricated\scratch";
            },
            CancellationToken.None);

        AssertRefused(result, fake, scratchCalls);
    }

    [WindowsPlatformFact]
    public async Task A_throwing_reader_refuses_a_windows_native_node_probe_without_leaking_its_failure()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "v24.12.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var scratchCalls = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.Node,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(true, () => throw new InvalidOperationException(SensitiveMarker)),
            () =>
            {
                scratchCalls++;
                return @"C:\Fabricated\scratch";
            },
            CancellationToken.None);

        AssertRefused(result, fake, scratchCalls);
        Assert.DoesNotContain(
            SensitiveMarker, $"{result.Reason}{result.Version}{result.ResolvedExecutablePath}{result.ResolvedScriptPath}", StringComparison.Ordinal);
    }

    [WindowsPlatformFact]
    public async Task A_cancelled_windows_native_node_probe_still_throws_operation_cancelled()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Cancelled,
            ExitCode = null,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
        }));
        var adapter = new ToolDiscoveryAdapter(fake);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ProbeResolvedAsync(
            Capability.Node,
            new ProviderLaunchTarget.DirectExecutable(FabricatedNode),
            ["--version"],
            new(true, () => @"C:\FabricatedOs\System32"),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None));
    }

    [WindowsOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Wind|ows\System32")]
    public async Task An_unusable_system_directory_refuses_a_windows_node_script_before_scratch_and_dispatch(string? systemDirectory)
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "codex-cli 0.34.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var scratchCalls = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.CodexCli,
            new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript),
            ["--version"],
            new(true, () => systemDirectory),
            () =>
            {
                scratchCalls++;
                return @"C:\Fabricated\scratch";
            },
            CancellationToken.None);

        AssertRefused(result, fake, scratchCalls);
    }

    [WindowsPlatformFact]
    public async Task A_control_character_system_directory_refuses_a_windows_node_script()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "codex-cli 0.34.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var scratchCalls = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.CodexCli,
            new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript),
            ["--version"],
            new(true, () => @"C:\Win" + (char)0 + @"dows\System32"),
            () =>
            {
                scratchCalls++;
                return @"C:\Fabricated\scratch";
            },
            CancellationToken.None);

        AssertRefused(result, fake, scratchCalls);
    }

    [WindowsPlatformFact]
    public async Task A_throwing_reader_refuses_a_windows_node_script_without_leaking_its_failure()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(Exited(0, "codex-cli 0.34.0\n")));
        var adapter = new ToolDiscoveryAdapter(fake);
        var scratchCalls = 0;

        var result = await adapter.ProbeResolvedAsync(
            Capability.CodexCli,
            new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript),
            ["--version"],
            new(true, () => throw new InvalidOperationException(SensitiveMarker)),
            () =>
            {
                scratchCalls++;
                return @"C:\Fabricated\scratch";
            },
            CancellationToken.None);

        AssertRefused(result, fake, scratchCalls);
        Assert.DoesNotContain(
            SensitiveMarker, $"{result.Reason}{result.Version}{result.ResolvedExecutablePath}{result.ResolvedScriptPath}", StringComparison.Ordinal);
    }

    [WindowsPlatformFact]
    public async Task A_cancelled_windows_node_script_probe_still_throws_operation_cancelled()
    {
        var fake = new FakeProcessExecutionAdapter((_, _) => Task.FromResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Cancelled,
            ExitCode = null,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
        }));
        var adapter = new ToolDiscoveryAdapter(fake);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ProbeResolvedAsync(
            Capability.CodexCli,
            new ProviderLaunchTarget.NodeScript(FabricatedNode, FabricatedScript),
            ["--version"],
            new(true, () => @"C:\FabricatedOs\System32"),
            () => @"C:\Fabricated\scratch",
            CancellationToken.None));
    }

    private static void AssertRefused(ToolDiscoveryResult result, FakeProcessExecutionAdapter fake, int scratchCalls)
    {
        Assert.Equal(CapabilityProbeReason.ExecutableInaccessible, result.Reason);
        Assert.Null(result.Version);
        Assert.Null(result.ResolvedExecutablePath);
        Assert.Null(result.ResolvedScriptPath);
        Assert.Empty(fake.Requests);
        Assert.Equal(0, scratchCalls);
    }
}
