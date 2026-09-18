using DevalCopilot.Application.Features.Runs.Queries.GetClaudeLaunchTarget;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Mirrors <c>GetCodexLaunchTargetQueryHandlerTests</c> exactly, adapted for the stricter Claude
/// contract: only a <see cref="CapabilityLaunchKind.DirectExecutable"/> success with no script
/// path is ever a valid launch target — Claude is never treated as a Node script in this slice.
/// Owns a fresh database per test method for the same
/// <see cref="Capability"/>-keyed-snapshot reason as its template.
/// </summary>
public sealed class GetClaudeLaunchTargetQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task HandleAsync_returns_null_when_no_claude_snapshot_exists()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_claude_snapshot_was_never_probed()
    {
        await using var dbContext = _fixture.CreateContext();
        dbContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_last_probe_failed()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_null_when_the_last_probe_failed_after_an_earlier_success()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        snapshot.MarkDispatched(Now.AddMinutes(5));
        snapshot.RecordFailure(CapabilityProbeReason.ExecutableNotFound, Now.AddMinutes(10));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_returns_the_launch_target_for_a_direct_executable_success()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(@"C:\safe\claude.exe", result.ExecutablePath);
    }

    /// <summary>
    /// The behavior distinguishing this handler from <c>GetCodexLaunchTargetQueryHandler</c>:
    /// Codex accepts a Node-script resolution and returns both paths, but Claude is never treated
    /// as a Node script — a snapshot observed with <see cref="CapabilityLaunchKind.NodeScript"/>
    /// is rejected outright rather than mapped to any launch target.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_null_for_a_node_script_resolution_rather_than_treating_claude_as_a_script()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.NodeScript, @"C:\safe\node.exe", @"C:\safe\claude.js", "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_ignores_a_snapshot_for_a_different_capability()
    {
        await using var dbContext = _fixture.CreateContext();
        var codexSnapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codexSnapshot.MarkDispatched(Now);
        codexSnapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.0.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(codexSnapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetClaudeLaunchTargetQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetClaudeLaunchTargetQuery(), CancellationToken.None);

        Assert.Null(result);
    }
}
