using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.MarkHostCapabilityProbeDispatched;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class MarkHostCapabilityProbeDispatchedCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_marks_a_never_probed_capability_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkHostCapabilityProbeDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkHostCapabilityProbeDispatchedCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value);

        var snapshot = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(Now, snapshot!.ProbeDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_capability()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkHostCapabilityProbeDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(new MarkHostCapabilityProbeDispatchedCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.already_dispatched", Assert.Single(result.Errors).Code);
        Assert.Equal(Now, snapshot.ProbeDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_capability_snapshot_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new MarkHostCapabilityProbeDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkHostCapabilityProbeDispatchedCommand(Capability.Git), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_found", Assert.Single(result.Errors).Code);
    }
}
