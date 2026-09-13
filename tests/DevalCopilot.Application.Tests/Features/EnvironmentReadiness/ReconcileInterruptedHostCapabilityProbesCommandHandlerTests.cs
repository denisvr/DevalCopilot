using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.ReconcileInterruptedHostCapabilityProbes;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class ReconcileInterruptedHostCapabilityProbesCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_clears_a_stuck_dispatch_marker_and_makes_it_immediately_eligible()
    {
        await using var dbContext = _fixture.CreateContext();
        var stuck = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        stuck.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(stuck);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var restartTime = Now.AddHours(1);
        var handler = new ReconcileInterruptedHostCapabilityProbesCommandHandler(dbContext, new FixedTimeProvider(restartTime));
        var result = await handler.HandleAsync(new ReconcileInterruptedHostCapabilityProbesCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.ProbeInterruptedByRestart, persisted!.ReasonCode);
        Assert.Null(persisted.ProbeDispatchedAtUtc);
        Assert.Equal(restartTime, persisted.NextProbeDueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_never_touches_a_capability_that_is_not_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedHostCapabilityProbesCommandHandler(dbContext, new FixedTimeProvider(Now.AddHours(1)));
        var result = await handler.HandleAsync(new ReconcileInterruptedHostCapabilityProbesCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.NeverProbed, persisted!.ReasonCode);
        Assert.Equal(Now, persisted.NextProbeDueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_returns_zero_when_nothing_is_stuck()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new ReconcileInterruptedHostCapabilityProbesCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedHostCapabilityProbesCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
