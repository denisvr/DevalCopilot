using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

public sealed class GetProviderRuntimePreflightQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_reads_the_two_shared_provider_snapshots_without_creating_or_reprobing_evidence()
    {
        await using var dbContext = _fixture.CreateContext();
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codex.MarkDispatched(Now);
        codex.RecordSuccess(@"C:\safe\codex.exe", "1.2.3", Now, Now.AddMinutes(5));

        dbContext.HostCapabilitySnapshots.Add(codex);
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProviderRuntimePreflightQueryHandler(dbContext, new FixedTimeProvider(Now));
        var runtimes = await handler.HandleAsync(new GetProviderRuntimePreflightQuery(), CancellationToken.None);

        Assert.Equal([ProviderRuntime.Codex, ProviderRuntime.ClaudeCode], runtimes.Select(runtime => runtime.Provider));
        Assert.Equal("1.2.3", runtimes[0].ObservedVersion);
        Assert.Equal(ProviderRuntimeStatus.Checking, runtimes[1].Status);
        Assert.Equal(2, await dbContext.HostCapabilitySnapshots.CountAsync(CancellationToken.None));
    }
}
