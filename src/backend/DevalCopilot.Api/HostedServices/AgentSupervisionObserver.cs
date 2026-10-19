using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Commands.RegisterAgentSupervision;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The one small host-owned collaborator through which the seven Agent supervisors share one observation policy (ADR-0056): it starts an
/// <see cref="AgentObservation"/> for one claimed attempt and writes it through the mediator in independent short scopes, never the
/// supervisor's own scope or tracked DbContext. Each call is bounded to two seconds independently of the underlying operation, an
/// observation fault is logged by fixed code only and never becomes an Agent outcome, and nothing here claims, dispatches, retries,
/// cancels or terminalizes an attempt. The supervisors keep their own eligibility, preparation, invocation, parsing and result
/// ownership. Resolved optionally from the supervisor's own composition, so a composition without it simply observes nothing.
/// </summary>
public sealed class AgentSupervisionObserver(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    AgentSupervisionHostInstance host,
    ILogger<AgentSupervisionObserver> logger)
{
    internal Guid HostInstanceId => host.Id;

    internal TimeProvider Clock => timeProvider;

    /// <summary>Starts the observation of one claimed attempt through the observer of the supervisor's own composition. Never throws:
    /// without an observer, or when registration is refused or fails, the returned observation is inert.</summary>
    public static async Task<AgentObservation> BeginAsync(IServiceScopeFactory scopeFactory, Guid runId, Guid attemptId)
    {
        AgentSupervisionObserver? observer;
        try
        {
            using var scope = scopeFactory.CreateScope();
            observer = scope.ServiceProvider.GetService<AgentSupervisionObserver>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AgentObservation.None;
        }

        return observer is null ? AgentObservation.None : await observer.BeginAsync(runId, attemptId);
    }

    public async Task<AgentObservation> BeginAsync(Guid runId, Guid attemptId)
    {
        var observerId = Guid.NewGuid();
        var registered = await BoundedAsync(
            StartWrite(new RegisterAgentSupervisionCommand(runId, attemptId, host.Id, observerId), "agent_supervision_registration_failed", attemptId),
            "agent_supervision_registration_timed_out",
            attemptId);
        return registered ? new AgentObservation(this, attemptId, observerId) : AgentObservation.None;
    }

    /// <summary>
    /// Starts one write off the caller's thread and returns at once. The store can block synchronously before its first real await (SQLite
    /// waits for a held write lock inside the call that begins a transaction), so a write is never started inline: the caller's bound
    /// covers it from its first instruction. The write owns its scope and DbContext until it settles, so a bound that expires never
    /// disposes state the running write still uses; it is left to finish under its own token and its revision/identity/closure fences.
    /// <paramref name="after"/> orders this write behind an earlier one of the same observation, so writes never overlap or reorder.
    /// </summary>
    internal Task<bool> StartWrite<TValue>(ICommand<Result<TValue>> command, string failureCode, Guid attemptId, Task? after = null) =>
        Task.Run(async () =>
        {
            if (after is not null)
            {
                // Only the order matters: an earlier write never throws (SendAsync swallows every observation error).
                await after.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            return await SendAsync(command, failureCode, attemptId).ConfigureAwait(false);
        });

    /// <summary>One write in its own scope with its own two-second bound. It never throws and never lets an observation error escape; it
    /// is true only for a committed success. A bounded token cancels the mediator call, never the underlying work of a supervisor. Callers
    /// start it through <see cref="StartWrite"/>, never inline.</summary>
    private async Task<bool> SendAsync<TValue>(ICommand<Result<TValue>> command, string failureCode, Guid attemptId)
    {
        using var bound = new CancellationTokenSource(AgentSupervisionPolicy.CallBound, timeProvider);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, bound.Token);
            if (result.IsFailure)
            {
                // A refusal (a closed, foreign, competing or incoherent observation) is an observation fact, never an Agent failure. It is
                // expected whenever a supervisor re-enters an unresolved attempt, so it is not a warning.
                logger.LogDebug("{Event} AttemptId={AttemptId} ErrorCode={ErrorCode}", failureCode, attemptId, result.Errors[0].Code);
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            logger.LogWarning("{Event} AttemptId={AttemptId}", failureCode, attemptId);
            return false;
        }
    }

    /// <summary>Waits for an observation write at most the call bound. A write that outlives it is left to settle on its own; the caller
    /// goes on, and no timed-out task is ever replaced by a second one.</summary>
    internal async Task<bool> BoundedAsync(Task<bool> write, string timeoutCode, Guid attemptId)
    {
        try
        {
            return await write.WaitAsync(AgentSupervisionPolicy.CallBound, timeProvider);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("{Event} AttemptId={AttemptId}", timeoutCode, attemptId);
            return false;
        }
    }

    internal void LogWarning(string eventCode, Guid attemptId) =>
        logger.LogWarning("{Event} AttemptId={AttemptId}", eventCode, attemptId);
}
