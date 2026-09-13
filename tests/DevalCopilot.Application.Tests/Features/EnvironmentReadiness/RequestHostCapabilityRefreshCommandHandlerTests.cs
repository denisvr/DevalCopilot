using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RequestHostCapabilityRefresh;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class RequestHostCapabilityRefreshCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_pulls_a_future_due_time_forward_to_now()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, Now.AddMinutes(5)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RequestHostCapabilityRefreshCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new RequestHostCapabilityRefreshCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(Now, persisted!.NextProbeDueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_is_a_no_op_while_a_probe_is_already_in_flight()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now.AddMinutes(5));
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RequestHostCapabilityRefreshCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new RequestHostCapabilityRefreshCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(5), result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(Now.AddMinutes(5), persisted!.NextProbeDueAtUtc);
        Assert.Equal(Now, persisted.ProbeDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_capability_snapshot_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new RequestHostCapabilityRefreshCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new RequestHostCapabilityRefreshCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_found", Assert.Single(result.Errors).Code);
    }
}
