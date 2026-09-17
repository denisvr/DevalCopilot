using System.Reflection;
using System.Runtime.CompilerServices;
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
        var outcome = ToolDiscoveryResult.DirectExecutableSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0");

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
    public async Task HandleAsync_records_a_node_script_success_with_its_script_path_alongside_the_node_executable()
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = ToolDiscoveryResult.NodeScriptSuccess(
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Users\dev\AppData\Roaming\npm\node_modules\@openai\codex\bin\codex.js",
            "1.0.0");

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.CodexCli, outcome), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.CodexCli);
        Assert.Equal(CapabilityLaunchKind.NodeScript, persisted!.LaunchKind);
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", persisted.ResolvedExecutablePath);
        Assert.Equal(
            @"C:\Users\dev\AppData\Roaming\npm\node_modules\@openai\codex\bin\codex.js", persisted.ResolvedScriptPath);
        Assert.Equal("1.0.0", persisted.ObservedVersion);
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
        var outcome = ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableNotFound);

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
        var outcome = ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableNotFound);

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_dispatched", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_capability_snapshot_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var outcome = ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableNotFound);

        var result = await handler.HandleAsync(new RecordHostCapabilityProbeResultCommand(Capability.Git, outcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.not_found", Assert.Single(result.Errors).Code);
    }

    // ToolDiscoveryResult's own factories (see ToolDiscoveryResultConstructionTests) already make
    // every malformed combination below impossible to construct through public API — no real
    // IToolDiscoveryAdapter implementation can hand the handler one. These tests prove the
    // handler's own defense-in-depth still holds even if that type-level guarantee were ever
    // bypassed (for example, by a future adapter implementation with a bug), by using reflection
    // to force a malformed instance into existence — something no legitimate caller can do.
    [Theory]
    [MemberData(nameof(MalformedSuccessResults))]
    public async Task HandleAsync_fails_safely_without_mutating_the_snapshot_for_a_malformed_success_result(
        ToolDiscoveryResult malformedOutcome)
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new RecordHostCapabilityProbeResultCommand(Capability.Git, malformedOutcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.invalid_probe_result", Assert.Single(result.Errors).Code);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.NeverProbed, persisted!.ReasonCode);
        Assert.NotNull(persisted.ProbeDispatchedAtUtc);
        Assert.Null(persisted.ResolvedExecutablePath);
    }

    [Theory]
    [InlineData(CapabilityProbeReason.None)]
    [InlineData(CapabilityProbeReason.NeverProbed)]
    [InlineData(CapabilityProbeReason.ProbeInterruptedByRestart)]
    [InlineData((CapabilityProbeReason)999)]
    public async Task HandleAsync_fails_safely_without_mutating_the_snapshot_for_an_invalid_failure_reason(
        CapabilityProbeReason invalidReason)
    {
        await using var dbContext = _fixture.CreateContext();
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordHostCapabilityProbeResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var malformedOutcome = CreateMalformed(invalidReason);

        var result = await handler.HandleAsync(
            new RecordHostCapabilityProbeResultCommand(Capability.Git, malformedOutcome), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("host_capabilities.invalid_probe_result", Assert.Single(result.Errors).Code);

        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal(CapabilityProbeReason.NeverProbed, persisted!.ReasonCode);
        Assert.NotNull(persisted.ProbeDispatchedAtUtc);
    }

    public static TheoryData<ToolDiscoveryResult> MalformedSuccessResults() => new()
    {
        // Reason = None but no launch kind at all.
        CreateMalformed(CapabilityProbeReason.None),
        // Reason = None, DirectExecutable, but no executable path.
        CreateMalformed(CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable),
        // Reason = None, DirectExecutable, executable path present, but version missing.
        CreateMalformed(
            CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable, resolvedExecutablePath: @"C:\git.exe"),
        // Reason = None, DirectExecutable, but a script path present too (impossible combination).
        CreateMalformed(
            CapabilityProbeReason.None,
            CapabilityLaunchKind.DirectExecutable,
            resolvedExecutablePath: @"C:\git.exe",
            resolvedScriptPath: @"C:\unexpected\script.js",
            version: "1.0.0"),
        // Reason = None, NodeScript, node path present, but no script path.
        CreateMalformed(
            CapabilityProbeReason.None,
            CapabilityLaunchKind.NodeScript,
            resolvedExecutablePath: @"C:\node.exe",
            version: "1.0.0"),
        // Reason = None, an undefined launch kind, otherwise a plausible-looking success.
        CreateMalformed(
            CapabilityProbeReason.None,
            (CapabilityLaunchKind)999,
            resolvedExecutablePath: @"C:\git.exe",
            version: "2.43.0"),
        // Reason = None, DirectExecutable, but the "executable path" is relative, not absolute.
        CreateMalformed(
            CapabilityProbeReason.None,
            CapabilityLaunchKind.DirectExecutable,
            resolvedExecutablePath: "git.exe",
            version: "2.43.0"),
        // Reason = None, NodeScript, absolute script path, but a relative node executable path.
        CreateMalformed(
            CapabilityProbeReason.None,
            CapabilityLaunchKind.NodeScript,
            resolvedExecutablePath: "node.exe",
            resolvedScriptPath: @"C:\npm\node_modules\@openai\codex\bin\codex.js",
            version: "1.0.0"),
        // Reason = None, NodeScript, absolute node path, but a relative script path.
        CreateMalformed(
            CapabilityProbeReason.None,
            CapabilityLaunchKind.NodeScript,
            resolvedExecutablePath: @"C:\Program Files\nodejs\node.exe",
            resolvedScriptPath: "codex.js",
            version: "1.0.0"),
    };

    /// <summary>
    /// Bypasses every <see cref="ToolDiscoveryResult"/> factory using reflection, to construct a
    /// combination the public API can never produce — proving the handler's own defensive check,
    /// not merely the type's constructor validation.
    /// </summary>
    private static ToolDiscoveryResult CreateMalformed(
        CapabilityProbeReason reason,
        CapabilityLaunchKind? launchKind = null,
        string? resolvedExecutablePath = null,
        string? resolvedScriptPath = null,
        string? version = null)
    {
        var instance = (ToolDiscoveryResult)RuntimeHelpers.GetUninitializedObject(typeof(ToolDiscoveryResult));
        SetBackingField(instance, nameof(ToolDiscoveryResult.Reason), reason);
        SetBackingField(instance, nameof(ToolDiscoveryResult.LaunchKind), launchKind);
        SetBackingField(instance, nameof(ToolDiscoveryResult.ResolvedExecutablePath), resolvedExecutablePath);
        SetBackingField(instance, nameof(ToolDiscoveryResult.ResolvedScriptPath), resolvedScriptPath);
        SetBackingField(instance, nameof(ToolDiscoveryResult.Version), version);
        return instance;
    }

    private static void SetBackingField(object instance, string propertyName, object? value)
    {
        var field = typeof(ToolDiscoveryResult).GetField($"<{propertyName}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Backing field for '{propertyName}' was not found.");
        field.SetValue(instance, value);
    }
}
