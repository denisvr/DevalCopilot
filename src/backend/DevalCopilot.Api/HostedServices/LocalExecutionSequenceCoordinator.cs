using System.Collections.Concurrent;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionQueue;
using DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ReserveLocalExecutionAdmission;
using DevalCopilot.Application.Features.Runs.Commands.ResolveLocalExecutionSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The one fixed local execution coordinator (ADR-0060). On a bounded poll it observes each live sequence through one mediator command
/// that decides entirely from locked durable state (deadlines, drain, classification of a finished operation, reservation of the next
/// step), and it tries the one claim of a step only for the call that reserved it. It composes the existing claims through the
/// mediator and never runs a provider or a process, selects an input, retries, repairs or resumes anything: the existing provider and
/// verification supervisors keep executing durably claimed operations exactly as before. Each sequence is observed by at most one
/// in-process worker at a time and slow preparation of one project never blocks another. Startup closure has already interrupted every
/// formerly advancing sequence before this service starts, and a restart or a late result never creates a successor. The ticket and its
/// preparation token live only in this process: no other host and no later tick can claim a reserved step.
/// </summary>
public sealed class LocalExecutionSequenceCoordinator(IServiceScopeFactory scopeFactory, ILogger<LocalExecutionSequenceCoordinator> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>At most this many step preparations (Git evidence capture and manifest sealing) run at once.</summary>
    private const int MaximumConcurrentPreparations = 4;

    /// <summary>One observation per sequence at a time. It is held only while the observation command runs, never across a claim.</summary>
    private readonly ConcurrentDictionary<Guid, Task> inFlight = new();

    /// <summary>The single preparation task of each reserved step, by step. Owned by the observation that reserved the step; a blocked
    /// preparation never prevents a later observation from expiring the step at its deadline.</summary>
    private readonly ConcurrentDictionary<Guid, Task> preparations = new();
    private readonly SemaphoreSlim preparationSlots = new(MaximumConcurrentPreparations, MaximumConcurrentPreparations);

    // ADR-0062: the rotating keyset cursors. A sweep fixes a high-water number, finishes that finite range page by page and refreshes it
    // on wrap, so an old set can neither starve later live records nor be kept from a revisit by continuous arrivals.
    private readonly LocalExecutionAdvancingSweep advancingSweep = new();
    private long queueCursor;
    private long? queueHighWater;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    await ObserveQueueAsync(stoppingToken);
                    await ReserveQueuedAdmissionsAsync(stoppingToken);

                    var page = await DispatchAsync(advancingSweep.NextQuery(), stoppingToken);
                    advancingSweep.Accept(page);
                    foreach (var sequence in page.Items)
                    {
                        // One in-process worker per sequence: a still-running worker is never doubled by a later tick.
                        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (inFlight.TryAdd(sequence.SequenceId, gate.Task))
                        {
                            _ = RunWorkerAsync(sequence.SequenceId, gate, stoppingToken);
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError("local_execution_sequence_coordinator_iteration_failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown: the in-flight workers observe the same token and stop on their own below.
        }

        await Task.WhenAll(inFlight.Values.Concat(preparations.Values).ToArray()).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>One bounded page of the queued-cycle observation. The in-process worker availability it passes is an observation that can
    /// only label a waiting reason; the database decides every admission.</summary>
    private async Task ObserveQueueAsync(CancellationToken stoppingToken)
    {
        var observed = await DispatchAsync(
            new ObserveLocalExecutionQueueCommand(queueCursor, queueHighWater, preparationSlots.CurrentCount > 0), stoppingToken);
        if (observed.IsFailure)
        {
            return;
        }

        if (observed.Value.SweepFinished)
        {
            queueCursor = 0;
            queueHighWater = null;
            return;
        }

        queueCursor = observed.Value.LastCoordinationNumber;
        queueHighWater = observed.Value.HighWaterCoordinationNumber;
    }

    /// <summary>ADR-0062: the host acquires an in-process preparation worker first, and only then asks the database to reserve the oldest
    /// eligible admission; a durable reservation is never held merely while waiting for that semaphore. The worker that receives the
    /// committed ticket alone prepares and claims it, once.</summary>
    private async Task ReserveQueuedAdmissionsAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; attempt < MaximumConcurrentPreparations; attempt++)
        {
            if (!await preparationSlots.WaitAsync(0, stoppingToken))
            {
                return;
            }

            var handedOver = false;
            try
            {
                var reserved = await DispatchAsync(new ReserveLocalExecutionAdmissionCommand(), stoppingToken);
                if (reserved.IsFailure || reserved.Value is not { ReservedStep: { } ticket, RunId: { } runId })
                {
                    return;
                }

                var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (preparations.TryAdd(ticket.StepId, preparation.Task))
                {
                    handedOver = true;
                    _ = RunPreparationAsync(runId, ticket.SequenceId, ticket, preparation, stoppingToken, slotHeld: true);
                }
            }
            finally
            {
                if (!handedOver)
                {
                    preparationSlots.Release();
                }
            }
        }
    }

    private async Task RunWorkerAsync(Guid sequenceId, TaskCompletionSource gate, CancellationToken stoppingToken)
    {
        try
        {
            await AdvanceOneAsync(sequenceId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("local_execution_sequence_coordinator_worker_failed SequenceId={SequenceId}", sequenceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; startup closure interrupts whatever was still advancing.
        }
        finally
        {
            inFlight.TryRemove(sequenceId, out _);
            gate.TrySetResult();
        }
    }

    private async Task AdvanceOneAsync(Guid sequenceId, CancellationToken stoppingToken)
    {
        var observed = await DispatchAsync(new ObserveLocalExecutionSequenceCommand(sequenceId), stoppingToken);
        if (observed.IsFailure || observed.Value.ReservedStep is not { } ticket)
        {
            return;
        }

        // This call alone reserved the step, so only it starts the one preparation. The preparation runs on its own task so that this
        // observation ends at once: the next poll observes the sequence again and expires the step at its deadline even while the
        // external capture is still blocked. A late return then loses the claim's compare-and-set; nothing is retried.
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (preparations.TryAdd(ticket.StepId, preparation.Task))
        {
            _ = RunPreparationAsync(observed.Value.RunId, sequenceId, ticket, preparation, stoppingToken);
        }
    }

    private async Task RunPreparationAsync(
        Guid runId,
        Guid sequenceId,
        LocalExecutionStepTicket ticket,
        TaskCompletionSource gate,
        CancellationToken stoppingToken,
        bool slotHeld = false)
    {
        try
        {
            await PrepareAsync(runId, sequenceId, ticket, stoppingToken, slotHeld);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("local_execution_sequence_coordinator_preparation_failed SequenceId={SequenceId}", sequenceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; startup closure interrupts whatever was still advancing.
        }
        finally
        {
            preparations.TryRemove(ticket.StepId, out _);
            gate.TrySetResult();
        }
    }

    private async Task PrepareAsync(
        Guid runId, Guid sequenceId, LocalExecutionStepTicket ticket, CancellationToken stoppingToken, bool slotHeld = false)
    {
        // Every outcome other than a proved success is settled by an independent read of the exact step/operation association, never by
        // another claim attempt.
        string? failureCode = null;
        var claimSucceeded = false;
        if (!slotHeld)
        {
            await preparationSlots.WaitAsync(stoppingToken);
        }

        try
        {
            var claimed = await DispatchAsync(new ClaimLocalExecutionSequenceStepCommand(ticket), stoppingToken);
            claimSucceeded = claimed.IsSuccess;
            failureCode = claimed.IsFailure ? claimed.Errors[0].Code : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("local_execution_sequence_step_claim_failed SequenceId={SequenceId}", sequenceId);
            failureCode = LocalExecutionSequenceErrors.PersistenceFailedCode;
        }
        catch (OperationCanceledException)
        {
            // A cancellation never establishes that the claim did not commit: the independent read below decides.
            failureCode = LocalExecutionSequenceErrors.PersistenceUnresolvedCode;
        }
        finally
        {
            preparationSlots.Release();
        }

        if (claimSucceeded)
        {
            await NotifyAsync(runId, sequenceId);
            return;
        }

        try
        {
            await DispatchAsync(new ResolveLocalExecutionSequenceStepPreparationCommand(ticket, failureCode), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("local_execution_sequence_step_resolution_failed SequenceId={SequenceId}", sequenceId);
        }
    }

    /// <summary>Run-advance notifications are only hints (ADR-0003): the committed claim is announced after the fact and a failure to
    /// announce it never changes the recorded result.</summary>
    private async Task NotifyAsync(Guid run, Guid sequenceId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var status = await mediator.SendAsync(new GetLocalExecutionSequenceQuery(run), CancellationToken.None);
            if (status.IsSuccess)
            {
                var notifier = scope.ServiceProvider.GetRequiredService<DevalCopilot.Application.Features.Runs.Ports.IRunEventNotifier>();
                await notifier.NotifyRunAdvancedAsync(run, status.Value.LatestEventSequence, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("local_execution_sequence_notification_failed SequenceId={SequenceId}", sequenceId);
        }
    }

    private async Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, cancellationToken);
    }

    private async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(query, cancellationToken);
    }
}
