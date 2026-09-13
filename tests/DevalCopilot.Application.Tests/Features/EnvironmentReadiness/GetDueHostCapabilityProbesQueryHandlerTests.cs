using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetDueHostCapabilityProbes;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class GetDueHostCapabilityProbesQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_returns_only_due_and_undispatched_capabilities()
    {
        await using var dbContext = _fixture.CreateContext();

        var due = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        var notYetDue = HostCapabilitySnapshot.Seed(Capability.Node, Now.AddMinutes(5));
        var dueButDispatched = HostCapabilitySnapshot.Seed(Capability.Docker, Now);
        dueButDispatched.MarkDispatched(Now);

        dbContext.HostCapabilitySnapshots.AddRange(due, notYetDue, dueButDispatched);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetDueHostCapabilityProbesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetDueHostCapabilityProbesQuery(), CancellationToken.None);

        Assert.Equal([Capability.Git], result);
    }

    [Fact]
    public async Task HandleAsync_returns_nothing_when_no_capability_is_due()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, Now.AddMinutes(5)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetDueHostCapabilityProbesQueryHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new GetDueHostCapabilityProbesQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
