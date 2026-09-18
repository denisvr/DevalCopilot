using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// <see cref="HostCapabilitySnapshot"/> is keyed one row per <see cref="Capability"/> across the
/// whole host, so a shared database would collide across test methods that each seed a
/// <see cref="Capability.CodexCli"/> snapshot.
/// </summary>
public sealed class GetCodexLaunchTargetQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_returns_null_when_no_codex_snapshot_exists()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_codex_snapshot_was_never_probed()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.CodexCli, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_last_probe_failed()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_last_probe_failed_after_an_earlier_success()
    {
        // Last-known-good evidence is retained by HostCapabilitySnapshot across a transient
        // failure, but this query must still refuse a stale success — only the *current*
        // ReasonCode governs eligibility, never merely the presence of old evidence fields.
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        snapshot.MarkDispatched(Now.AddMinutes(5));
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(10));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_the_launch_target_for_a_direct_executable_success()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(@"C:\safe\codex.exe", result.ExecutablePath);
        Assert.Null(result.ScriptPath);
    }

    [Fact]
    public async Task HandleAsync_returns_the_launch_target_for_a_node_script_success()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.NodeScript, @"C:\safe\node.exe", @"C:\safe\codex.js", "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(@"C:\safe\node.exe", result.ExecutablePath);
        Assert.Equal(@"C:\safe\codex.js", result.ScriptPath);
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

        var handler = new GetCodexLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCodexLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }
}
