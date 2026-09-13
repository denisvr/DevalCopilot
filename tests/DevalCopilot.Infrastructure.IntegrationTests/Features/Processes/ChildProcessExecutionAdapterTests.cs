using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// Exercises <see cref="ChildProcessExecutionAdapter"/> against a real child process — the
/// deterministic <c>ProcessExecutionFixture</c> test double — rather than a fake, because the
/// behavior under test (no-shell argument passing, process-tree termination, pipe capture and
/// truncation) only exists at the real OS process boundary.
/// </summary>
public sealed class ChildProcessExecutionAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private readonly ChildProcessExecutionAdapter _adapter = new();
    private readonly string _approvedRoot;

    public ChildProcessExecutionAdapterTests()
    {
        _approvedRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-process-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_approvedRoot);

        Assert.True(File.Exists(FixtureExecutablePath), $"Expected the fixture executable at '{FixtureExecutablePath}'.");
    }

    [Fact]
    public async Task Successful_exit_reports_the_exited_outcome_and_zero_exit_code()
    {
        var request = CreateRequest(["exit-code", "0"]);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Non_zero_exit_reports_the_exited_outcome_and_the_real_exit_code()
    {
        var request = CreateRequest(["exit-code", "7"]);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task A_process_that_outlives_its_timeout_is_killed_and_reports_timed_out()
    {
        var request = CreateRequest(["sleep-ms", "20000"], timeout: TimeSpan.FromMilliseconds(200));

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Cancelling_the_caller_token_kills_the_process_and_reports_cancelled()
    {
        var request = CreateRequest(["sleep-ms", "20000"], timeout: TimeSpan.FromSeconds(30));
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await _adapter.ExecuteAsync(request, cancellationSource.Token);

        Assert.Equal(ProcessExecutionOutcome.Cancelled, result.Outcome);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task A_timed_out_process_tree_is_fully_terminated_including_its_child()
    {
        var request = CreateRequest(["spawn-tree", "20000"], timeout: TimeSpan.FromMilliseconds(300));

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        var marker = JsonSerializer.Deserialize<SpawnTreeMarker>(
            result.StandardOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(marker);

        await AssertProcessIsNoLongerRunningAsync(marker!.ChildPid);
    }

    [Fact]
    public async Task Output_beyond_the_capture_caps_is_discarded_and_flagged_truncated()
    {
        // Both streams write more than the default 64 KiB per-stream cap. stdout consumes its
        // full 64 KiB share of the default combined 128 KiB budget, leaving stderr exactly
        // enough shared budget for its own 64 KiB cap too — so both are capped at exactly the
        // documented defaults and both are flagged truncated.
        var request = CreateRequest(["write-bytes", "100000", "100000"]);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionRequest.DefaultMaxBytesPerStream, result.StandardOutput.Length);
        Assert.True(result.StandardOutputTruncated);
        Assert.Equal(ProcessExecutionRequest.DefaultMaxBytesPerStream, result.StandardError.Length);
        Assert.True(result.StandardErrorTruncated);
    }

    [Fact]
    public async Task Arguments_containing_shell_metacharacters_reach_the_child_literally()
    {
        string[] arguments =
        [
            "a && b",
            "$(whoami)",
            "`echo pwned`",
            "a; rm -rf /",
            "a | cat",
            "quoted \"value\"",
            "trailing space ",
            string.Empty,
        ];
        var request = CreateRequest(["echo-args", .. arguments]);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        var echoed = JsonSerializer.Deserialize<string[]>(result.StandardOutput);
        Assert.Equal(arguments, echoed);
    }

    [Fact]
    public async Task A_working_directory_outside_the_approved_root_is_rejected_before_any_process_starts()
    {
        var outsideDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-process-tests-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            var request = new ProcessExecutionRequest
            {
                ExecutablePath = FixtureExecutablePath,
                Arguments = ["exit-code", "0"],
                WorkingDirectory = outsideDirectory,
                ApprovedRoot = _approvedRoot,
                Timeout = TimeSpan.FromSeconds(5),
            };

            await Assert.ThrowsAsync<ArgumentException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Only_the_explicit_environment_allowlist_is_visible_to_the_child()
    {
        var request = CreateRequest(["print-env", "DEVALCOPILOT_TEST_VAR"], environmentVariables: new Dictionary<string, string>
        {
            ["DEVALCOPILOT_TEST_VAR"] = "hello",
        });

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("hello" + Environment.NewLine, result.StandardOutput);

        // PATH is always present in this test host's own ambient environment, but the adapter
        // never forwards it unless the caller explicitly allowlists it.
        var pathRequest = CreateRequest(["print-env", "PATH"], environmentVariables: new Dictionary<string, string>
        {
            ["DEVALCOPILOT_TEST_VAR"] = "hello",
        });

        var pathResult = await _adapter.ExecuteAsync(pathRequest, CancellationToken.None);
        Assert.Equal("<unset>" + Environment.NewLine, pathResult.StandardOutput);
    }

    [Fact]
    public async Task A_relative_executable_path_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"]) with { ExecutablePath = "ProcessExecutionFixture.exe" };

        await Assert.ThrowsAsync<ArgumentException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_executable_path_is_rejected_before_any_process_starts()
    {
        var missingPath = Path.Combine(_approvedRoot, "does-not-exist.exe");
        var request = CreateRequest(["exit-code", "0"]) with { ExecutablePath = missingPath };

        await Assert.ThrowsAsync<ArgumentException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_relative_approved_root_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"]) with { ApprovedRoot = "relative-root" };

        await Assert.ThrowsAsync<ArgumentException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task An_argument_count_beyond_the_maximum_is_rejected_before_any_process_starts()
    {
        var arguments = Enumerable.Range(0, ProcessExecutionRequest.MaxArgumentCount + 1)
            .Select(index => index.ToString())
            .ToArray();
        var request = CreateRequest(["echo-args", .. arguments]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task An_argument_longer_than_the_maximum_utf8_byte_length_is_rejected_before_any_process_starts()
    {
        var oversizedArgument = new string('a', ProcessExecutionRequest.MaxArgumentUtf8Bytes + 1);
        var request = CreateRequest(["echo-args", oversizedArgument]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_zero_timeout_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"], timeout: TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_negative_non_infinite_timeout_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"], timeout: TimeSpan.FromSeconds(-2));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task An_infinite_timeout_is_accepted_for_a_process_that_exits_on_its_own()
    {
        var request = CreateRequest(["exit-code", "0"], timeout: Timeout.InfiniteTimeSpan);

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
    }

    [Fact]
    public async Task A_negative_max_bytes_per_stream_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"]) with { MaxBytesPerStream = -1 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_negative_max_total_captured_bytes_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"]) with { MaxTotalCapturedBytes = -1 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_max_total_captured_bytes_beyond_the_allowed_ceiling_is_rejected_before_any_process_starts()
    {
        var request = CreateRequest(["exit-code", "0"])
            with
        { MaxTotalCapturedBytes = ProcessExecutionRequest.MaxAllowedTotalCapturedBytes + 1 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _adapter.ExecuteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_filesystem_root_approved_root_correctly_contains_a_real_subdirectory()
    {
        var filesystemRoot = Path.GetPathRoot(_approvedRoot)!;
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = FixtureExecutablePath,
            Arguments = ["exit-code", "0"],
            WorkingDirectory = _approvedRoot,
            ApprovedRoot = filesystemRoot,
            Timeout = TimeSpan.FromSeconds(5),
        };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
    }

    [Fact]
    public async Task A_filesystem_root_approved_root_is_correctly_recognised_as_its_own_working_directory()
    {
        // Regression coverage for treating the root itself as a valid working directory: a
        // naive TrimEnd of trailing separator characters turns "C:\" into "C:", which no
        // longer string-equals the canonicalized working directory "C:\" and means something
        // different to Windows path APIs (drive-relative rather than the drive root).
        var filesystemRoot = Path.GetPathRoot(_approvedRoot)!;
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = FixtureExecutablePath,
            Arguments = ["exit-code", "0"],
            WorkingDirectory = filesystemRoot,
            ApprovedRoot = filesystemRoot,
            Timeout = TimeSpan.FromSeconds(5),
        };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
    }

    [Fact]
    public async Task Killing_a_process_tree_that_already_exited_on_its_own_does_not_throw()
    {
        // Deterministic coverage for the timeout/cancellation exit race: Process.Kill(true)
        // throws InvalidOperationException if the process has already exited by the time it
        // is called. Rather than reproduce the narrow timing window inside ExecuteAsync, this
        // exercises the exact same helper against a process that has, with certainty, already
        // exited — proving the "already gone" case is swallowed rather than propagated.
        var startInfo = new ProcessStartInfo(FixtureExecutablePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add("exit-code");
        startInfo.ArgumentList.Add("0");

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();

        var exception = Record.Exception(() => ProcessTreeTermination.KillIfStillRunning(process));

        Assert.Null(exception);
    }

    private ProcessExecutionRequest CreateRequest(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null) =>
        new()
        {
            ExecutablePath = FixtureExecutablePath,
            Arguments = arguments,
            WorkingDirectory = _approvedRoot,
            ApprovedRoot = _approvedRoot,
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>(),
        };

    private static async Task AssertProcessIsNoLongerRunningAsync(int processId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                // The OS has already recycled the process id: it is gone.
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail($"Process {processId} was still running {5} seconds after the owning tree was killed.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_approvedRoot))
        {
            Directory.Delete(_approvedRoot, recursive: true);
        }
    }

    private sealed record SpawnTreeMarker(int ParentPid, int ChildPid);
}
