using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RecordHostCapabilityProbeResult;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class RecordHostCapabilityProbeResultCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_records_a_successful_probe_clears_the_marker_and_advances_the_due_time()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = new ToolDiscoveryResult
        {
            Reason = CapabilityProbeReason.None,
            ResolvedExecutablePath = @"C:\Program Files\Git\cmd\git.exe",
            Version = "2.43.0",
        };

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityProbeReason.None, result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.None, persisted!.ReasonCode);
        Assert.Equal("2.43.0", persisted.ObservedVersion);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", persisted.ResolvedExecutablePath);
        Assert.Null(persisted.ProbeDispatchedAtUtc);
        Assert.True(persisted.NextProbeDueAtUtc > Now);
    }

    [Fact]
    public async Task HandleAsync_records_a_failed_probe_clears_the_marker_and_advances_the_due_time()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableNotFound };

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityProbeReason.ExecutableNotFound, result.Value);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.ExecutableNotFound, persisted!.ReasonCode);
        Assert.Null(persisted.ProbeDispatchedAtUtc);
        Assert.True(persisted.NextProbeDueAtUtc > Now);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_capability_was_not_dispatched()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableNotFound };

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_dispatched", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_capability_snapshot_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = new ToolDiscoveryResult { Reason = CapabilityProbeReason.ExecutableNotFound };

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_found", Assert.Single(result.Errors).Code);
    }
}
