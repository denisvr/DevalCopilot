using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.Processes.Ports;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R1 and QP-R2 (correction 2): the deadline waiter never waits for admission work in flight, expiry during validation or
/// durable marker work starts nothing, authoritative success is never printed or kept before the required closure, a writer that
/// really appends after its release leaves only non-final bytes, and every report fits inside the stated outer budget.</summary>
public sealed class WholeEntryFinalizationTests : QualificationTestBase
{
    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    private ProcessAdmission OwnedAdmission(Func<ProcessShape, string?>? revalidate = null, Func<string?>? external = null) =>
        new([new InventoryEntry("owned.probe", 1, _ => true)], 1, new AdmissionLedger(LedgerDirectory, "owned-test", TimeProvider.System), revalidate, external);

    private static readonly ProcessShape Owned = new("owned.exe", []);

    // QP-R2: expiry during validation and at the dispatch handoff ----------------------------------------------------------------

    [Fact]
    public void Expiry_during_a_successful_slow_validation_refuses_the_start_and_consumes_nothing()
    {
        // The reviewer's control: a 40 ms deadline against a 200 ms successful validator.
        var deadline = EntryDeadline.Start(TimeSpan.FromMilliseconds(40));
        var admission = OwnedAdmission(_ => { Thread.Sleep(200); return null; }, () => deadline.IsExpired ? "OuterDeadlineExceeded" : null);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(Owned));

        Assert.Equal(ProcessAdmission.ClosedCode, refusal.Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.Equal("OuterDeadlineExceeded", admission.Facts.ClosedBy);
        Assert.Empty(admission.Ledger.Consumed());
    }

    [Fact]
    public void Expiry_during_the_durable_marker_work_keeps_the_consumed_marker_and_starts_nothing()
    {
        var calls = 0;
        // Checks: the start, after validation, after the marker. The third answers expired.
        var admission = OwnedAdmission(external: () => ++calls >= 3 ? "OuterDeadlineExceeded" : null);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(Owned));

        Assert.Equal(ProcessAdmission.ExpiredBeforeStartCode, refusal.Code);
        Assert.Equal(1, admission.Ledger.Consumed()["owned.probe"]);
        Assert.Equal(1, admission.Ledger.Consumed()[AdmissionLedger.TotalName]);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.Equal(1, admission.Facts.Refused[ProcessAdmission.ExpiredBeforeStartCode]);
    }

    [Fact]
    public async Task Expiry_at_the_dispatch_handoff_never_reaches_the_process_boundary_and_keeps_the_marker()
    {
        var calls = 0;
        // Checks: the start, after validation, after the marker (all open), then the handoff (expired).
        var admission = OwnedAdmission(external: () => ++calls >= 4 ? "OuterDeadlineExceeded" : null);
        var inner = new ScriptedStatusAdapter(ScriptedStatusAdapter.Authenticated());
        var boundary = new AdmittingProcessExecutionAdapter(inner, admission);
        var request = new ProcessExecutionRequest { ExecutablePath = "owned.exe", WorkingDirectory = @"C:\w", ApprovedRoot = @"C:\w", Timeout = TimeSpan.FromSeconds(1) };

        var refusal = await Assert.ThrowsAsync<ProcessAdmissionRefusedException>(() => boundary.ExecuteAsync(request, CancellationToken.None));

        Assert.Equal(ProcessAdmission.ExpiredBeforeStartCode, refusal.Code);
        Assert.Empty(inner.Requests);
        Assert.Equal(1, admission.Ledger.Consumed()["owned.probe"]);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.False(admission.Facts.Admitted.ContainsKey("owned.probe"));
    }

    [Fact]
    public void A_refusal_that_closes_the_gate_after_the_admit_stops_the_admitted_request_at_the_handoff()
    {
        var admission = OwnedAdmission();
        var admitted = admission.Admit(Owned);
        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(new ProcessShape("other.exe", [])));

        Assert.Equal("owned.probe", admitted);
        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.ConfirmOpen(admitted));
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.Equal(1, admission.Ledger.Consumed()["owned.probe"]);
    }

    [Fact]
    public async Task Reading_the_progress_never_waits_for_a_request_whose_validation_is_in_flight()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var admission = OwnedAdmission(_ => { entered.Set(); release.Wait(); return null; });
        var admitting = Task.Run(() => admission.Admit(Owned));
        await Task.Run(() => entered.Wait());

        var clock = Stopwatch.StartNew();
        var reads = await Task.Run(() => (admission.Facts, admission.IsClosed, admission.Facts));
        var elapsed = clock.Elapsed;
        release.Set();
        await admitting;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), "the progress read waited for the admission lock");
        Assert.Equal(["owned.probe"], reads.Item1.PendingClasses);
        Assert.False(reads.Item2);
        Assert.Equal(0, reads.Item1.TotalAdmitted);
        Assert.Empty(admission.Facts.PendingClasses);
        Assert.Equal(1, admission.Facts.TotalAdmitted);
    }

    [Fact]
    public async Task Concurrent_progress_reads_always_see_a_coherent_snapshot_while_requests_are_admitted_and_refused()
    {
        var entries = new[] { new InventoryEntry("owned.probe", 20, shape => shape.ExecutablePath == "owned.exe") };
        var admission = new ProcessAdmission(entries, 20, new AdmissionLedger(LedgerDirectory, "owned-test", TimeProvider.System));
        using var stop = new CancellationTokenSource();
        var violations = 0;
        var reader = Task.Run(() =>
        {
            var lastAdmitted = 0;
            while (!stop.IsCancellationRequested)
            {
                var facts = admission.Facts;
                var admitted = facts.TotalAdmitted;
                if (admitted < lastAdmitted || facts.PendingClasses.Count > 1 || (facts.ClosedBy is not null && facts.TotalRefused == 0))
                {
                    Interlocked.Increment(ref violations);
                }

                lastAdmitted = admitted;
            }
        });

        for (var index = 0; index < 20; index++)
        {
            admission.Admit(Owned);
        }

        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(Owned));
        await stop.CancelAsync();
        await reader;

        Assert.Equal(0, violations);
        Assert.Equal(20, admission.Facts.TotalAdmitted);
        Assert.Equal("ClassLimit.owned.probe", admission.Facts.ClosedBy);
    }

    // QP-R1: the waiter and the progress snapshot --------------------------------------------------------------------------------

    [Fact]
    public async Task The_deadline_waiter_reports_admission_progress_without_waiting_for_a_validation_in_flight()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ProcessAdmission? admission = null;
        var environment = new ScriptedEnvironment
        {
            AdmissionFor = _ => admission!.Facts,
            OnSubmit = (_, _) =>
            {
                _ = Task.Run(() => admission!.Admit(Owned));
                entered.Wait();
                return new TaskCompletionSource<SubmissionResult>().Task;
            },
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(800) },
            deadline =>
            {
                admission = OwnedAdmission(
                    _ => { entered.Set(); release.Wait(); return null; }, () => deadline.IsExpired ? "OuterDeadlineExceeded" : null);
                return environment;
            },
            output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        release.Set();

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exit);
        Assert.True(elapsed < TimeSpan.FromSeconds(3), "the deadline waiter waited for the admission work it was bounding");
        using var summary = JsonDocument.Parse(output.ToString());
        var progress = summary.RootElement.GetProperty("processAdmission");
        Assert.Equal("owned.probe", Assert.Single(progress.GetProperty("pending").EnumerateArray()).GetString());
        Assert.Equal("OuterDeadlineExceeded", progress.GetProperty("closedBy").GetString());
        Assert.False(summary.RootElement.GetProperty("finalizationComplete").GetBoolean());
    }

    // QP-R1: authoritative success only after the required closure ---------------------------------------------------------------

    [Fact]
    public async Task A_held_disposal_that_outlasts_the_deadline_leaves_no_qualified_summary_printed_or_kept()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var environment = new ScriptedEnvironment { OnDispose = () => new ValueTask(release.Task) };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(800) }, () => environment,
            output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        release.SetResult();
        await Task.Delay(500);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exit);
        Assert.True(elapsed < TimeSpan.FromSeconds(3));
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        using var verdict = JsonDocument.Parse(File.ReadAllText(saved));
        Assert.NotEqual("Qualified", verdict.RootElement.GetProperty("result").GetString());
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("code").GetString());
        Assert.Equal("ReportOrClosure", verdict.RootElement.GetProperty("expiredDuring").GetString());
        // The success summary was never rendered, because the closure it must state had not finished: nothing reached the sink.
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("unproven", error.ToString(), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
    }

    [Fact]
    public async Task A_writer_that_appends_after_its_release_leaves_non_final_bytes_and_persists_only_the_unproven_verdict()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var environment = new ScriptedEnvironment();
        using var output = new AppendingWriter(release.Task);
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(800) }, () => environment,
            output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        var beforeRelease = output.ToString();
        release.SetResult();
        await output.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exit);
        Assert.True(elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(string.Empty, beforeRelease);
        Assert.Contains("unproven", error.ToString(), StringComparison.Ordinal);
        // The call that was already inside the sink could not be revoked, so its bytes appear late; they are unproven. What the entry
        // itself persisted, and what its exit code says, is the unproven verdict, never the late success.
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        using var verdict = JsonDocument.Parse(File.ReadAllText(saved));
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("code").GetString());
        Assert.False(verdict.RootElement.GetProperty("finalizationComplete").GetBoolean());
        Assert.Equal(1, environment.Count("dispose"));
    }

    [Fact]
    public async Task A_normal_end_closes_the_environment_first_then_prints_a_summary_that_states_it_then_persists_the_same_text()
    {
        var order = new List<string>();
        var environment = new ScriptedEnvironment { OnDispose = () => { order.Add("dispose"); return ValueTask.CompletedTask; } };
        using var output = new RecordingWriter(() => order.Add("print"));
        using var error = new StringWriter();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromSeconds(20) }, () => environment,
            output, error, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["dispose", "print"], order);
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Qualified", summary.RootElement.GetProperty("result").GetString());
        Assert.True(summary.RootElement.GetProperty("closure").GetProperty("environmentDisposed").GetBoolean());
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        Assert.Equal(output.ToString().Trim(), File.ReadAllText(saved).Trim());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_failed_closure_disqualifies_an_otherwise_qualified_result_before_anything_is_printed()
    {
        var environment = new ScriptedEnvironment { OnDispose = () => throw new IOException("C:/Users/someone/secret") };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny, () => environment, output, error, CancellationToken.None);

        Assert.Equal(5, exit);
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Failed", summary.RootElement.GetProperty("result").GetString());
        Assert.Equal("EnvironmentDisposalFailed", summary.RootElement.GetProperty("code").GetString());
        Assert.False(summary.RootElement.GetProperty("closure").GetProperty("environmentDisposed").GetBoolean());
        Assert.DoesNotContain("someone", output.ToString(), StringComparison.Ordinal);
    }

    // QP-R1: every report fits inside the stated outer budget ---------------------------------------------------------------------

    [Fact]
    public async Task A_refusal_whose_error_sink_never_returns_still_returns_inside_the_stated_budget()
    {
        using var release = new ManualResetEventSlim();
        using var error = new HeldWriter(release);
        using var output = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [], _ => null, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(600) }, () => new ScriptedEnvironment(), output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        release.Set();

        Assert.Equal(LauncherEntry.RefusedExitCode, exit);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(1500), "the refusal report waited past the stated budget");
    }

    [Fact]
    public async Task An_expiry_whose_sinks_all_hold_returns_no_later_than_the_stated_budget_plus_scheduling_slack()
    {
        using var release = new ManualResetEventSlim();
        using var output = new HeldWriter(release);
        using var error = new HeldWriter(release);
        var environment = new ScriptedEnvironment
        {
            OnSubmit = (_, _) => new TaskCompletionSource<SubmissionResult>().Task,
        };
        var budget = TimeSpan.FromMilliseconds(1000);
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Overall = TimeSpan.FromSeconds(10), Outer = budget }, () => environment,
            output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        release.Set();

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exit);
        Assert.True(elapsed >= budget - TimeSpan.FromMilliseconds(250), "the entry returned before its work deadline");
        Assert.True(elapsed < budget + TimeSpan.FromMilliseconds(900), "the entry added post-budget waits");
    }

    [Fact]
    public async Task An_unexpected_failure_report_with_a_held_error_sink_also_stays_inside_the_budget()
    {
        using var release = new ManualResetEventSlim();
        using var error = new HeldWriter(release);
        using var output = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exit = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(800) },
            () => throw new InvalidOperationException("boom"), output, error, CancellationToken.None);
        var elapsed = clock.Elapsed;
        release.Set();

        Assert.Equal(LauncherEntry.UnexpectedFailureExitCode, exit);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(1800));
    }

    // Writers ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Holds its call until released, then really appends the text: an uncooperative sink that cannot be revoked.</summary>
    private sealed class AppendingWriter(Task release) : StringWriter
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteLineAsync(string? value)
        {
            await release;
            await base.WriteLineAsync(value);
            Finished.TrySetResult();
        }
    }

    private sealed class HeldWriter(ManualResetEventSlim released) : StringWriter
    {
        public override Task WriteLineAsync(string? value) => Task.Run(() => released.Wait());
    }

    private sealed class RecordingWriter(Action onPrint) : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            onPrint();
            return base.WriteLineAsync(value);
        }
    }
}
