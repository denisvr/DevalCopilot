using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.MarkHostCapabilityProbeDispatched;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RecordHostCapabilityProbeResult;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetDueHostCapabilityProbes;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Probes due host capabilities outside any EF Core transaction and records each result in a
/// short one. Structurally mirrors <see cref="ProcessAttemptSupervisor"/>, with one deliberate
/// difference: a version probe is read-only and idempotent, so host shutdown mid-probe simply
/// leaves the dispatch marker for the next restart to clear and retry — no terminal
/// classification is invented the way a process attempt's cancellation is.
/// </summary>
public sealed class HostCapabilityReadinessSupervisor(
    IServiceScopeFactory scopeFactory,
    IToolDiscoveryAdapter discoveryAdapter,
    ILogger<HostCapabilityReadinessSupervisor> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Bounds the short local SQLite write that records a probe result, independent of
    /// <c>stoppingToken</c> in both directions — host shutdown cannot cancel it early (that
    /// would lose a known result), and it cannot block shutdown indefinitely if the write ever
    /// stalls.
    /// </summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await ClaimAndProbeDueCapabilitiesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("host_capability_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ClaimAndProbeDueCapabilitiesAsync(CancellationToken stoppingToken)
    {
        var dueCapabilities = await DispatchAsync(new GetDueHostCapabilityProbesQuery(), stoppingToken);

        foreach (var capability in dueCapabilities)
        {
            await ProbeCapabilityAsync(capability, stoppingToken);
        }
    }

    private async Task ProbeCapabilityAsync(Capability capability, CancellationToken stoppingToken)
    {
        // The in-flight claim: committed before the discovery adapter is ever invoked, so two
        // polls never overlap a probe for the same capability. If this fails — including
        // because host shutdown cancelled it, which is safe here since no external work has
        // happened yet — the adapter is never called for this cycle.
        try
        {
            var dispatchMarked = await DispatchAsync(
                new MarkHostCapabilityProbeDispatchedCommand(capability), stoppingToken);

            if (dispatchMarked.IsFailure)
            {
                logger.LogError(
                    "host_capability_dispatch_marking_rejected Capability={Capability} ErrorCode={ErrorCode}",
                    capability, dispatchMarked.Errors[0].Code);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            logger.LogError("host_capability_dispatch_marking_failed Capability={Capability}", capability);
            return;
        }

        ToolDiscoveryResult outcome;
        try
        {
            // Cancellable by stoppingToken: a version probe is read-only and idempotent, so
            // shutdown mid-probe is safe to simply abandon — the marker stays set and restart
            // reconciliation clears it, re-probing on the next host instance.
            outcome = await discoveryAdapter.DiscoverAsync(capability, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            logger.LogError("host_capability_probe_failed Capability={Capability}", capability);
            return;
        }

        // A closed classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (shutdown must not cancel
        // it early and lose a known outcome) nor CancellationToken.None (a stalled local write
        // must not block shutdown forever). If it still times out or otherwise fails, nothing
        // retries here — the capability simply stays dispatched, exactly like any other
        // unrecorded probe, for the next startup's reconciliation to clear.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        try
        {
            var recordResult = await DispatchAsync(
                new RecordHostCapabilityProbeResultCommand(capability, outcome), recordingTimeoutSource.Token);

            if (recordResult.IsFailure)
            {
                logger.LogError(
                    "host_capability_result_recording_rejected Capability={Capability} ErrorCode={ErrorCode}",
                    capability, recordResult.Errors[0].Code);
            }
        }
        catch (Exception)
        {
            logger.LogError("host_capability_result_recording_failed Capability={Capability}", capability);
        }
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
}
