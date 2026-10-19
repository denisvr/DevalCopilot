using System.Collections.Concurrent;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetAdvanceablePlanningSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The one fixed read-only planning coordinator (ADR-0055). On a bounded poll it observes each live sequence through one mediator
/// command that decides entirely from locked durable state (deadlines, drain, classification of a finished step, reservation of the
/// next step), and it tries the one claim of a step only for the call that reserved it. It composes the existing claim through the
/// mediator and never runs a provider, selects an input, retries, repairs or resumes anything: the three provider supervisors keep
/// executing durably claimed attempts exactly as before. Each sequence is observed by at most one in-process worker at a time and
/// slow preparation of one project never blocks another. Startup recovery has already interrupted every formerly advancing sequence
/// before this service starts, and a restart or a late result never creates a successor.
/// </summary>
public sealed class PlanningSequenceCoordinator(IServiceScopeFactory scopeFactory, ILogger<PlanningSequenceCoordinator> logger)
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    var sequences = await DispatchAsync(new GetAdvanceablePlanningSequencesQuery(), stoppingToken);
                    foreach (var sequence in sequences)
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
                    logger.LogError("planning_sequence_coordinator_iteration_failed");
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

    private async Task RunWorkerAsync(Guid sequenceId, TaskCompletionSource gate, CancellationToken stoppingToken)
    {
        try
        {
            await AdvanceOneAsync(sequenceId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("planning_sequence_coordinator_worker_failed SequenceId={SequenceId}", sequenceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; startup recovery interrupts whatever was still advancing.
        }
        finally
        {
            inFlight.TryRemove(sequenceId, out _);
            gate.TrySetResult();
        }
    }

    private async Task AdvanceOneAsync(Guid sequenceId, CancellationToken stoppingToken)
    {
        var observed = await DispatchAsync(new ObservePlanningSequenceCommand(sequenceId), stoppingToken);
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
        Guid runId, Guid sequenceId, PlanningSequenceStepTicket ticket, TaskCompletionSource gate, CancellationToken stoppingToken)
    {
        try
        {
            await PrepareAsync(runId, sequenceId, ticket, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("planning_sequence_coordinator_preparation_failed SequenceId={SequenceId}", sequenceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; startup recovery interrupts whatever was still advancing.
        }
        finally
        {
            preparations.TryRemove(ticket.StepId, out _);
            gate.TrySetResult();
        }
    }

    private async Task PrepareAsync(Guid runId, Guid sequenceId, PlanningSequenceStepTicket ticket, CancellationToken stoppingToken)
    {
        // Every outcome other than a proved success is settled by an independent read of the exact step/attempt association, never by
        // another claim attempt.
        string? failureCode = null;
        var claimSucceeded = false;
        await preparationSlots.WaitAsync(stoppingToken);
        try
        {
            var claimed = await DispatchAsync(new ClaimPlanningSequenceStepCommand(ticket), stoppingToken);
            claimSucceeded = claimed.IsSuccess;
            failureCode = claimed.IsFailure ? claimed.Errors[0].Code : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("planning_sequence_step_claim_failed SequenceId={SequenceId}", sequenceId);
            failureCode = PlanningSequenceErrors.PersistenceFailedCode;
        }
        catch (OperationCanceledException)
        {
            // A cancellation never establishes that the claim did not commit: the independent read below decides.
            failureCode = PlanningSequenceErrors.PersistenceUnresolvedCode;
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
            await DispatchAsync(new ResolvePlanningSequenceStepPreparationCommand(ticket, failureCode), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("planning_sequence_step_resolution_failed SequenceId={SequenceId}", sequenceId);
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
            var status = await mediator.SendAsync(new GetPlanningSequenceQuery(run), CancellationToken.None);
            if (status.IsSuccess)
            {
                var notifier = scope.ServiceProvider.GetRequiredService<DevalCopilot.Application.Features.Runs.Ports.IRunEventNotifier>();
                await notifier.NotifyRunAdvancedAsync(run, status.Value.LatestEventSequence, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("planning_sequence_notification_failed SequenceId={SequenceId}", sequenceId);
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
