using System.Reflection;
using DevalCopilot.Api.HostedServices;
using Microsoft.Extensions.Hosting;
using Xunit.Sdk;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// Test-owned completion proof of a STOPPED supervision sweep (LCQ-V2). The production sweep deliberately lets a call that outlived its bounded
/// stop keep running in a scope it owns until the call settles. A scope counter cannot prove that: a call paused before it enters the scope
/// factory, or one still queued behind it, is not counted yet. This helper instead (1) requires the sweep loop's own <c>ExecuteTask</c> to
/// have ended, so the sweep can start no further call and no longer mutates its private bookkeeping, (2) snapshots the sweep's pending
/// inventory read and pending observation Tasks, and (3) waits for every one of them to settle (their scopes and database connections are
/// disposed inside those Tasks) under a separate finite teardown bound that is never part of the measured stop. A Task that settles
/// faulted or canceled is settled; only an unfinished one fails. It reads two private Task fields by reflection and fails explicitly,
/// never as a zero count, when either is missing or of another type. It adds no production API, drain, delay or retry.
/// </summary>
internal static class SweepCompletion
{
    private const string InventoryField = "pendingInventory";
    private const string ObservationsField = "pendingObservations";

    /// <summary>The Tasks pending after the sweep loop ended: the inventory read (if any) and every unsettled-or-settled observation.</summary>
    internal static IReadOnlyList<Task> SnapshotPending(BackgroundService sweep)
    {
        var executeTask = sweep.ExecuteTask
            ?? throw new XunitException("The sweep was never started, so it has no loop to prove ended.");
        if (!executeTask.IsCompleted)
        {
            throw new XunitException("The sweep loop has not ended: its pending calls cannot be snapshotted yet.");
        }

        var type = sweep.GetType();
        var pending = new List<Task>();

        var inventory = type.GetField(InventoryField, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new XunitException($"{type.Name} has no private field '{InventoryField}': the completion proof cannot read its pending inventory.");
        if (!typeof(Task).IsAssignableFrom(inventory.FieldType))
        {
            throw new XunitException($"{type.Name}.{InventoryField} is a {inventory.FieldType.Name}, not a Task.");
        }

        if (inventory.GetValue(sweep) is Task inventoryTask)
        {
            pending.Add(inventoryTask);
        }

        var observations = type.GetField(ObservationsField, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new XunitException($"{type.Name} has no private field '{ObservationsField}': the completion proof cannot read its pending observations.");
        if (observations.GetValue(sweep) is not System.Collections.IDictionary dictionary
            || observations.FieldType.GetGenericArguments() is not [var keyType, var valueType]
            || keyType != typeof(Guid)
            || !typeof(Task).IsAssignableFrom(valueType))
        {
            throw new XunitException($"{type.Name}.{ObservationsField} is not a Dictionary<Guid, Task>.");
        }

        foreach (var value in dictionary.Values)
        {
            pending.Add((Task)value!);
        }

        return pending;
    }

    /// <summary>Proves the stopped sweep's loop ended and every call it left behind has settled, within <paramref name="bound"/>.</summary>
    internal static async Task WaitForSettledAsync(BackgroundService sweep, TimeSpan bound)
    {
        var executeTask = sweep.ExecuteTask ?? throw new XunitException("The sweep was never started.");
        if (await Task.WhenAny(executeTask, Task.Delay(bound)) != executeTask)
        {
            throw new XunitException($"The sweep loop had not ended after {bound.TotalSeconds:F0} s.");
        }

        var pending = SnapshotPending(sweep);
        var all = Task.WhenAll(pending.Select(Observe));
        if (await Task.WhenAny(all, Task.Delay(bound)) != all)
        {
            var unfinished = pending.Count(task => !task.IsCompleted);
            throw new XunitException($"{unfinished} of {pending.Count} pending sweep call(s) had not settled after {bound.TotalSeconds:F0} s.");
        }
    }

    // Settling is all that matters here: a canceled or faulted call has released its scope too, and its outcome is asserted elsewhere.
    private static async Task Observe(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // Settled with a fault or cancellation.
        }
    }
}
