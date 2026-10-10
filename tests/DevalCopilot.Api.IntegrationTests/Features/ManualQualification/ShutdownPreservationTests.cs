using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Anything short of a proven shutdown preserves the root, whatever else succeeded, and the summary says why.</summary>
public sealed class ShutdownPreservationTests : QualificationTestBase
{
    [Theory]
    [InlineData("EnumerationFailed")]
    [InlineData("LeftoverAlive")]
    [InlineData("OriginUnresolved")]
    [InlineData("IdentityUnavailable")]
    public async Task An_unproven_child_shutdown_preserves_the_root_and_reports_its_reason(string reason)
    {
        var environment = new ScriptedEnvironment { Shutdown = new ShutdownReport(true, false, reason) };

        var report = await RunAsync(environment);

        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal("ShutdownUnproven", report.Cleanup!.Reason);
        Assert.False(report.Cleanup.Removed);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var shutdown = summary.RootElement.GetProperty("shutdown");
        Assert.False(shutdown.GetProperty("childProcessesProvenStopped").GetBoolean());
        Assert.Equal(reason, shutdown.GetProperty("childProcessProof").GetString());
    }

    [Fact]
    public async Task A_proof_from_an_unreadable_baseline_parent_preserves_the_root_through_the_session()
    {
        var epoch = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var explorer = new ProcessEntry(10, 1, "explorer.exe", epoch);
        var launcher = new ProcessEntry(100, 10, "ManualQualification.exe", epoch.AddMinutes(1));
        var parent = new ProcessEntry(700, 10, "preexisting-parent.exe");
        var helper = new ProcessEntry(701, 700, "helper.exe", epoch.AddMinutes(9));
        var watch = ChildProcessWatch.Begin(new ScriptedProcessTable([explorer, launcher, parent], [explorer, launcher, parent, helper]), 100);
        var proof = await watch.ProveAsync(TimeSpan.Zero);
        var environment = new ScriptedEnvironment { Shutdown = new ShutdownReport(true, proof.Proven, proof.Reason) };

        var report = await RunAsync(environment);

        Assert.False(proof.Proven);
        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal("ShutdownUnproven", report.Cleanup!.Reason);
        Assert.False(report.Cleanup.Removed);
        Assert.Equal(6, report.ExitCode);
    }

    [Fact]
    public async Task A_host_that_did_not_stop_in_its_bound_preserves_the_root()
    {
        var environment = new ScriptedEnvironment { Shutdown = new ShutdownReport(false, true) };

        var report = await RunAsync(environment);

        Assert.Equal(0, environment.Count("cleanup"));
        Assert.Equal("ShutdownUnproven", report.Cleanup!.Reason);
    }

    [Fact]
    public async Task A_proven_shutdown_removes_the_root_only_after_the_final_observation()
    {
        var environment = new ScriptedEnvironment { Shutdown = new ShutdownReport(true, true, "Proven") };

        var report = await RunAsync(environment);

        var calls = environment.Calls.ToList();
        Assert.True(report.Cleanup!.Removed);
        Assert.True(calls.IndexOf("stop") < calls.FindLastIndex(call => call == "snapshot.workspace"));
        Assert.True(calls.FindLastIndex(call => call == "snapshot.workspace") < calls.IndexOf("cleanup"));
    }
}
