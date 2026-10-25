using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// ADR-0065 against a REAL owned child process: the adapter produces its physical termination fact only after the actual owned process
/// instance was waited to completion successfully and its exit code was obtained (normally, or within the existing bounded wait after
/// its own timeout or cancellation termination request). It never changes the result: a timed-out or cancelled process keeps its
/// TimedOut or Cancelled outcome and its null semantic exit code. Launch faults produce no result and therefore no evidence.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseProcessTerminationTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private readonly ChildProcessExecutionAdapter adapter = new();
    private readonly string approvedRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-termination-tests-{Guid.NewGuid():N}");

    public LocalExecutionOwnershipReleaseProcessTerminationTests()
    {
        Directory.CreateDirectory(approvedRoot);
        Assert.True(File.Exists(FixtureExecutablePath), $"Expected the fixture executable at '{FixtureExecutablePath}'.");
    }

    public void Dispose()
    {
        if (Directory.Exists(approvedRoot))
        {
            Directory.Delete(approvedRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task A_normal_exit_reports_the_physical_exit_code_equal_to_the_semantic_one_and_an_elapsed_within_the_duration(int exitCode)
    {
        var result = await adapter.ExecuteAsync(Request(["exit-code", exitCode.ToString()]), CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(exitCode, result.ExitCode);
        var termination = Assert.IsType<ProcessTerminationObservation>(result.Termination);
        Assert.Equal(exitCode, termination.PhysicalExitCode);
        Assert.True(termination.ObservedElapsed >= TimeSpan.Zero);
        Assert.True(termination.ObservedElapsed <= result.Duration);
    }

    [Fact]
    public async Task A_timeout_keeps_its_outcome_and_null_exit_code_and_still_reports_the_observed_physical_termination()
    {
        var result = await adapter.ExecuteAsync(
            Request(["sleep-ms", "20000"], TimeSpan.FromMilliseconds(300)), CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.Null(result.ExitCode);
        var termination = Assert.IsType<ProcessTerminationObservation>(result.Termination);
        Assert.True(termination.ObservedElapsed >= TimeSpan.FromMilliseconds(250), $"elapsed {termination.ObservedElapsed}");
        Assert.True(termination.ObservedElapsed <= result.Duration);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_cancellation_keeps_its_outcome_and_null_exit_code_and_still_reports_the_observed_physical_termination()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await adapter.ExecuteAsync(Request(["sleep-ms", "20000"], TimeSpan.FromSeconds(30)), cancellation.Token);

        Assert.Equal(ProcessExecutionOutcome.Cancelled, result.Outcome);
        Assert.Null(result.ExitCode);
        var termination = Assert.IsType<ProcessTerminationObservation>(result.Termination);
        Assert.True(termination.ObservedElapsed <= result.Duration);
    }

    [Fact]
    public async Task A_timed_out_process_tree_proves_only_the_invoked_process_and_the_result_is_never_a_clean_exit()
    {
        var result = await adapter.ExecuteAsync(
            Request(["spawn-tree", "20000"], TimeSpan.FromMilliseconds(600)), CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.NotNull(result.Termination);
        var evidence = AgentProcessEvidence.FromProcessExecutionResult(result);
        Assert.False(evidence.IsCleanExit);
        Assert.Equal(result.Termination, evidence.Termination);
        Assert.Null(evidence.ExitCode);
    }

    [Fact]
    public async Task A_launch_fault_produces_no_result_and_so_no_physical_evidence()
    {
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = Path.Combine(approvedRoot, "missing.exe"),
            Arguments = [],
            WorkingDirectory = approvedRoot,
            ApprovedRoot = approvedRoot,
            Timeout = TimeSpan.FromSeconds(5),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public void An_observation_refuses_a_negative_elapsed_time_and_equal_observations_are_equal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessTerminationObservation(0, TimeSpan.FromTicks(-1)));
        Assert.Equal(new ProcessTerminationObservation(3, TimeSpan.FromSeconds(1)), new ProcessTerminationObservation(3, TimeSpan.FromSeconds(1)));
        Assert.NotEqual(new ProcessTerminationObservation(3, TimeSpan.FromSeconds(1)), new ProcessTerminationObservation(4, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.Zero, new ProcessTerminationObservation(0, TimeSpan.Zero).ObservedElapsed);
    }

    private ProcessExecutionRequest Request(IReadOnlyList<string> arguments, TimeSpan? timeout = null) => new()
    {
        ExecutablePath = FixtureExecutablePath,
        Arguments = arguments,
        WorkingDirectory = approvedRoot,
        ApprovedRoot = approvedRoot,
        Timeout = timeout ?? TimeSpan.FromSeconds(10),
    };
}
