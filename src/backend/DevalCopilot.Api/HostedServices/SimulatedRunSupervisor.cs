using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ClaimSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;
using DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleSimulatedRuns;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Claims runs whose intent was recorded and plays back the deterministic simulated
/// step sequence outside the command that recorded intent. Every mediator dispatch opens
/// its own short-lived DI scope (and therefore its own scoped DbContext), which is
/// disposed before the next simulated-work delay begins — no scope, connection, or
/// transaction is ever held open across a wait. A failed step stops the attempt instead
/// of silently continuing toward completion.
/// </summary>
public sealed class SimulatedRunSupervisor(
    IServiceScopeFactory scopeFactory,
    ISimulatedAgentAdapter adapter,
    ILogger<SimulatedRunSupervisor> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(300);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await ClaimAndRunEligibleWorkAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Simulated run supervisor iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ClaimAndRunEligibleWorkAsync(CancellationToken stoppingToken)
    {
        var eligibleRunIds = await DispatchAsync(new GetEligibleSimulatedRunsQuery(), stoppingToken);

        foreach (var runId in eligibleRunIds)
        {
            await RunSimulatedAttemptAsync(runId, stoppingToken);
        }
    }

    private async Task RunSimulatedAttemptAsync(Guid runId, CancellationToken stoppingToken)
    {
        var claimResult = await DispatchAsync(new ClaimSimulatedRunCommand(runId), stoppingToken);
        if (claimResult.IsFailure)
        {
            // Another supervisor tick or instance already claimed this run; nothing to do.
            return;
        }

        var attemptId = claimResult.Value.AttemptId;
        var messageIds = new List<Guid>();

        foreach (var step in adapter.GetSteps())
        {
            await Task.Delay(StepDelay, stoppingToken);

            Guid? inReplyToMessageId = null;
            if (step.InReplyToStepIndex.HasValue)
            {
                if (step.InReplyToStepIndex.Value < 0 || step.InReplyToStepIndex.Value >= messageIds.Count)
                {
                    logger.LogError("Simulated run {RunId} has an invalid collaboration reply reference.", runId);
                    return;
                }

                inReplyToMessageId = messageIds[step.InReplyToStepIndex.Value];
            }

            var stepResult = await DispatchAsync(
                new RecordSimulatedAgentStepCommand(
                    runId,
                    attemptId,
                    step.Stage,
                    step.Actor,
                    step.Recipient,
                    step.EventType,
                    step.MessageType,
                    inReplyToMessageId,
                    step.Summary,
                    step.StructuredContentJson),
                stoppingToken);

            if (stepResult.IsFailure)
            {
                logger.LogError(
                    "Simulated run {RunId} attempt {AttemptId} failed to record step {EventType}: {ErrorCode}",
                    runId, attemptId, step.EventType, stepResult.Errors[0].Code);
                return;
            }

            messageIds.Add(stepResult.Value.MessageId);
            await NotifyAsync(runId, stepResult.Value.EventSequence, stoppingToken);
        }

        await Task.Delay(StepDelay, stoppingToken);
        var completeResult = await DispatchAsync(new CompleteSimulatedRunCommand(runId, attemptId), stoppingToken);
        if (completeResult.IsFailure)
        {
            logger.LogError(
                "Simulated run {RunId} attempt {AttemptId} failed to complete: {ErrorCode}",
                runId, attemptId, completeResult.Errors[0].Code);
            return;
        }

        await NotifyAsync(runId, completeResult.Value, stoppingToken);
    }

    private async Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        return await mediator.SendAsync(command, cancellationToken);
    }

    private async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        return await mediator.SendAsync(query, cancellationToken);
    }

    private async Task NotifyAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(runId, latestSequence, cancellationToken);
    }
}
