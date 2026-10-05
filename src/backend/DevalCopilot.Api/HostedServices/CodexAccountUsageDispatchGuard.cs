using System.Collections.Concurrent;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordCodexAccountUsageStop;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// The supervisors' side of the run-scoped Codex account-usage stop (ADR-0025), shared by the four Codex supervisors so they cannot
/// drift. Called after a supervisor's normal pre-dispatch Git capture and before <c>MarkAgentAttemptDispatchedCommand</c>:
/// <see cref="PrepareAsync"/> reads the claimed attempt's own stored threshold snapshot and, only when it claimed one, makes a separate
/// fresh strict observation bound to this exact attempt, threshold and the launch tuple the supervisor is about to hand its adapter.
/// A disabled stop makes no observation at all. When the guard stops (or cannot observe), the claimed, undispatched attempt is
/// resolved once to its terminal outcome through the dedicated recording command: no dispatch marker, no invocation, no polling
/// retry, no refund. The dispatch gate still validates the facts independently, and <see cref="ResolveRefusedAsync"/> resolves an attempt
/// whose gate refused for an account-usage reason. All external work happens here, outside any EF transaction.
/// </summary>
public sealed class CodexAccountUsageDispatchGuard(
    IServiceScopeFactory scopeFactory,
    IAccountUsageObserver adapter,
    TimeProvider timeProvider,
    ILogger<CodexAccountUsageDispatchGuard> logger)
{
    /// <summary>Attempts whose account-usage refusal could not be recorded durably. Process-local and deliberately never cleared: such
    /// an attempt stays Running and undispatched, is never observed or dispatched again by ordinary polling, and nothing here
    /// recovers, retries or refunds it.</summary>
    private readonly ConcurrentDictionary<Guid, byte> _unrecordedRefusals = new();

    /// <summary>True when this attempt was refused for an account-usage reason but the terminal recording could not commit, so it
    /// must never return to dispatch eligibility.</summary>
    public bool IsBlocked(Guid attemptId) => _unrecordedRefusals.ContainsKey(attemptId);

    public async Task<CodexAccountUsageDispatchPreparation> PrepareAsync(
        Guid runId, Guid attemptId, CodexLaunchTarget? launchTarget, CancellationToken stoppingToken)
    {
        if (IsBlocked(attemptId))
        {
            return new CodexAccountUsageDispatchPreparation(true, null);
        }

        var plan = await QueryPlanAsync(runId, attemptId, stoppingToken);
        switch (plan.State)
        {
            case CodexAccountUsageStopPlanState.NotConfigured:
                return new CodexAccountUsageDispatchPreparation(false, null);
            case CodexAccountUsageStopPlanState.Invalid:
                await ResolveAsync(runId, attemptId, null);
                return new CodexAccountUsageDispatchPreparation(true, null);
        }

        var threshold = plan.ThresholdPercent!.Value;
        if (launchTarget is null)
        {
            await ResolveAsync(runId, attemptId, null);
            return new CodexAccountUsageDispatchPreparation(true, null);
        }

        var facts = await CodexAccountUsageStopGate.ObserveAsync(
            adapter, timeProvider, attemptId, threshold,
            new CodexAccountUsageLaunchTuple(launchTarget.ExecutablePath, launchTarget.ScriptPath), stoppingToken);
        if (CodexAccountUsageStopPolicy.Evaluate(facts, threshold, timeProvider.GetUtcNow()).Permits)
        {
            return new CodexAccountUsageDispatchPreparation(false, facts);
        }

        await ResolveAsync(runId, attemptId, facts);
        return new CodexAccountUsageDispatchPreparation(true, null);
    }

    /// <summary>True when the dispatch gate refused for an account-usage reason, which this guard must resolve instead of leaving the
    /// claimed attempt stranded.</summary>
    public static bool IsAccountUsageRefusal(string code) =>
        code is CodexAccountUsageStopGate.ReachedCode
            or CodexAccountUsageStopGate.EvidenceUnavailableCode
            or CodexAccountUsageStopGate.SettingInvalidCode
            or CodexAccountUsageStopGate.PolicyChangedCode
            or RecordCodexAccountUsageStopCommandHandler.GuardMismatchCode;

    public Task ResolveRefusedAsync(Guid runId, Guid attemptId, CodexAccountUsageGuardFacts? facts) => ResolveAsync(runId, attemptId, facts);

    private async Task ResolveAsync(Guid runId, Guid attemptId, CodexAccountUsageGuardFacts? facts)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        Result<RecordCodexAccountUsageStopCommandResult> recorded;
        try
        {
            recorded = await mediator.SendAsync(new RecordCodexAccountUsageStopCommand(runId, attemptId, facts), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A durable-write failure is the same blocker as a refused recording: nothing is echoed from the exception.
            Block(attemptId, "exception");
            return;
        }

        if (recorded.IsFailure)
        {
            Block(attemptId, recorded.Errors[0].Code);
            return;
        }

        // Notify only after durable commit — never before.
        await scope.ServiceProvider.GetRequiredService<IRunEventNotifier>()
            .NotifyRunAdvancedAsync(runId, recorded.Value.LatestEventSequence, CancellationToken.None);
    }

    /// <summary>The terminal recording of a refusal did not commit. The attempt stays Running and undispatched but is blocked from
    /// every later dispatch pass of this process (fail closed); the blocker is surfaced in the host log and nothing recovers it. An
    /// attempt that is already terminal, already dispatched or unknown needs no block: it is not eligible for dispatch anyway.</summary>
    private void Block(Guid attemptId, string code)
    {
        if (code is "attempts.not_found" or "attempts.not_eligible")
        {
            logger.LogWarning("codex_account_usage_stop_record_not_needed AttemptId={AttemptId} Code={Code}", attemptId, code);
            return;
        }

        _unrecordedRefusals.TryAdd(attemptId, 0);
        logger.LogError("codex_account_usage_stop_record_failed_attempt_blocked AttemptId={AttemptId} Code={Code}", attemptId, code);
    }

    private async Task<GetCodexAccountUsageStopPlanQueryResult> QueryPlanAsync(Guid runId, Guid attemptId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new GetCodexAccountUsageStopPlanQuery(runId, attemptId), cancellationToken);
    }
}
