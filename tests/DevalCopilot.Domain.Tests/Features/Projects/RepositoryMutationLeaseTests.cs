using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class RepositoryMutationLeaseTests
{
    private static RepositoryMutationLease CreateActive() =>
        RepositoryMutationLease.Acquire(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1UL, new byte[16], DateTimeOffset.UtcNow);

    [Fact]
    public void Acquire_starts_active_with_no_release_or_supersede_timestamp()
    {
        var lease = CreateActive();

        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Null(lease.ReleasedAtUtc);
        Assert.Null(lease.SupersededAtUtc);
    }

    [Fact]
    public void Release_is_terminal_and_records_when_it_happened()
    {
        var lease = CreateActive();
        var now = DateTimeOffset.UtcNow;

        lease.Release(now);

        Assert.Equal(LeaseStatus.Released, lease.Status);
        Assert.Equal(now, lease.ReleasedAtUtc);
        Assert.Throws<InvalidOperationException>(() => lease.Release(now));
        Assert.Throws<InvalidOperationException>(() => lease.Supersede(now));
    }

    [Fact]
    public void Supersede_is_terminal_and_records_when_it_happened()
    {
        var lease = CreateActive();
        var now = DateTimeOffset.UtcNow;

        lease.Supersede(now);

        Assert.Equal(LeaseStatus.Superseded, lease.Status);
        Assert.Equal(now, lease.SupersededAtUtc);
        Assert.Throws<InvalidOperationException>(() => lease.Supersede(now));
        Assert.Throws<InvalidOperationException>(() => lease.Release(now));
    }
}
