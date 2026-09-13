using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.EnvironmentReadiness;

public sealed class HostCapabilitySnapshotTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Seed_creates_a_never_probed_snapshot_immediately_due()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        Assert.Equal(Capability.Git, snapshot.Capability);
        Assert.Equal(CapabilityProbeReason.NeverProbed, snapshot.ReasonCode);
        Assert.Equal(BaseTime, snapshot.NextProbeDueAtUtc);
        Assert.Null(snapshot.ProbeDispatchedAtUtc);
        Assert.Null(snapshot.ResolvedExecutablePath);
        Assert.Null(snapshot.ObservedVersion);
        Assert.Null(snapshot.EvidenceObservedAtUtc);
    }

    [Fact]
    public void MarkDispatched_sets_the_marker()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        snapshot.MarkDispatched(BaseTime);

        Assert.Equal(BaseTime, snapshot.ProbeDispatchedAtUtc);
    }

    [Fact]
    public void MarkDispatched_twice_throws()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);

        Assert.Throws<InvalidOperationException>(() => snapshot.MarkDispatched(BaseTime));
    }

    [Fact]
    public void RecordSuccess_before_dispatch_throws()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        Assert.Throws<InvalidOperationException>(
            () => snapshot.RecordSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0", BaseTime, BaseTime.AddMinutes(5)));
    }

    [Fact]
    public void RecordSuccess_after_dispatch_records_evidence_clears_the_marker_and_advances_the_due_time()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);

        var nextDue = BaseTime.AddMinutes(5);
        snapshot.RecordSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0", BaseTime, nextDue);

        Assert.Equal(CapabilityProbeReason.None, snapshot.ReasonCode);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", snapshot.ResolvedExecutablePath);
        Assert.Equal("2.43.0", snapshot.ObservedVersion);
        Assert.Equal(BaseTime, snapshot.EvidenceObservedAtUtc);
        Assert.Null(snapshot.ProbeDispatchedAtUtc);
        Assert.Equal(nextDue, snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void RecordFailure_before_dispatch_throws()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        Assert.Throws<InvalidOperationException>(
            () => snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, BaseTime.AddMinutes(5)));
    }

    [Theory]
    [InlineData(CapabilityProbeReason.None)]
    [InlineData(CapabilityProbeReason.NeverProbed)]
    [InlineData(CapabilityProbeReason.ProbeInterruptedByRestart)]
    public void RecordFailure_rejects_reasons_that_are_not_failures(CapabilityProbeReason invalidReason)
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.RecordFailure(invalidReason, BaseTime.AddMinutes(5)));
    }

    [Fact]
    public void RecordFailure_after_dispatch_sets_the_reason_clears_the_marker_and_advances_the_due_time()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);

        var nextDue = BaseTime.AddMinutes(5);
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, nextDue);

        Assert.Equal(CapabilityProbeReason.ExecutableNotFound, snapshot.ReasonCode);
        Assert.Null(snapshot.ProbeDispatchedAtUtc);
        Assert.Equal(nextDue, snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void RecordFailure_never_erases_last_known_good_evidence_from_an_earlier_success()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);
        snapshot.RecordSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0", BaseTime, BaseTime.AddMinutes(5));

        snapshot.MarkDispatched(BaseTime.AddMinutes(5));
        snapshot.RecordFailure(CapabilityProbeReason.ProbeTimedOut, BaseTime.AddMinutes(10));

        Assert.Equal(CapabilityProbeReason.ProbeTimedOut, snapshot.ReasonCode);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", snapshot.ResolvedExecutablePath);
        Assert.Equal("2.43.0", snapshot.ObservedVersion);
        Assert.Equal(BaseTime, snapshot.EvidenceObservedAtUtc);
    }

    [Fact]
    public void ClearStuckDispatch_is_a_no_op_when_nothing_is_dispatched()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        snapshot.ClearStuckDispatch(BaseTime.AddMinutes(1));

        Assert.Equal(CapabilityProbeReason.NeverProbed, snapshot.ReasonCode);
        Assert.Equal(BaseTime, snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void ClearStuckDispatch_clears_a_stuck_marker_and_makes_the_capability_immediately_eligible()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);
        snapshot.MarkDispatched(BaseTime);

        var restartTime = BaseTime.AddHours(1);
        snapshot.ClearStuckDispatch(restartTime);

        Assert.Equal(CapabilityProbeReason.ProbeInterruptedByRestart, snapshot.ReasonCode);
        Assert.Null(snapshot.ProbeDispatchedAtUtc);
        Assert.Equal(restartTime, snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void PullProbeDueForward_is_a_no_op_while_a_probe_is_already_in_flight()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime.AddMinutes(10));
        snapshot.MarkDispatched(BaseTime);

        snapshot.PullProbeDueForward(BaseTime);

        Assert.Equal(BaseTime.AddMinutes(10), snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void PullProbeDueForward_pulls_a_future_due_time_forward_to_now()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime.AddMinutes(10));

        snapshot.PullProbeDueForward(BaseTime);

        Assert.Equal(BaseTime, snapshot.NextProbeDueAtUtc);
    }

    [Fact]
    public void PullProbeDueForward_never_pushes_the_due_time_later()
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, BaseTime);

        snapshot.PullProbeDueForward(BaseTime.AddMinutes(10));

        Assert.Equal(BaseTime, snapshot.NextProbeDueAtUtc);
    }
}
