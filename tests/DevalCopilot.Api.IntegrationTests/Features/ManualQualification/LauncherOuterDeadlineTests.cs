using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The whole sequence's outer bound: a session still running when it elapses is reported as unproven, and nothing is
/// stopped, cleaned or removed from the launcher's side.</summary>
public sealed class LauncherOuterDeadlineTests : QualificationTestBase
{
    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    [Fact]
    public async Task A_session_running_past_the_outer_bound_is_reported_exit_eight_unproven_with_nothing_stopped_or_cleaned()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                return SubmissionResult.Accepted(Guid.NewGuid());
            },
            AdmissionFor = _ => new ProcessAdmissionFacts(new Dictionary<string, int> { ["role.codex.exec"] = 1 }, new Dictionary<string, int>()),
        };
        var limits = Tiny with { Overall = TimeSpan.FromSeconds(3), Outer = TimeSpan.FromMilliseconds(300) };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, limits, () => environment, output, error, CancellationToken.None);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.Equal(0, environment.Count("stop"));
        Assert.Equal(0, environment.Count("cleanup"));
        using var summary = JsonDocument.Parse(output.ToString());
        var root = summary.RootElement;
        Assert.Equal("Failed", root.GetProperty("result").GetString());
        Assert.Equal("OuterDeadlineExceeded", root.GetProperty("code").GetString());
        Assert.False(root.GetProperty("shutdown").GetProperty("hostStopped").GetBoolean());
        Assert.False(root.GetProperty("shutdown").GetProperty("childProcessesProvenStopped").GetBoolean());
        Assert.False(root.GetProperty("cleanup").GetProperty("ownedRootRemoved").GetBoolean());
        Assert.Equal(1, root.GetProperty("processAdmission").GetProperty("admitted").GetProperty("role.codex.exec").GetInt32());
        Assert.False(root.TryGetProperty("planner", out _));
        Assert.False(root.TryGetProperty("source", out _));
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        Assert.Equal(output.ToString().Trim(), File.ReadAllText(saved).Trim());
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
    }

    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task A_synchronous_first_snapshot_inside_the_session_cannot_hold_the_entry_past_its_bound()
    {
        // The reviewer's control: only the first synchronous source snapshot blocks (400 ms) against a 40 ms budget.
        var environment = new ScriptedEnvironment
        {
            SourceAt = count =>
            {
                if (count == 1)
                {
                    Thread.Sleep(400);
                }

                return QualificationReadings.Snapshot();
            },
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(40) }, () => environment,
            output, error, CancellationToken.None);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.True(clock.Elapsed < Elapsed, "the entry returned only after the blocked snapshot");
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, environment.Count("stop"));
        Assert.Equal(0, environment.Count("cleanup"));
    }

    [Fact]
    public async Task A_blocking_environment_factory_is_inside_the_bound_and_creates_nothing_afterwards()
    {
        using var release = new ManualResetEventSlim();
        var environment = new ScriptedEnvironment();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag],
            Confirmed,
            Ledger,
            Tiny with { Outer = TimeSpan.FromMilliseconds(50) },
            () =>
            {
                release.Wait();
                return environment;
            },
            output,
            error,
            CancellationToken.None);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.True(clock.Elapsed < Elapsed);
        release.Set();
        await Task.Delay(300);
        Assert.Empty(environment.Calls);
        Assert.False(Ledger.IsSpent());
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("OuterDeadlineExceeded", summary.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, summary.RootElement.GetProperty("processAdmission").GetProperty("totalAdmitted").GetInt32());
    }

    [Fact]
    public async Task A_held_report_sink_cannot_hold_the_entry_and_the_late_release_saves_the_report_or_disposes_nothing_more()
    {
        var environment = new ScriptedEnvironment();
        using var released = new ManualResetEventSlim();
        using var output = new HeldWriter(released);
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Outer = TimeSpan.FromMilliseconds(400) }, () => environment,
            output, error, CancellationToken.None);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.True(clock.Elapsed < Elapsed);
        Assert.Contains("unproven", error.ToString(), StringComparison.Ordinal);
        released.Set();
        await Task.Delay(400);
        // The closure finished before the print (so it ran once), and the only persisted summary is the unproven verdict: the late
        // release neither saves the report nor disposes again.
        var saved = Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        using var verdict = JsonDocument.Parse(File.ReadAllText(saved));
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, environment.Count("dispose"));
    }

    [Fact]
    public async Task A_detached_continuation_after_the_bound_consumes_nothing_starts_nothing_and_rewrites_nothing()
    {
        var environment = new ScriptedEnvironment
        {
            OnSubmit = async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(900));
                return SubmissionResult.Accepted(Guid.NewGuid());
            },
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny with { Overall = TimeSpan.FromSeconds(5), Outer = TimeSpan.FromMilliseconds(300) },
            () => environment, output, error, CancellationToken.None);
        var summaryBefore = output.ToString();
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(0, environment.Count("submit.Reviewer"));
        Assert.Equal(0, environment.Count("stop"));
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal(0, environment.Count("dispose"));
        Assert.True(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Equal(summaryBefore, output.ToString());
        Assert.Single(Directory.GetFiles(LedgerDirectory, "summary-*.json"));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_missing_approval_refuses_the_entry_before_any_environment_effect()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, Tiny, () => throw new EnvironmentRefusedException("ApprovedLaunchesRequired"),
            output, error, CancellationToken.None);

        Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
        Assert.Contains("Refused: ApprovedLaunchesRequired.", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(Ledger.IsSpent());
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    private sealed class HeldWriter(ManualResetEventSlim released) : StringWriter
    {
        public override Task WriteLineAsync(string? value) => Task.Run(() => released.Wait());
    }

    [Fact]
    public async Task A_session_that_finishes_inside_the_outer_bound_reports_normally()
    {
        var environment = new ScriptedEnvironment();
        var limits = Tiny with { Outer = TimeSpan.FromSeconds(15) };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag], Confirmed, Ledger, limits, () => environment, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Qualified", summary.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public void The_production_limits_are_the_proposed_envelope()
    {
        var production = QualificationLimits.Production;

        Assert.Equal(TimeSpan.FromMinutes(12), production.Stage);
        Assert.Equal(TimeSpan.FromMinutes(32), production.Overall);
        Assert.Equal(TimeSpan.FromSeconds(60), production.Shutdown);
        Assert.Equal(TimeSpan.FromMinutes(35), production.Outer);
        Assert.True(production.Outer > production.Overall + production.Shutdown);
        Assert.Equal(TimeSpan.FromSeconds(30), StatusLimits.Production.Execution);
        Assert.Equal(TimeSpan.FromMinutes(2), StatusLimits.Production.Outer);
        Assert.True(StatusLimits.Production.Sequence < StatusLimits.Production.Outer);
    }
}
