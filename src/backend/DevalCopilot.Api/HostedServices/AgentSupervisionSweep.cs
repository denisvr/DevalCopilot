using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ObserveAgentSupervision;
using DevalCopilot.Application.Features.Runs.Queries.GetOpenAgentSupervisions;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The independent bounded observation of every open Agent supervision (ADR-0056). It runs on its own timer, apart from every role
/// supervisor, so a blocked Git preparation, adapter or result recorder cannot hide an elapsed deadline or a stale heartbeat. Each round
/// reads a bounded window of open observations after a persisted-in-memory cursor and observes them, so every open observation is visited
/// eventually, without starvation. Every store call runs off the sweep's own thread in a scope it owns until the call settles, so a store
/// that blocks synchronously can delay neither the loop nor host shutdown beyond the two-second bound, and a bound that expires never
/// disposes state a running call still uses. At most one call per observation (and one inventory read) is pending at a time; an
/// observation whose earlier call is unresolved is skipped, never observed again concurrently. Observation records the first attention
/// once; it never claims, retries, cancels or terminalizes an attempt and never changes a Run or workspace. Startup closure of
/// previous-host observations has already run before this service starts. No work runs in side-effect-free compositions.
/// </summary>
public sealed class AgentSupervisionSweep(
    IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AgentSupervisionSweep> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Owned by the single sweep loop only.
    private readonly Dictionary<Guid, Task> pendingObservations = [];
    private Task<OpenAgentSupervisionWindow>? pendingInventory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        Guid? cursor = null;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                cursor = await SweepRoundAsync(cursor, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown: nothing durable is half written, every write is its own short transaction and a pending one settles alone.
        }
    }

    private async Task<Guid?> SweepRoundAsync(Guid? cursor, CancellationToken stoppingToken)
    {
        foreach (var settled in pendingObservations.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToList())
        {
            pendingObservations.Remove(settled);
        }

        // One inventory read at a time: a read that outlived its bound is awaited by a later round, never replaced by a second one.
        pendingInventory ??= Start((mediator, token) => mediator.SendAsync(new GetOpenAgentSupervisionsQuery(cursor), token));
        OpenAgentSupervisionWindow window;
        try
        {
            window = await pendingInventory.WaitAsync(AgentSupervisionPolicy.CallBound, timeProvider, stoppingToken);
            pendingInventory = null;
        }
        catch (TimeoutException)
        {
            if (pendingInventory?.IsCompleted == true)
            {
                pendingInventory = null;
            }

            logger.LogWarning("agent_supervision_sweep_inventory_timed_out");
            return cursor;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            pendingInventory = null;
            logger.LogWarning("agent_supervision_sweep_inventory_failed");
            return cursor;
        }

        var started = new List<Task>();
        foreach (var supervisionId in window.SupervisionIds)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (pendingObservations.ContainsKey(supervisionId))
            {
                continue;
            }

            var observation = ObserveAsync(supervisionId);
            pendingObservations[supervisionId] = observation;
            started.Add(observation);
        }

        try
        {
            // One bound for the round, independent of every single observation: a stuck one is left pending and skipped next time.
            await Task.WhenAll(started).WaitAsync(AgentSupervisionPolicy.CallBound, timeProvider, stoppingToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("agent_supervision_sweep_observation_timed_out");
        }

        return window.NextCursor ?? cursor;
    }

    /// <summary>One observation in its own scope with its own two-second token, independent of the host token and of every other
    /// observation. It never throws; a refusal or fault is logged by fixed code only.</summary>
    private Task ObserveAsync(Guid supervisionId) => Task.Run(async () =>
    {
        try
        {
            using var bound = new CancellationTokenSource(AgentSupervisionPolicy.CallBound, timeProvider);
            await using var scope = scopeFactory.CreateAsyncScope();
            var observed = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new ObserveAgentSupervisionCommand(supervisionId), bound.Token);
            if (observed.IsFailure)
            {
                logger.LogWarning("agent_supervision_sweep_observation_refused ErrorCode={ErrorCode}", observed.Errors[0].Code);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("agent_supervision_sweep_observation_failed SupervisionId={SupervisionId}", supervisionId);
        }
    });

    /// <summary>Starts one read off the sweep's thread, with its own two-second token, in a scope it owns until the read settles.</summary>
    private Task<T> Start<T>(Func<IApplicationMediator, CancellationToken, Task<T>> read) => Task.Run(async () =>
    {
        using var bound = new CancellationTokenSource(AgentSupervisionPolicy.CallBound, timeProvider);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IApplicationMediator>(), bound.Token);
    });
}
