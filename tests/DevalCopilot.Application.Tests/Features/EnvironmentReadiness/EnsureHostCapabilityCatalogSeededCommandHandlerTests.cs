using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// <c>HostCapabilitySnapshots</c> is a global, capability-keyed table with no per-test
/// disambiguating identifier (unlike Projects/Runs/Attempts, which use a fresh GUID per test),
/// so a shared database would let one test's seeded rows leak into another test's exact-count
/// assertion.
/// </summary>
public sealed class EnsureHostCapabilityCatalogSeededCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_seeds_exactly_one_row_per_catalog_capability_regardless_of_how_many_projects_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new EnsureHostCapabilityCatalogSeededCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityCatalog.All.Count, result.Value);

        var seeded = await dbContext.HostCapabilitySnapshots.ToListAsync();
        Assert.Equal(7, seeded.Count);
        Assert.All(seeded, snapshot => Assert.Equal(CapabilityProbeReason.NeverProbed, snapshot.ReasonCode));
        Assert.Equal(
            CapabilityCatalog.All.OrderBy(c => c).ToArray(),
            seeded.Select(s => s.Capability).OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task HandleAsync_never_re_seeds_or_resets_an_existing_row()
    {
        await using var dbContext = _fixture.CreateContext();
        var firstHandler = new EnsureHostCapabilityCatalogSeededCommandHandler(dbContext, new FixedTimeProvider(Now));
        await firstHandler.HandleAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var gitSnapshot = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.Git);
        gitSnapshot.MarkDispatched(Now);
        gitSnapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.43.0", Now, Now.AddMinutes(5));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var secondHandler = new EnsureHostCapabilityCatalogSeededCommandHandler(dbContext, new FixedTimeProvider(Now.AddHours(1)));
        var secondResult = await secondHandler.HandleAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(0, secondResult.Value);

        var allSnapshots = await dbContext.HostCapabilitySnapshots.ToListAsync();
        Assert.Equal(7, allSnapshots.Count);

        var persistedGit = allSnapshots.Single(s => s.Capability == Capability.Git);
        Assert.Equal(CapabilityProbeReason.None, persistedGit.ReasonCode);
        Assert.Equal("2.43.0", persistedGit.ObservedVersion);
    }
}
