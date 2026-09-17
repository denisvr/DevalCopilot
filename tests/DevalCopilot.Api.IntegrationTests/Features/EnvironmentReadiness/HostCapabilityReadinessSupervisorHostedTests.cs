using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.MarkHostCapabilityProbeDispatched;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.ReconcileInterruptedHostCapabilityProbes;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Starts the real <see cref="HostCapabilityReadinessSupervisor"/> — the actual
/// <c>BackgroundService</c>, with its real scoped mediator and EF-transaction pipeline — against
/// a fake <see cref="IToolDiscoveryAdapter"/>. A fake is used here (rather than the real
/// Infrastructure adapter against a real system tool, as <c>ProcessAttemptSupervisorHostedTests</c>
/// does against its deterministic fixture executable) because the fixed catalog's executable
/// identities are real tool names the test machine may or may not have installed; faking the
/// port itself keeps these tests deterministic regardless of the host they run on.
/// </summary>
public sealed class HostCapabilityReadinessSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-capability-supervisor-hosted-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Starting_the_supervisor_probes_one_due_capability_and_records_its_result_exactly_once()
    {
        var invocationCount = 0;
        await using var provider = BuildServiceProvider(services =>
        {
            services.AddSingleton<IToolDiscoveryAdapter>(new FakeToolDiscoveryAdapter((_, _) =>
            {
                Interlocked.Increment(ref invocationCount);
                return Task.FromResult(
                    ToolDiscoveryResult.DirectExecutableSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0"));
            }));
        });
        await SeedCatalogAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await PollForReasonAsync(provider, Capability.Git, CapabilityProbeReason.None);

            // Several more 200ms poll ticks pass here while the capability sits fresh
            // (NextProbeDueAtUtc far in the future), to prove it is never re-probed.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(1, invocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
        Assert.Equal("2.43.0", persisted!.ObservedVersion);
        Assert.Null(persisted.ProbeDispatchedAtUtc);
    }

    [Fact]
    public async Task A_slow_probe_is_never_invoked_a_second_time_while_still_in_flight()
    {
        var invocationCount = 0;
        var probeStartedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbeSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildServiceProvider(services =>
        {
            services.AddSingleton<IToolDiscoveryAdapter>(new FakeToolDiscoveryAdapter(async (_, _) =>
            {
                Interlocked.Increment(ref invocationCount);
                probeStartedSource.TrySetResult();
                await releaseProbeSource.Task;
                return ToolDiscoveryResult.DirectExecutableSuccess(@"C:\fake\git.exe", "1.0.0");
            }));
        });
        await SeedCatalogAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await probeStartedSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Several more 200ms poll ticks pass here while the probe is still stalled — the
            // durable dispatch marker must prevent any of them from invoking it again.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
            Assert.Equal(1, invocationCount);

            releaseProbeSource.TrySetResult();
            await PollForReasonAsync(provider, Capability.Git, CapabilityProbeReason.None);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task Host_shutdown_mid_probe_leaves_the_dispatch_marker_set_rather_than_inventing_a_result()
    {
        var probeStartedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var provider = BuildServiceProvider(services =>
        {
            services.AddSingleton<IToolDiscoveryAdapter>(new FakeToolDiscoveryAdapter(async (_, cancellationToken) =>
            {
                probeStartedSource.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable: the delay above never completes without cancellation.");
            }));
        });
        await SeedCatalogAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);

        await probeStartedSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (var stopCancellation = new CancellationTokenSource(PollTimeout))
        {
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);

        // A version probe is read-only and idempotent: shutdown mid-probe simply abandons it —
        // the marker stays set for the next restart to clear, and no terminal reason is
        // invented the way a process attempt's cancellation would be.
        Assert.NotNull(persisted!.ProbeDispatchedAtUtc);
        Assert.Equal(CapabilityProbeReason.NeverProbed, persisted.ReasonCode);
    }

    [Fact]
    public async Task A_failed_dispatch_marking_transaction_prevents_the_probe_from_running()
    {
        var invoked = false;
        await using var provider = BuildServiceProvider(services =>
        {
            services.AddSingleton<IToolDiscoveryAdapter>(new FakeToolDiscoveryAdapter((_, _) =>
            {
                invoked = true;
                return Task.FromResult(ToolDiscoveryResult.DirectExecutableSuccess(@"C:\fake\git.exe", "1.0.0"));
            }));
            services.RemoveAll<IRequestHandler<MarkHostCapabilityProbeDispatchedCommand, Result<DateTimeOffset>>>();
            services.AddScoped<IRequestHandler<MarkHostCapabilityProbeDispatchedCommand, Result<DateTimeOffset>>>(
                _ => new AlwaysFailingMarkHostCapabilityProbeDispatchedCommandHandler());
        });
        await SeedCatalogAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800));
            Assert.False(invoked);

            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var persisted = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
            Assert.Null(persisted!.ProbeDispatchedAtUtc);
            Assert.Equal(CapabilityProbeReason.NeverProbed, persisted.ReasonCode);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task Restart_reconciliation_clears_a_dispatch_marker_a_prior_instance_left_stuck()
    {
        await using (var provider = BuildServiceProvider())
        {
            await SeedCatalogAsync(provider);
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var snapshot = await dbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);
            snapshot!.MarkDispatched(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        await using var reopenedProvider = BuildServiceProvider();
        await using (var scope = reopenedProvider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var result = await mediator.SendAsync(new ReconcileInterruptedHostCapabilityProbesCommand(), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value);
        }

        await using var verificationScope = reopenedProvider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persisted = await verificationDbContext.HostCapabilitySnapshots.FindAsync(Capability.Git);

        Assert.Null(persisted!.ProbeDispatchedAtUtc);
        Assert.Equal(CapabilityProbeReason.ProbeInterruptedByRestart, persisted.ReasonCode);
    }

    private sealed class FakeToolDiscoveryAdapter(
        Func<Capability, CancellationToken, Task<ToolDiscoveryResult>> discover) : IToolDiscoveryAdapter
    {
        public Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken) =>
            discover(capability, cancellationToken);
    }

    private sealed class AlwaysFailingMarkHostCapabilityProbeDispatchedCommandHandler
        : ICommandHandler<MarkHostCapabilityProbeDispatchedCommand, Result<DateTimeOffset>>
    {
        public Task<Result<DateTimeOffset>> HandleAsync(MarkHostCapabilityProbeDispatchedCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result<DateTimeOffset>.Failure(
                Error.Conflict("host_capabilities.dispatch_marking_test_failure", "Simulated failure.")));
    }

    private ServiceProvider BuildServiceProvider(Action<ServiceCollection>? configureAdditionalServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddDevalenteMediator(typeof(EnsureHostCapabilityCatalogSeededCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(EnsureHostCapabilityCatalogSeededCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        configureAdditionalServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds the full 7-capability catalog, then pushes every capability except
    /// <paramref name="dueCapability"/> far into the future — otherwise all 7 are freshly due
    /// at once and the supervisor correctly probes every one of them in the same cycle, which
    /// would make "was this one capability probed" assertions ambiguous.
    /// </summary>
    private static async Task SeedCatalogAsync(ServiceProvider provider, Capability dueCapability = Capability.Git)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var seedResult = await mediator.SendAsync(new EnsureHostCapabilityCatalogSeededCommand(), CancellationToken.None);
        Assert.True(seedResult.IsSuccess);

        var farFuture = DateTimeOffset.UtcNow.AddDays(1);
        var otherSnapshots = await dbContext.HostCapabilitySnapshots
            .Where(snapshot => snapshot.Capability != dueCapability)
            .ToListAsync();
        foreach (var snapshot in otherSnapshots)
        {
            snapshot.MarkDispatched(DateTimeOffset.UtcNow);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\tool.exe", null, "1.0.0", DateTimeOffset.UtcNow, farFuture);
        }

        await dbContext.SaveChangesAsync();
    }

    private static HostCapabilityReadinessSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IToolDiscoveryAdapter>(),
        NullLogger<HostCapabilityReadinessSupervisor>.Instance);

    private static async Task<CapabilityProbeReason> PollForReasonAsync(ServiceProvider provider, Capability capability, CapabilityProbeReason expected)
    {
        var deadline = DateTimeOffset.UtcNow.Add(PollTimeout);
        CapabilityProbeReason reason;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await using var pollScope = provider.CreateAsyncScope();
            var dbContext = pollScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            reason = (await dbContext.HostCapabilitySnapshots.FindAsync(capability))!.ReasonCode;
        }
        while (reason != expected && DateTimeOffset.UtcNow < deadline);

        return reason;
    }
}
