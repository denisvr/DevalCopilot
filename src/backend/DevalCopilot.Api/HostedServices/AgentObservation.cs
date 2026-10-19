using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.AdvanceAgentSupervisionPhase;
using DevalCopilot.Application.Features.Runs.Commands.CloseAgentSupervision;
using DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The lifetime of one host observation of one claimed Agent attempt (ADR-0056). A supervisor opens it at entry to its per-attempt
/// execution with <c>await using</c>, reports the two later phases, and disposal always ends it. While it lives, one loop reports a
/// heartbeat every five seconds with at most one pending write and no accumulating ticks. Every write runs off the caller's thread, in its
/// own scope, behind the previous write of this observation (so writes never overlap or reorder), and every call and the disposal waits
/// are bounded to two seconds independently of the supervisor's own token, even when the store blocks synchronously. None of them can
/// throw, skip a safety check, discard a known result or replace an original exception or cancellation. A heartbeat proves only that this
/// loop reported; the phase deadlines are absolute and are never extended by it. Closure reads the durable Attempt afresh; a method
/// return is never evidence that a result committed.
/// </summary>
public sealed class AgentObservation : IAsyncDisposable
{
    /// <summary>An inert observation: nothing is registered, nothing is written.</summary>
    public static readonly AgentObservation None = new();

    private readonly AgentSupervisionObserver? observer;
    private readonly Guid attemptId;
    private readonly Guid observerId;
    private readonly CancellationTokenSource? stopSource;
    private readonly Task loop = Task.CompletedTask;
    private readonly Lock writeGate = new();

    // The last write of this observation, settled or not: every new write is ordered behind it, and a heartbeat is skipped while it is
    // pending. At most the two phase entries and the closure can wait behind one another, so pending writes stay bounded.
    private Task lastWrite = Task.CompletedTask;
    private int disposed;

    private AgentObservation()
    {
    }

    internal AgentObservation(AgentSupervisionObserver observer, Guid attemptId, Guid observerId)
    {
        this.observer = observer;
        this.attemptId = attemptId;
        this.observerId = observerId;
        stopSource = new CancellationTokenSource();
        loop = Task.Run(() => HeartbeatLoopAsync(stopSource.Token));
    }

    public bool IsActive => observer is not null;

    /// <summary>The adapter call is about to start, after the ordinary dispatch gate succeeded. It does not claim a process started.</summary>
    public Task EnterAwaitingAdapterResultAsync() => EnterAsync(AgentSupervisionPhase.AwaitingAdapterResult);

    /// <summary>The adapter call returned or threw, or none was made: the existing post-source, artifact and result-recording work runs.</summary>
    public Task EnterRecordingResultAsync() => EnterAsync(AgentSupervisionPhase.RecordingResult);

    public async ValueTask DisposeAsync()
    {
        if (observer is null || Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await stopSource!.CancelAsync();
            try
            {
                await loop.WaitAsync(AgentSupervisionPolicy.CallBound, observer.Clock);
            }
            catch (Exception)
            {
                // The loop only waits for ticks and starts writes off its own thread; a bounded wait that fails changes nothing durable.
            }

            await observer.BoundedAsync(
                Enqueue(
                    new CloseAgentSupervisionCommand(attemptId, observer.HostInstanceId, observerId),
                    "agent_supervision_closure_failed",
                    onlyWhenIdle: false)!,
                "agent_supervision_closure_timed_out",
                attemptId);
        }
        catch (Exception)
        {
            // Disposal never throws: it must not replace the supervisor's own exception or cancellation.
        }
        finally
        {
            stopSource!.Dispose();
        }
    }

    private async Task EnterAsync(AgentSupervisionPhase phase)
    {
        if (observer is null || Volatile.Read(ref disposed) == 1)
        {
            return;
        }

        try
        {
            await observer.BoundedAsync(
                Enqueue(
                    new AdvanceAgentSupervisionPhaseCommand(attemptId, observer.HostInstanceId, observerId, phase),
                    "agent_supervision_phase_failed",
                    onlyWhenIdle: false)!,
                "agent_supervision_phase_timed_out",
                attemptId);
        }
        catch (Exception)
        {
            // An observation fault never reaches the supervisor.
        }
    }

    /// <summary>Starts one write behind the previous one, off the caller's thread. With <paramref name="onlyWhenIdle"/> nothing is
    /// started (null) while an earlier write is still pending: a heartbeat is dropped, never queued.</summary>
    private Task<bool>? Enqueue<TValue>(ICommand<Result<TValue>> command, string failureCode, bool onlyWhenIdle)
    {
        lock (writeGate)
        {
            if (onlyWhenIdle && !lastWrite.IsCompleted)
            {
                return null;
            }

            var write = observer!.StartWrite(command, failureCode, attemptId, lastWrite.IsCompleted ? null : lastWrite);
            lastWrite = write;
            return write;
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken stopToken)
    {
        try
        {
            using var timer = new PeriodicTimer(AgentSupervisionPolicy.HeartbeatInterval, observer!.Clock);
            while (await timer.WaitForNextTickAsync(stopToken))
            {
                // One pending write at most: a tick that finds any write of this observation unresolved is dropped, never queued, and a
                // timed-out write is never replaced by a second one while it is still unresolved.
                _ = Enqueue(
                    new HeartbeatAgentSupervisionCommand(attemptId, observer.HostInstanceId, observerId),
                    "agent_supervision_heartbeat_failed",
                    onlyWhenIdle: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal stopped the loop.
        }
        catch (Exception)
        {
            observer!.LogWarning("agent_supervision_heartbeat_loop_failed", attemptId);
        }
    }
}
