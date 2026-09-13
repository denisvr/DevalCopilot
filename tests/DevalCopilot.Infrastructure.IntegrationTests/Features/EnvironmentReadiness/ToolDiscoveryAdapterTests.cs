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
/// </summary>
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
}
