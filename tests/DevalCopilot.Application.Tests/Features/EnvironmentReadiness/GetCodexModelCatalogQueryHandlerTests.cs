using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// <see cref="HostCapabilitySnapshot"/> is keyed one row per <see cref="Capability"/> across the
/// whole host, so a shared database would collide across test methods that each seed a
/// <see cref="Capability.CodexCli"/> snapshot. Mirrors <c>GetCodexAccountAllowanceQueryHandlerTests</c>.
/// </summary>
public sealed class GetCodexModelCatalogQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeCodexModelCatalogAdapter : ICodexModelCatalogAdapter
    {
        public (string ExecutablePath, string? ScriptPath)? CapturedLaunchTarget { get; private set; }
        public CodexModelCatalogObservation Observation { get; set; } = CodexModelCatalogObservation.Unknown;
        public int CallCount { get; private set; }

        public Task<CodexModelCatalogObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken)
        {
            CallCount++;
            CapturedLaunchTarget = (executablePath, scriptPath);
            return Task.FromResult(Observation);
        }
    }

    [Fact]
    public async Task HandleAsync_reports_unknown_without_invoking_the_adapter_when_no_codex_snapshot_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        var adapter = new FakeCodexModelCatalogAdapter();

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        var result = await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal(CodexModelCatalogStatus.Unknown, result.Status);
        Assert.Null(result.RetrievedAtUtc);
        Assert.Empty(result.Models);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task HandleAsync_reports_unknown_without_invoking_the_adapter_when_the_last_probe_failed()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter();

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        var result = await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal(CodexModelCatalogStatus.Unknown, result.Status);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task HandleAsync_reports_unknown_when_a_vetted_target_exists_but_the_adapter_reports_no_observation()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = CodexModelCatalogObservation.Unknown };

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        var result = await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal(CodexModelCatalogStatus.Unknown, result.Status);
        Assert.Equal(1, adapter.CallCount);
        Assert.Equal((@"C:\safe\codex.exe", (string?)null), adapter.CapturedLaunchTarget);
    }

    [Fact]
    public async Task HandleAsync_maps_an_observed_catalog_from_a_direct_executable_vetted_target()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var model = new CodexModelCatalogEntry("gpt-6-sol", "GPT-6 Sol", ["medium", "high"], "medium");
        var retrievedAt = new DateTimeOffset(2026, 9, 28, 18, 0, 5, TimeSpan.Zero);
        var adapter = new FakeCodexModelCatalogAdapter
        {
            Observation = new CodexModelCatalogObservation(true, retrievedAt, [model]),
        };

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        var result = await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal(CodexModelCatalogStatus.Observed, result.Status);
        Assert.Equal(retrievedAt, result.RetrievedAtUtc);
        var resultModel = Assert.Single(result.Models);
        Assert.Equal(model, resultModel);
    }

    [Fact]
    public async Task HandleAsync_passes_the_node_script_shape_to_the_adapter_when_that_is_the_vetted_target()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.NodeScript, @"C:\safe\node.exe", @"C:\safe\codex.js", "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter();

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal((@"C:\safe\node.exe", (string?)@"C:\safe\codex.js"), adapter.CapturedLaunchTarget);
    }

    [Fact]
    public async Task HandleAsync_ignores_a_snapshot_for_a_different_capability()
    {
        await using var dbContext = _fixture.CreateContext();
        var claudeSnapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        claudeSnapshot.MarkDispatched(Now);
        claudeSnapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claudeSnapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter();

        var handler = new GetCodexModelCatalogQueryHandler(dbContext, adapter);
        var result = await handler.HandleAsync(new GetCodexModelCatalogQuery(), CancellationToken.None);

        Assert.Equal(CodexModelCatalogStatus.Unknown, result.Status);
        Assert.Equal(0, adapter.CallCount);
    }
}
