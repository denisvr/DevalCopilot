using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;
using DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// The shared ADR-0056 scenarios, run against each of the seven real supervisors through its own composition: the real
/// <c>BackgroundService</c>, scoped mediator, EF-transaction pipeline, artifact store and a real file-backed SQLite database with
/// production migrations, faking only the provider adapter and the Git evidence reader. Every scenario compares the original path with
/// an unobserved baseline of the same composition: the same terminal status and outcome and the same invocation cardinality. Nothing
/// here starts a real provider, process or Git command.
/// </summary>
internal static class AgentSupervisionHostedScenarios
{
    private sealed class Session : IAsyncDisposable
    {
        private readonly AgentSupervisionHostedComposition composition;
        private readonly List<IHostedService> started = [];

        private Session(AgentSupervisionHostedComposition composition) => this.composition = composition;

        public OffsetClock Clock { get; } = new();

        public AgentSupervisionHostInstance Host { get; } = new(Guid.NewGuid());

        public GateableEvidenceReader Reader { get; private set; } = null!;

        public ServiceProvider Provider { get; private set; } = null!;

        public IHostedService Supervisor { get; private set; } = null!;

        public object Adapter { get; private set; } = null!;

        public Guid RunId { get; private set; }

        public Guid AttemptId { get; private set; }

        public static async Task<Session> CreateAsync(AgentSupervisionHostedComposition composition, bool observed, bool openAdapter = true)
        {
            // Each session owns a pristine database: a seed adds singleton rows (capability snapshots) that a second one cannot repeat.
            AgentSupervisionHostedSupport.ResetDatabase(composition.DatabasePath());
            var session = new Session(composition);
            session.Reader = new GateableEvidenceReader(composition.EvidenceForCall);
            session.Provider = composition.Build(
                session.Reader,
                observed ? services => AgentSupervisionHostedSupport.Register(services, session.Clock, session.Host) : _ => { });
            (session.RunId, session.AttemptId) = await composition.Seed(session.Provider, session.Reader);
            session.Adapter = composition.AdapterOf(session.Provider);
            if (openAdapter)
            {
                composition.OpenAdapter?.Invoke(session.Adapter);
            }

            session.Supervisor = composition.CreateSupervisor(session.Provider);
            return session;
        }

        public async Task StartSupervisorAsync()
        {
            await Supervisor.StartAsync(CancellationToken.None);
            started.Add(Supervisor);
        }

        public async Task<AgentSupervisionSweep> StartSweepAsync()
        {
            var sweep = AgentSupervisionHostedSupport.CreateSweep(Provider);
            await sweep.StartAsync(CancellationToken.None);
            started.Add(sweep);
            return sweep;
        }

        public async Task StopAllAsync()
        {
            Reader.Release();
            foreach (var service in started.AsEnumerable().Reverse())
            {
                await AgentSupervisionHostedSupport.StopAsync(service);
            }

            started.Clear();
        }

        public int Invocations => AgentSupervisionHostedSupport.InvocationCount(Adapter);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAllAsync();
            }
            catch (Exception)
            {
                // Disposal must not mask the test's own failure.
            }

            await Provider.DisposeAsync();
        }
    }

    /// <summary>The same composition without any observation: the terminal status, outcome and adapter cardinality the original path
    /// produces on its own.</summary>
    private static async Task<(AttemptStatus Status, AgentOutcome? Outcome, int Invocations)> BaselineAsync(
        AgentSupervisionHostedComposition composition)
    {
        await using var baseline = await Session.CreateAsync(composition, observed: false);
        await baseline.StartSupervisorAsync();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(baseline.Provider, baseline.AttemptId);
        await baseline.StopAllAsync();
        return (attempt.Status, attempt.AgentOutcome, baseline.Invocations);
    }

    private static async Task<AgentSupervision> WaitForClosedAsync(Session session) =>
        await AgentSupervisionHostedSupport.WaitAsync(async () =>
        {
            var receipt = await AgentSupervisionHostedSupport.ReceiptAsync(session.Provider, session.AttemptId);
            return receipt is { ClosedAtUtc: not null } ? receipt : null;
        });

    private static async Task<AgentSupervision> WaitForAttentionAsync(Session session) =>
        await AgentSupervisionHostedSupport.WaitAsync(async () =>
        {
            var receipt = await AgentSupervisionHostedSupport.ReceiptAsync(session.Provider, session.AttemptId);
            return receipt is { AttentionReason: not null } ? receipt : null;
        });

    private static async Task<AgentSupervision> WaitForReceiptAsync(Session session, AgentSupervisionPhase? phase = null) =>
        await AgentSupervisionHostedSupport.WaitAsync(async () =>
        {
            var receipt = await AgentSupervisionHostedSupport.ReceiptAsync(session.Provider, session.AttemptId);
            return receipt is not null && (phase is null || receipt.Phase == phase) ? receipt : null;
        });

    /// <summary>The observer's own loop reports every five seconds of real time, which a test that jumps the logical clock would never
    /// wait for. Its committed heartbeats are reproduced here in 25-second steps of the logical clock, so that only the absolute phase
    /// deadline can be the first boundary to elapse (a healthy heartbeat must never mask it); the clock then lands one second past it.</summary>
    private static async Task KeepReportingUntilAsync(Session session, AgentSupervision observation)
    {
        while (session.Clock.GetUtcNow() + TimeSpan.FromSeconds(25) < observation.PhaseDeadlineUtc)
        {
            session.Clock.Advance(TimeSpan.FromSeconds(25));
            await using var scope = session.Provider.CreateAsyncScope();
            var beat = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
                new HeartbeatAgentSupervisionCommand(session.AttemptId, session.Host.Id, observation.ObserverId), CancellationToken.None);
            Assert.True(beat.IsSuccess, beat.IsFailure ? beat.Errors[0].Code : null);
            Assert.False(beat.Value.AttentionRecorded);
        }

        session.Clock.Advance(observation.PhaseDeadlineUtc - session.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
    }

    /// <summary>The supervision events of the session's own attempt, in commit order. A composition may seed earlier observed attempts of
    /// the same run (the diagnosis path does), whose events are their own.</summary>
    private static async Task<List<RunEvent>> AttemptEventsAsync(Session session) =>
        (await AgentSupervisionHostedSupport.SupervisionEventsAsync(session.Provider, session.RunId))
            .Where(runEvent => runEvent.AttemptId == session.AttemptId)
            .ToList();

    private static async Task<GetAgentSupervisionQueryResult> ReadAsync(Session session)
    {
        await using var scope = session.Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new GetAgentSupervisionQuery(session.RunId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>The supervisor observes its attempt from entry to its terminal result and closes the observation cleanly, while the
    /// original path (status, outcome, adapter cardinality) is exactly the unobserved baseline.</summary>
    public static async Task ObservedThroughToItsTerminalResultAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true);
        await session.StartSupervisorAsync();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        var receipt = await WaitForClosedAsync(session);
        await session.StopAllAsync();

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
        AgentSupervisionHostedSupport.AssertCleanTermination(receipt, session.Host);
        Assert.Equal(AgentSupervisionPhase.RecordingResult, receipt.Phase);
        Assert.Null(receipt.AttentionReason);
        Assert.True(receipt.Revision >= 4, $"register, two phases and the closure: revision {receipt.Revision}");
        Assert.Equal(session.RunId, receipt.RunId);
        // Every material transition appended exactly one event, in order; the heartbeats appended none.
        var events = await AttemptEventsAsync(session);
        Assert.Equal(
            [
                RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionPhaseEntered, RunEventType.AgentSupervisionPhaseEntered,
                RunEventType.AgentSupervisionClosed,
            ],
            events.Select(runEvent => runEvent.EventType));

        var read = await ReadAsync(session);
        Assert.Equal(AgentSupervisionStatus.Ended, read.Status);
        Assert.Equal(attempt.Status, read.Attempt!.Status);
        Assert.Null(read.Attention);
    }

    /// <summary>A Git preparation that never returns: the independent sweep marks the elapsed Preparing deadline while the supervisor is
    /// blocked, and when the preparation finally returns every original safety check and the single invocation still run.</summary>
    public static async Task BlockedPreparationIsMarkedWhileTheOriginalPathStillCompletesAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true);
        session.Reader.HoldCall(1);
        await session.StartSupervisorAsync();
        await session.Reader.Entered.WaitAsync(TimeSpan.FromSeconds(20));
        var registered = await WaitForReceiptAsync(session, AgentSupervisionPhase.Preparing);
        Assert.Equal(0, session.Invocations);

        await KeepReportingUntilAsync(session, registered);
        await session.StartSweepAsync();
        var marked = await WaitForAttentionAsync(session);

        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, marked.AttentionReason);
        Assert.Equal(registered.PhaseDeadlineUtc, marked.AttentionAtUtc);
        Assert.Null(marked.ClosedAtUtc);
        Assert.Equal(AttemptStatus.Running, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);
        Assert.Equal(0, session.Invocations);
        var whileBlocked = await ReadAsync(session);
        Assert.Equal(AgentSupervisionStatus.NeedsAttention, whileBlocked.Status);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, whileBlocked.Attention!.RecordedReason);

        session.Reader.Release();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        var closed = await WaitForClosedAsync(session);

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, closed.ClosureReason);
        Assert.Equal(marked.AttentionReason, closed.AttentionReason);
        Assert.Equal(marked.AttentionAtUtc, closed.AttentionAtUtc);
        var events = await AttemptEventsAsync(session);
        Assert.Equal(
            [
                RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionPhaseEntered,
                RunEventType.AgentSupervisionPhaseEntered, RunEventType.AgentSupervisionClosed,
            ],
            events.Select(runEvent => runEvent.EventType));
    }

    /// <summary>A result recorder (its Git capture) that never returns after a real invocation: the sweep marks the RecordingResult
    /// deadline, and when the recorder returns the known result is still recorded exactly as the unobserved path records it, with no
    /// second invocation.</summary>
    public static async Task BlockedRecorderIsMarkedWhileTheKnownResultIsStillRecordedAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true);
        session.Reader.HoldCall(2);
        await session.StartSupervisorAsync();
        await session.Reader.Entered.WaitAsync(TimeSpan.FromSeconds(20));
        var recording = await WaitForReceiptAsync(session, AgentSupervisionPhase.RecordingResult);
        Assert.Equal(1, session.Invocations);
        Assert.Equal(AttemptStatus.Running, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);

        await KeepReportingUntilAsync(session, recording);
        await session.StartSweepAsync();
        var marked = await WaitForAttentionAsync(session);

        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, marked.AttentionReason);
        Assert.Equal(recording.PhaseDeadlineUtc, marked.AttentionAtUtc);
        Assert.Equal(AttemptStatus.Running, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);

        session.Reader.Release();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        var closed = await WaitForClosedAsync(session);

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, closed.ClosureReason);
        Assert.Equal(marked.AttentionAtUtc, closed.AttentionAtUtc);
        Assert.Equal(
            [
                RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionPhaseEntered, RunEventType.AgentSupervisionPhaseEntered,
                RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionClosed,
            ],
            (await AttemptEventsAsync(session)).Select(runEvent => runEvent.EventType));
    }

    /// <summary>The host stops while a Git preparation is blocked: disposal closes the observation from a fresh durable read (the attempt
    /// is still Running, so the observer is recorded as ended without a terminal result), the attempt is never terminalized by it and the
    /// provider is never invoked, and the existing restart reconciliation then remains the only authority over the attempt while the
    /// startup closure finds nothing left open.</summary>
    public static async Task ShutdownDuringPreparationClosesTheObservationAndLeavesTheAttemptToReconciliationAsync(
        AgentSupervisionHostedComposition composition)
    {
        await using var session = await Session.CreateAsync(composition, observed: true);
        session.Reader.HoldCall(1);
        await session.StartSupervisorAsync();
        await session.Reader.Entered.WaitAsync(TimeSpan.FromSeconds(20));
        await WaitForReceiptAsync(session, AgentSupervisionPhase.Preparing);

        await AgentSupervisionHostedSupport.StopAsync(session.Supervisor);

        var closed = await WaitForClosedAsync(session);
        Assert.Equal(AgentSupervisionClosureReason.ObserverEnded, closed.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, closed.AttentionReason);
        Assert.Equal(AttemptStatus.Running, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);
        Assert.Equal(0, session.Invocations);
        var read = await ReadAsync(session);
        Assert.Equal(AgentSupervisionStatus.NeedsAttention, read.Status);
        Assert.Equal(AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, read.ReasonCode);

        // Restart: the existing reconciliation (unchanged) decides the attempt; the startup closure has nothing left to close.
        await using (var scope = session.Provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            Assert.True((await mediator.SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None)).IsSuccess);
            Assert.True((await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None)).IsSuccess);
            var closure = await mediator.SendAsync(new CloseInterruptedAgentSupervisionsCommand(), CancellationToken.None);
            Assert.Equal(0, closure.Value.ClosedCount);
        }

        Assert.Equal(AttemptStatus.Interrupted, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);
        var afterRestart = await AgentSupervisionHostedSupport.ReceiptAsync(session.Provider, session.AttemptId);
        Assert.Equal((closed.ClosureReason, closed.AttentionReason, closed.Revision), (afterRestart!.ClosureReason, afterRestart.AttentionReason, afterRestart.Revision));
        Assert.Equal(AgentSupervisionStatus.Ended, (await ReadAsync(session)).Status);
    }

    /// <summary>An observation whose every update fails after registration: nothing reaches the original path, the known result is
    /// recorded unchanged, the receipt is left open and the independent sweep later closes it from the durable Attempt.</summary>
    public static async Task ObservationFaultsNeverChangeTheOriginalResultAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true);
        await AgentSupervisionHostedSupport.ExecuteRawAsync(
            composition.DatabasePath(),
            "CREATE TRIGGER trg_test_fault_agent_supervision_update BEFORE UPDATE ON agent_supervisions "
            + "BEGIN SELECT RAISE(ABORT, 'agent_supervision_test_fault'); END;");
        await session.StartSupervisorAsync();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        await session.StopAllAsync();

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
        var orphan = await AgentSupervisionHostedSupport.ReceiptAsync(session.Provider, session.AttemptId);
        Assert.NotNull(orphan);
        Assert.Null(orphan.ClosedAtUtc);
        Assert.Equal(AgentSupervisionPhase.Preparing, orphan.Phase);

        await AgentSupervisionHostedSupport.ExecuteRawAsync(
            composition.DatabasePath(), "DROP TRIGGER trg_test_fault_agent_supervision_update;");
        await session.StartSweepAsync();
        var closed = await WaitForClosedAsync(session);

        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, closed.ClosureReason);
        Assert.Null(closed.AttentionReason);
        Assert.Equal(attempt.Status, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);
    }

    /// <summary>The observation store is unavailable altogether (its table is gone): registration fails inertly and the original path
    /// runs to the unchanged result with the unchanged invocation count.</summary>
    public static async Task AnUnavailableObservationStoreNeverBlocksTheOriginalPathAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true);
        await AgentSupervisionHostedSupport.ExecuteRawAsync(composition.DatabasePath(), "DROP TABLE agent_supervisions;");
        await session.StartSupervisorAsync();
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        await session.StopAllAsync();

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
    }

    /// <summary>An adapter call that never returns: the sweep marks the elapsed AwaitingAdapterResult deadline (the reserved invocation
    /// timeout plus two minutes) while the adapter is held, and when it returns the known result is recorded once.</summary>
    public static async Task ABlockedAdapterIsMarkedAtItsTimeoutPlusGraceAsync(AgentSupervisionHostedComposition composition)
    {
        var hold = composition.Hold ?? throw new InvalidOperationException("This composition has no adapter hold.");
        var baseline = await BaselineAsync(composition);

        await using var session = await Session.CreateAsync(composition, observed: true, openAdapter: false);
        hold.Arm?.Invoke(session.Adapter);
        await session.StartSupervisorAsync();
        await hold.Entered(session.Adapter).WaitAsync(TimeSpan.FromSeconds(20));
        var awaiting = await WaitForReceiptAsync(session, AgentSupervisionPhase.AwaitingAdapterResult);
        Assert.Equal(1, session.Invocations);
        var attemptRow = await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId);
        Assert.Equal(awaiting.PhaseEnteredAtUtc + attemptRow.AgentTimeout!.Value + AgentSupervisionPolicy.AdapterResultGrace, awaiting.PhaseDeadlineUtc);

        await KeepReportingUntilAsync(session, awaiting);
        await session.StartSweepAsync();
        var marked = await WaitForAttentionAsync(session);
        Assert.Equal(AgentSupervisionAttentionReason.PhaseDeadlineElapsed, marked.AttentionReason);
        Assert.Equal(awaiting.PhaseDeadlineUtc, marked.AttentionAtUtc);
        Assert.Equal(AttemptStatus.Running, (await AgentSupervisionHostedSupport.AttemptAsync(session.Provider, session.AttemptId)).Status);

        hold.Release(session.Adapter);
        var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(session.Provider, session.AttemptId);
        var closed = await WaitForClosedAsync(session);

        Assert.Equal(baseline, (attempt.Status, attempt.AgentOutcome, session.Invocations));
        Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, closed.ClosureReason);
        Assert.Equal(marked.AttentionAtUtc, closed.AttentionAtUtc);
    }
}
