using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;
using DomainProcessOutcome = DevalCopilot.Domain.Features.Runs.ProcessOutcome;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes claimed Process attempts outside any EF Core transaction and records their
/// terminal result in a short one. Separate from <see cref="SimulatedRunSupervisor"/> by
/// design — the two flows share no code and a change to one never risks the other. Never
/// retries automatically: a failed or interrupted attempt stays exactly as recorded until a
/// later, explicit re-run.
/// </summary>
public sealed class ProcessAttemptSupervisor(
    IServiceScopeFactory scopeFactory,
    IProcessExecutionAdapter adapter,
    ILogger<ProcessAttemptSupervisor> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Bounds the terminal-result recording transaction — a single short local SQLite
    /// write — once a real result, a shutdown-cancellation classification, or a safe
    /// no-result failure already exists. Deliberately independent of <c>stoppingToken</c> in
    /// both directions: host shutdown cannot cancel it early (that would lose a known
    /// result), and it cannot block shutdown indefinitely if the write ever stalls.
    /// </summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(5);

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
                // Never the exception object itself: it could carry command arguments,
                // environment data, or process output through its message. Only a stable
                // event message is logged.
                logger.LogError("process_attempt_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ClaimAndRunEligibleWorkAsync(CancellationToken stoppingToken)
    {
        var eligibleAttempts = await DispatchAsync(new GetEligibleProcessAttemptsQuery(), stoppingToken);

        foreach (var attempt in eligibleAttempts)
        {
            await RunProcessAttemptAsync(attempt, stoppingToken);
        }
    }

    private async Task RunProcessAttemptAsync(EligibleProcessAttempt attempt, CancellationToken stoppingToken)
    {
        // The execution-start claim: committed before the adapter is ever invoked, so the
        // external command runs at most once for this attempt even if a terminal result is
        // never recorded. If this fails — including because host shutdown cancelled it, which
        // is safe here since no external work has happened yet — the adapter is never called
        // and this attempt is simply left for a later poll (or restart reconciliation) to
        // resolve; nothing retries automatically within this call.
        try
        {
            var dispatchMarked = await DispatchAsync(
                new MarkProcessAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);

            if (dispatchMarked.IsFailure)
            {
                logger.LogError(
                    "process_attempt_dispatch_marking_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                    attempt.AttemptId, dispatchMarked.Errors[0].Code);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Never the exception object, its message, or any command/environment/output
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("process_attempt_dispatch_marking_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        // Reconstructed only from the persisted, non-secret intent — this slice never
        // persists environment variables, so the request always carries an empty one.
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = attempt.ExecutablePath,
            Arguments = attempt.Arguments,
            WorkingDirectory = attempt.WorkingDirectory,
            ApprovedRoot = attempt.ApprovedRoot,
            Timeout = attempt.Timeout,
            MaxBytesPerStream = attempt.MaxBytesPerStream,
            MaxTotalCapturedBytes = attempt.MaxTotalCapturedBytes,
            EnvironmentVariables = new Dictionary<string, string>(),
        };

        DomainProcessOutcome? outcome;
        int? exitCode = null;
        try
        {
            // Cancellable by stoppingToken: this is the only step host shutdown is allowed
            // to interrupt before a terminal classification exists.
            var result = await adapter.ExecuteAsync(request, stoppingToken);
            outcome = MapOutcome(result.Outcome);
            exitCode = result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Host shutdown began before the adapter itself could report a real result —
            // a known, safe terminal classification, not a crash. Recorded exactly like any
            // other cancellation the adapter might have reported on its own, so the attempt
            // is never left Running for restart reconciliation to misclassify as Interrupted.
            outcome = DomainProcessOutcome.Cancelled;
            exitCode = null;
        }
        catch (Exception)
        {
            // The adapter rejected the reconstructed request or failed to start the process
            // at all: no ProcessExecutionResult exists to report. The attempt and run still
            // fail atomically below, but with no outcome, exit code, or exception detail ever
            // persisted. Only a stable event code and the attempt identifier are logged —
            // never the exception object, its message, command arguments, environment data,
            // or process output.
            logger.LogError("process_attempt_execution_failed AttemptId={AttemptId}", attempt.AttemptId);
            outcome = null;
        }

        // A terminal classification exists at this point — a real result, an adapter-level
        // cancellation, or a safe no-result failure — so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled
        // local write must not block shutdown forever). If it still times out or otherwise
        // fails, no terminal result is invented and nothing retries here — the attempt simply
        // stays Running, exactly like any other unrecorded attempt, for the next startup's
        // reconciliation to mark Interrupted.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        try
        {
            var recordResult = await DispatchAsync(
                new RecordProcessAttemptResultCommand(attempt.RunId, attempt.AttemptId, outcome, exitCode),
                recordingTimeoutSource.Token);

            if (recordResult.IsFailure)
            {
                logger.LogError(
                    "process_attempt_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                    attempt.AttemptId, recordResult.Errors[0].Code);
            }
        }
        catch (Exception)
        {
            // Covers the bounded timeout elapsing as well as any other dispatch failure.
            // Never the exception object, its message, or any command/environment/output
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("process_attempt_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
        }
    }

    private static DomainProcessOutcome MapOutcome(ProcessExecutionOutcome outcome) => outcome switch
    {
        ProcessExecutionOutcome.Exited => DomainProcessOutcome.Exited,
        ProcessExecutionOutcome.TimedOut => DomainProcessOutcome.TimedOut,
        ProcessExecutionOutcome.Cancelled => DomainProcessOutcome.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

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
}
