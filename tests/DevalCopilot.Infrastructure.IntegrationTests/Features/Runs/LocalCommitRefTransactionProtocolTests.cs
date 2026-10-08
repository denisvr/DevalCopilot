using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The prepared reference-transaction owner at its production boundary (ADR-0029 R1). A real child process, driven by the
/// repository's deterministic <c>ProcessExecutionFixture</c> in a scripted protocol mode, stands in for
/// <c>git update-ref --stdin</c>; the owner itself is the production type with its production protocol, deadline, pipe-closure,
/// termination and reaping code. Every case is labelled by the phase that failed, and the tests prove no command is resent.
/// </summary>
public sealed class LocalCommitRefTransactionProtocolTests : IDisposable
{
    private const string Branch = "devalcopilot/workspace/test/1";
    private static readonly string Commit = new('a', 40);
    private static readonly string Parent = new('b', 40);
    private static readonly string FixtureExecutable = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(25);

    // Small budgets keep the faults deterministic while staying far above the fixture's start-up and teardown time even when the
    // whole suite runs in parallel: a silent child spends two fifths of the cleanup budget on its abort exchange before stdin is
    // closed, so the remaining exit wait must still comfortably cover a .NET process exit on a loaded machine.
    private static readonly LocalCommitRefTransactionBudgets Small = new(
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"devalcopilot-ref-protocol-{Guid.NewGuid():N}");
    private readonly List<string> _phases = [];

    public LocalCommitRefTransactionProtocolTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        foreach (var file in new[] { PidFile, ChildPidFile })
        {
            TryKill(file);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private string PidFile => Path.Combine(_directory, "pid.txt");

    private string ChildPidFile => Path.Combine(_directory, "child-pid.txt");

    private string RequestLog => Path.Combine(_directory, "requests.log");

    private string[] Requests => File.Exists(RequestLog)
        ? File.ReadAllLines(RequestLog).Where(line => line.Length > 0).ToArray()
        : [];

    private static Dictionary<string, object?> Step(
        int read, string? raw = null, string? stderr = null, string? base64 = null, int sleepMs = 0) => new()
        {
            ["ReadLines"] = read,
            ["WriteRaw"] = raw,
            ["WriteStderr"] = stderr,
            ["WriteBase64"] = base64,
            ["SleepMs"] = sleepMs,
        };

    /// <summary>The two healthy acknowledgements that precede commit/abort, plus the final scripted step.</summary>
    private ProcessStartInfo Script(
        Dictionary<string, object?> final, bool hang = false, bool waitForEof = true, int exitCode = 0, bool trackProcesses = false,
        bool childHoldsOutput = false)
    {
        var steps = new List<Dictionary<string, object?>> { Step(1, "start: ok\n"), Step(2, "prepare: ok\n"), final };
        return Launch(steps, hang, waitForEof, exitCode, trackProcesses, childHoldsOutput);
    }

    private ProcessStartInfo Launch(
        List<Dictionary<string, object?>> steps, bool hang, bool waitForEof, int exitCode, bool trackProcesses,
        bool childHoldsOutput = false)
    {
        var script = Path.Combine(_directory, $"script-{Guid.NewGuid():N}.json");
        File.WriteAllText(script, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Steps"] = steps,
            ["ThenHang"] = hang,
            ["ThenWaitForEof"] = waitForEof,
            ["ExitCode"] = exitCode,
            ["PidFilePath"] = PidFile,
            ["ChildPidFilePath"] = trackProcesses || childHoldsOutput ? ChildPidFile : null,
            ["ChildHoldsOutputOnly"] = childHoldsOutput,
            ["RequestLogPath"] = RequestLog,
        }));
        var info = new ProcessStartInfo(FixtureExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--stdio");
        return info;
    }

    private async Task<LocalCommitRefTransactionStart> StartAsync(
        ProcessStartInfo info, LocalCommitRefTransactionBudgets? budgets = null, CancellationToken cancellation = default,
        Action<string>? observer = null) =>
        await LocalCommitRefTransaction.StartAsync(
            info, Branch, Commit, Parent, budgets ?? Small, phase =>
            {
                _phases.Add(phase);
                observer?.Invoke(phase);
            }, cancellation).WaitAsync(Guard);

    private static bool IsAlive(string pidFile)
    {
        if (!File.Exists(pidFile))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim()));
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKill(string pidFile)
    {
        if (!IsAlive(pidFile))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim()));
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }
    }

    private async Task<(LocalCommitRefCommitOutcome Outcome, string Reason)> PrepareAndCommitAsync(
        ProcessStartInfo info, CancellationToken cancellation = default, LocalCommitRefTransactionBudgets? budgets = null)
    {
        var start = await StartAsync(info, budgets);
        Assert.True(start.Transaction is not null, "prepare should succeed: " + start.Reason);
        await using var transaction = start.Transaction;
        var outcome = await transaction.CommitAsync(cancellation).WaitAsync(Guard);
        return (outcome, transaction.FailureReason);
    }

    [Fact]
    public async Task A_healthy_commit_requires_actual_eof_and_sends_each_command_exactly_once()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\n"), waitForEof: true));

        Assert.True(outcome == LocalCommitRefCommitOutcome.Acknowledged, reason);
        Assert.Equal(
            ["start", $"update refs/heads/{Branch} {Commit} {Parent}", "prepare", "commit"], Requests);
        Assert.Equal(["launched", "start_sent", "start_acknowledged", "update_sent", "prepare_sent", "prepared"],
            _phases.Take(6).ToArray());
        Assert.Contains("commit_sent", _phases);
        Assert.Contains("committed", _phases);
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task A_healthy_abort_confirms_without_ever_sending_commit()
    {
        var start = await StartAsync(Script(Step(1, "abort: ok\n")));
        await using var transaction = start.Transaction!;

        var outcome = await transaction.AbortAsync().WaitAsync(Guard);

        Assert.Equal(LocalCommitRefAbortOutcome.Confirmed, outcome);
        Assert.Equal(["start", $"update refs/heads/{Branch} {Commit} {Parent}", "prepare", "abort"], Requests);
        Assert.DoesNotContain("commit_sent", _phases);
        Assert.False(IsAlive(PidFile));
    }

    public static TheoryData<string, string, string> MalformedAcknowledgements => new()
    {
        { "crlf", "commit: ok\r\n", "commit:ack_malformed" },
        { "embedded-cr", "com\rmit: ok\n", "commit:ack_malformed" },
        { "nul-byte", "commit\0: ok\n", "commit:ack_malformed" },
        { "non-ascii", "commit: ok é\n", "commit:ack_malformed" },
        { "wrong-text", "commit: no\n", "commit:ack_unexpected" },
        { "wrong-case", "Commit: ok\n", "commit:ack_unexpected" },
        { "trailing-space", "commit: ok \n", "commit:ack_unexpected" },
        { "overlong-with-newline", new string('x', 400) + "\n", "commit:ack_overflow" },
        { "overlong-without-newline", new string('x', 5000), "commit:ack_overflow" },
    };

    [Theory]
    [MemberData(nameof(MalformedAcknowledgements))]
    public async Task A_malformed_commit_acknowledgement_is_uncertain_and_never_accepted(string label, string raw, string expected)
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, raw), waitForEof: true));

        Assert.True(outcome == LocalCommitRefCommitOutcome.Uncertain, label + ": " + reason);
        Assert.Equal(expected, reason);
        Assert.Single(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task Invalid_utf8_bytes_in_the_commit_acknowledgement_are_malformed()
    {
        var invalid = Convert.ToBase64String([(byte)'c', 0xFF, 0xFE, (byte)'\n']);
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, base64: invalid)));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:ack_malformed", reason);
    }

    [Fact]
    public async Task An_acknowledgement_without_a_newline_that_then_hangs_names_the_commit_phase_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok"), hang: true, trackProcesses: true));
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:ack_timeout", reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(12), $"unbounded: {stopwatch.Elapsed}");
        Assert.Single(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile), "the owned child must be terminated and reaped");
        Assert.False(IsAlive(ChildPidFile), "the owned child's descendants must be terminated");
    }

    [Fact]
    public async Task An_acknowledgement_without_a_newline_followed_by_EOF_is_a_distinct_eof_fault()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok"), waitForEof: false));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:ack_eof", reason);
    }

    [Fact]
    public async Task A_missing_acknowledgement_with_a_nonzero_exit_is_uncertain_and_not_resent()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1), waitForEof: false, exitCode: 128));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:ack_eof", reason);
        Assert.Single(Requests, request => request == "commit");
    }

    [Fact]
    public async Task Stderr_beyond_its_byte_bound_fails_an_otherwise_healthy_commit()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\n", stderr: new string('E', 5000))));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:stderr_overflow", reason);
    }

    [Fact]
    public async Task Bounded_stderr_does_not_by_itself_fail_a_healthy_commit()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\n", stderr: new string('E', 512))));

        Assert.True(outcome == LocalCommitRefCommitOutcome.Acknowledged, reason);
    }

    [Fact]
    public async Task Unsolicited_extra_standard_output_after_the_acknowledgement_is_uncertain()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\nsurprise\n")));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:extra_output", reason);
    }

    [Fact]
    public async Task A_nonzero_exit_after_a_healthy_acknowledgement_is_uncertain()
    {
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\n"), exitCode: 3));

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:exit_code", reason);
    }

    [Fact]
    public async Task A_child_that_acknowledges_but_never_exits_is_terminated_reaped_and_reported_by_phase()
    {
        var stopwatch = Stopwatch.StartNew();
        var (outcome, reason) = await PrepareAndCommitAsync(Script(Step(1, "commit: ok\n"), hang: true, trackProcesses: true));
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:exit_timeout", reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"unbounded: {stopwatch.Elapsed}");
        Assert.False(IsAlive(PidFile));
        Assert.False(IsAlive(ChildPidFile));
        Assert.Contains("killed", _phases);
    }

    [Fact]
    public async Task Caller_cancellation_while_waiting_for_the_commit_acknowledgement_is_uncertain_and_reaps_the_child()
    {
        using var cancellation = new CancellationTokenSource();
        var info = Script(Step(1, "commit: ok\n", sleepMs: 20_000), hang: true, trackProcesses: true);
        var start = await StartAsync(info, Small with { Phase = TimeSpan.FromSeconds(20) });
        await using var transaction = start.Transaction!;

        var pending = transaction.CommitAsync(cancellation.Token);
        await Task.Delay(400);
        cancellation.Cancel();
        var outcome = await pending.WaitAsync(Guard);

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:ack_cancelled", transaction.FailureReason);
        Assert.Single(Requests, request => request == "commit");
        await transaction.DisposeAsync();
        Assert.False(IsAlive(PidFile));
        Assert.False(IsAlive(ChildPidFile));
    }

    [Fact]
    public async Task Commit_is_refused_after_the_transaction_deadline_and_abort_still_completes_in_its_own_budget()
    {
        var budgets = new LocalCommitRefTransactionBudgets(
            TimeSpan.FromMilliseconds(2500), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        var start = await StartAsync(Script(Step(1, "abort: ok\n")), budgets);
        await using var transaction = start.Transaction!;

        var proof = await transaction.RunProofAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return true;
            },
            CancellationToken.None).WaitAsync(Guard);
        Assert.False(proof);
        Assert.Equal("proof:deadline", transaction.FailureReason);

        var commit = await transaction.CommitAsync(CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(LocalCommitRefCommitOutcome.NotAttempted, commit);
        Assert.DoesNotContain(Requests, request => request == "commit");

        var abort = await transaction.AbortAsync().WaitAsync(Guard);
        Assert.Equal(LocalCommitRefAbortOutcome.Confirmed, abort);
        Assert.Equal("abort", Requests[^1]);
    }

    [Fact]
    public async Task The_owners_deadline_is_passed_to_every_proof_read_not_none()
    {
        var budgets = Small with { Total = TimeSpan.FromSeconds(8) };
        var start = await StartAsync(Script(Step(1, "abort: ok\n")), budgets);
        await using var transaction = start.Transaction!;
        CancellationToken observed = default;

        var proof = await transaction.RunProofAsync(
            token =>
            {
                observed = token;
                return Task.FromResult(true);
            },
            CancellationToken.None).WaitAsync(Guard);

        Assert.True(proof);
        Assert.True(observed.CanBeCanceled, "a proof read must receive a cancellable owner scope, never CancellationToken.None");
    }

    [Fact]
    public async Task Caller_cancellation_during_a_proof_propagates_and_abort_remains_bounded()
    {
        var start = await StartAsync(Script(Step(1, "abort: ok\n")));
        await using var transaction = start.Transaction!;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.RunProofAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return true;
            },
            cancellation.Token).WaitAsync(Guard));

        Assert.Equal(LocalCommitRefAbortOutcome.Confirmed, await transaction.AbortAsync().WaitAsync(Guard));
    }

    [Fact]
    public async Task An_abort_the_child_ignores_is_unconfirmed_within_the_cleanup_budget_and_the_child_is_terminated()
    {
        var info = Script(Step(1), hang: true, trackProcesses: true);
        var start = await StartAsync(info, Small with { Cleanup = TimeSpan.FromMilliseconds(1200) });
        await using var transaction = start.Transaction!;
        var stopwatch = Stopwatch.StartNew();

        var outcome = await transaction.AbortAsync().WaitAsync(Guard);
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefAbortOutcome.Unconfirmed, outcome);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"cleanup exceeded its budget: {stopwatch.Elapsed}");
        Assert.False(IsAlive(PidFile));
        Assert.False(IsAlive(ChildPidFile));
    }

    [Fact]
    public async Task Disposal_without_a_terminal_command_aborts_closes_and_reaps_the_owned_child()
    {
        var start = await StartAsync(Script(Step(1, "abort: ok\n")));
        var transaction = start.Transaction!;

        await transaction.DisposeAsync().AsTask().WaitAsync(Guard);

        Assert.Equal("abort", Requests[^1]);
        Assert.DoesNotContain(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task A_second_terminal_command_is_never_sent()
    {
        var start = await StartAsync(Script(Step(1, "commit: ok\n")));
        await using var transaction = start.Transaction!;

        Assert.Equal(LocalCommitRefCommitOutcome.Acknowledged, await transaction.CommitAsync(CancellationToken.None).WaitAsync(Guard));
        Assert.Equal(LocalCommitRefCommitOutcome.NotAttempted, await transaction.CommitAsync(CancellationToken.None).WaitAsync(Guard));
        Assert.Equal(LocalCommitRefAbortOutcome.Unconfirmed, await transaction.AbortAsync().WaitAsync(Guard));
        Assert.Single(Requests, request => request == "commit");
        Assert.DoesNotContain(Requests, request => request == "abort");
    }

    public static TheoryData<string, int, string, string, bool, bool> StartFaults => new()
    {
        // label, scripted-exit, start-ack, prepare-ack, hang, proves-no-mutation
        { "prepare-refused-eof", 128, "start: ok\n", "", false, true },
        { "start-ack-malformed", 0, "start: ok\r\n", "prepare: ok\n", false, true },
        { "prepare-ack-unexpected", 0, "start: ok\n", "prepare: fine\n", false, true },
        { "prepare-ack-missing-newline-hang", 0, "start: ok\n", "prepare: ok", true, false },
    };

    [Theory]
    [MemberData(nameof(StartFaults))]
    public async Task A_start_or_prepare_fault_never_yields_a_transaction_and_reaps_the_child(
        string label, int exitCode, string startAck, string prepareAck, bool hang, bool noMutationProven)
    {
        var steps = new List<Dictionary<string, object?>> { Step(1, startAck), Step(2, prepareAck) };
        var info = Launch(steps, hang, waitForEof: label != "prepare-refused-eof", exitCode, trackProcesses: hang);

        var start = await StartAsync(info);

        Assert.Null(start.Transaction);
        Assert.True(start.NoMutationProven == noMutationProven, label + ": " + start.Reason);
        Assert.DoesNotContain(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile), label);
        if (hang)
        {
            Assert.False(IsAlive(ChildPidFile));
            Assert.Equal("prepare:ack_timeout", start.Reason);
        }
    }

    [Fact]
    public async Task A_launch_failure_proves_no_mutation_and_names_the_launch_phase()
    {
        var info = new ProcessStartInfo(Path.Combine(_directory, "missing-git.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var start = await StartAsync(info);

        Assert.Null(start.Transaction);
        Assert.True(start.NoMutationProven);
        Assert.Equal("launch:failed", start.Reason);
    }

    [Fact]
    public async Task Caller_cancellation_during_prepare_reaps_the_child_and_never_reports_a_transaction()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var steps = new List<Dictionary<string, object?>> { Step(1, "start: ok\n"), Step(2, "prepare: ok\n", sleepMs: 20_000) };
        var info = Launch(steps, hang: true, waitForEof: false, exitCode: 0, trackProcesses: true);

        var start = await StartAsync(info, Small with { Phase = TimeSpan.FromSeconds(20) }, cancellation.Token);

        Assert.Null(start.Transaction);
        Assert.Equal("prepare:ack_cancelled", start.Reason);
        Assert.False(start.NoMutationProven);
        Assert.False(IsAlive(PidFile));
        Assert.False(IsAlive(ChildPidFile));
    }

    public enum StreamFault
    {
        StdoutDisposed,
        StderrDrainCancelled,
    }

    /// <summary>Breaks the owner's final-stream evidence at the instant the terminal acknowledgement has been admitted. Disposing the
    /// actual stdout pipe makes the owner's final read hit a real faulted stream. A pending pipe read survives disposal of its stream and
    /// would still observe the child's real end of file, so the standard-error fault is instead the cancellation of the owner's own
    /// drain, which ends the drain before any end of file was read.</summary>
    private static Action<string> BreakStreamAt(string phase, StreamFault fault, Func<LocalCommitRefTransaction?> owner) => observed =>
    {
        if (observed != phase)
        {
            return;
        }

        var field = (string name) => typeof(LocalCommitRefTransaction).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (fault == StreamFault.StdoutDisposed)
        {
            ((Process)field("process").GetValue(owner())!).StandardOutput.BaseStream.Dispose();
        }
        else
        {
            ((CancellationTokenSource)field("drain").GetValue(owner())!).Cancel();
        }
    };

    [Theory]
    [InlineData(StreamFault.StdoutDisposed, "commit:stdout_unconfirmed")]
    [InlineData(StreamFault.StderrDrainCancelled, "commit:stderr_unconfirmed")]
    public async Task A_stream_that_faults_after_the_commit_acknowledgement_never_confirms_the_commit(StreamFault fault, string expected)
    {
        LocalCommitRefTransaction? transaction = null;
        var start = await StartAsync(
            Script(Step(1, "commit: ok\n")), observer: BreakStreamAt("commit_acknowledged", fault, () => transaction));
        transaction = start.Transaction!;
        await using var owner = transaction;

        var outcome = await owner.CommitAsync(CancellationToken.None).WaitAsync(Guard);

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal(expected, owner.FailureReason);
        Assert.DoesNotContain("committed", _phases);
        Assert.Single(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile), "the owned child is still reaped");
    }

    [Theory]
    [InlineData(StreamFault.StdoutDisposed, "abort:stdout_unconfirmed")]
    [InlineData(StreamFault.StderrDrainCancelled, "abort:stderr_unconfirmed")]
    public async Task A_stream_that_faults_after_the_abort_acknowledgement_never_confirms_the_abort(StreamFault fault, string expected)
    {
        LocalCommitRefTransaction? transaction = null;
        var start = await StartAsync(
            Script(Step(1, "abort: ok\n")), observer: BreakStreamAt("abort_acknowledged", fault, () => transaction));
        transaction = start.Transaction!;
        await using var owner = transaction;

        var outcome = await owner.AbortAsync().WaitAsync(Guard);

        Assert.Equal(LocalCommitRefAbortOutcome.Unconfirmed, outcome);
        Assert.Equal(expected, owner.FailureReason);
        Assert.DoesNotContain("aborted", _phases);
        Assert.DoesNotContain(Requests, request => request == "commit");
        Assert.Single(Requests, request => request == "abort");
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task Standard_output_that_never_reaches_end_of_file_leaves_the_commit_uncertain_within_the_cleanup_budget()
    {
        var info = Script(Step(1, "commit: ok\n"), childHoldsOutput: true);
        var stopwatch = Stopwatch.StartNew();

        var (outcome, reason) = await PrepareAndCommitAsync(info, budgets: Small with { Cleanup = TimeSpan.FromSeconds(3) });
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:stdout_unconfirmed", reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"unbounded: {stopwatch.Elapsed}");
        Assert.DoesNotContain("committed", _phases);
        Assert.Single(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task Standard_output_that_never_reaches_end_of_file_leaves_the_abort_unconfirmed_within_the_cleanup_budget()
    {
        var start = await StartAsync(
            Script(Step(1, "abort: ok\n"), childHoldsOutput: true), Small with { Cleanup = TimeSpan.FromSeconds(3) });
        await using var transaction = start.Transaction!;
        var stopwatch = Stopwatch.StartNew();

        var outcome = await transaction.AbortAsync().WaitAsync(Guard);
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefAbortOutcome.Unconfirmed, outcome);
        Assert.Equal("abort:stdout_unconfirmed", transaction.FailureReason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"unbounded: {stopwatch.Elapsed}");
        Assert.DoesNotContain("aborted", _phases);
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task Standard_error_that_never_reaches_end_of_file_leaves_the_commit_uncertain_within_the_cleanup_budget()
    {
        // The descendant inherits both pipes, so the child itself exits cleanly but standard error is never closed.
        var info = Script(Step(1, "commit: ok\n"), trackProcesses: true);
        var stopwatch = Stopwatch.StartNew();

        var (outcome, reason) = await PrepareAndCommitAsync(info, budgets: Small with { Cleanup = TimeSpan.FromSeconds(3) });
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefCommitOutcome.Uncertain, outcome);
        Assert.Equal("commit:stderr_unfinished", reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"unbounded: {stopwatch.Elapsed}");
        Assert.DoesNotContain("committed", _phases);
        Assert.Single(Requests, request => request == "commit");
        Assert.False(IsAlive(PidFile));
    }

    [Fact]
    public async Task Standard_error_that_never_reaches_end_of_file_leaves_the_abort_unconfirmed_within_the_cleanup_budget()
    {
        var start = await StartAsync(Script(Step(1, "abort: ok\n"), trackProcesses: true), Small with { Cleanup = TimeSpan.FromSeconds(3) });
        await using var transaction = start.Transaction!;
        var stopwatch = Stopwatch.StartNew();

        var outcome = await transaction.AbortAsync().WaitAsync(Guard);
        stopwatch.Stop();

        Assert.Equal(LocalCommitRefAbortOutcome.Unconfirmed, outcome);
        Assert.Equal("abort:stderr_unfinished", transaction.FailureReason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"unbounded: {stopwatch.Elapsed}");
        Assert.DoesNotContain("aborted", _phases);
        Assert.False(IsAlive(PidFile));
    }
}
