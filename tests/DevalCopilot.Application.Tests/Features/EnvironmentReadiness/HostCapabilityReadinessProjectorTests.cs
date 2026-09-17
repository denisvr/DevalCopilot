using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

/// <summary>
/// The one unambiguous mapping from shared host evidence to the accepted UI vocabulary. Pure
/// function, no database — every branch of the approved mapping table is exercised directly.
/// </summary>
public sealed class HostCapabilityReadinessProjectorTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static HostCapabilitySnapshot CreateSnapshot(
        Capability capability, CapabilityProbeReason reason, DateTimeOffset nextProbeDueAtUtc)
    {
        var snapshot = HostCapabilitySnapshot.Seed(capability, BaseTime);
        if (reason == CapabilityProbeReason.NeverProbed)
        {
            return snapshot;
        }

        snapshot.MarkDispatched(BaseTime);
        if (reason == CapabilityProbeReason.None)
        {
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.43.0", BaseTime, nextProbeDueAtUtc);
        }
        else if (reason == CapabilityProbeReason.ProbeInterruptedByRestart)
        {
            // Only ever produced by restart reconciliation clearing a stuck marker — never a
            // valid RecordFailure reason.
            snapshot.ClearStuckDispatch(nextProbeDueAtUtc);
        }
        else
        {
            snapshot.RecordFailure(reason, nextProbeDueAtUtc);
        }

        return snapshot;
    }

    [Fact]
    public void A_required_capability_that_is_not_found_is_unavailable()
    {
        var snapshot = CreateSnapshot(Capability.Git, CapabilityProbeReason.ExecutableNotFound, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        var git = Assert.Single(result, r => r.Capability == Capability.Git);
        Assert.True(git.IsRequired);
        Assert.Equal(CapabilityDisplayStatus.Unavailable, git.DisplayStatus);
    }

    [Fact]
    public void Optional_docker_that_is_not_found_is_degraded_not_unavailable()
    {
        var snapshot = CreateSnapshot(Capability.Docker, CapabilityProbeReason.ExecutableNotFound, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        var docker = Assert.Single(result, r => r.Capability == Capability.Docker);
        Assert.False(docker.IsRequired);
        Assert.Equal(CapabilityDisplayStatus.Degraded, docker.DisplayStatus);
    }

    [Theory]
    [InlineData(CapabilityProbeReason.ProbeTimedOut)]
    [InlineData(CapabilityProbeReason.ExecutableInaccessible)]
    [InlineData(CapabilityProbeReason.ProbeInterruptedByRestart)]
    [InlineData(CapabilityProbeReason.LaunchTargetAmbiguous)]
    public void Timeout_inaccessible_interrupted_by_restart_and_ambiguous_launch_target_are_needs_attention(CapabilityProbeReason reason)
    {
        var snapshot = CreateSnapshot(Capability.Git, reason, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        Assert.Equal(CapabilityDisplayStatus.NeedsAttention, Assert.Single(result, r => r.Capability == Capability.Git).DisplayStatus);
    }

    [Fact]
    public void Unparseable_version_is_degraded()
    {
        var snapshot = CreateSnapshot(Capability.Node, CapabilityProbeReason.VersionProbeUnparseable, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        Assert.Equal(CapabilityDisplayStatus.Degraded, Assert.Single(result, r => r.Capability == Capability.Node).DisplayStatus);
    }

    [Fact]
    public void A_successful_parsed_probe_is_ready()
    {
        var snapshot = CreateSnapshot(Capability.Git, CapabilityProbeReason.None, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        var git = Assert.Single(result, r => r.Capability == Capability.Git);
        Assert.Equal(CapabilityDisplayStatus.Ready, git.DisplayStatus);
        Assert.Equal("2.43.0", git.Version);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", git.ResolvedExecutablePath);
    }

    [Fact]
    public void Never_probed_has_no_display_status_and_is_not_a_fifth_state()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        var git = Assert.Single(result, r => r.Capability == Capability.Git);
        Assert.Null(git.DisplayStatus);
        Assert.Equal(CapabilityProbeReason.NeverProbed, git.ReasonCode);
    }

    [Fact]
    public void A_capability_with_no_snapshot_row_at_all_is_presented_identically_to_never_probed()
    {
        var result = HostCapabilityReadinessProjector.Project([], BaseTime);

        Assert.Equal(CapabilityCatalog.All.Count, result.Count);
        Assert.All(result, capability =>
        {
            Assert.Null(capability.DisplayStatus);
            Assert.Equal(CapabilityProbeReason.NeverProbed, capability.ReasonCode);
            Assert.False(capability.IsStale);
        });
    }

    [Fact]
    public void Evidence_well_within_its_due_time_is_not_stale()
    {
        var snapshot = CreateSnapshot(Capability.Git, CapabilityProbeReason.None, BaseTime.AddMinutes(5));

        var result = HostCapabilityReadinessProjector.Project([snapshot], BaseTime);

        Assert.False(Assert.Single(result, r => r.Capability == Capability.Git).IsStale);
    }

    [Fact]
    public void Evidence_overdue_beyond_the_staleness_grace_is_stale_but_keeps_its_mapped_status()
    {
        var snapshot = CreateSnapshot(Capability.Git, CapabilityProbeReason.None, BaseTime);
        var farPastDue = BaseTime.AddHours(1);

        var result = HostCapabilityReadinessProjector.Project([snapshot], farPastDue);

        var git = Assert.Single(result, r => r.Capability == Capability.Git);
        Assert.True(git.IsStale);
        Assert.Equal(CapabilityDisplayStatus.Ready, git.DisplayStatus);
    }
}
