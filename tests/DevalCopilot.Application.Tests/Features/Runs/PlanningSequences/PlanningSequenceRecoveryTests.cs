using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordPlanningSequenceDispatchRefusal;
using DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Devalente.Shared.Results;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningSequences.PlanningSequenceTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055 recovery and refusal: a failed claim is settled by an independent read of the exact step/attempt association and
/// never retried; startup interrupts every formerly advancing sequence (Armed, preparing, awaiting a result, stopping) and nothing ever
/// resumes, redispatches or adopts a result; a linked attempt whose sequence no longer authorizes dispatch ends with the dedicated
/// outcome, proved under the write lock, and an ordinary attempt is never affected.</summary>
public sealed class PlanningSequenceRecoveryTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<Result<ResolvePlanningSequenceStepPreparationCommandResult>> ResolveAsync(
        PlanningSequenceTestScene scene, PlanningSequenceStepTicket ticket, string? failure, IPlanningSequenceStepProbe? probe = null)
    {
        await using var db = fixture.CreateContext();
        return await new ResolvePlanningSequenceStepPreparationCommandHandler(db, probe ?? scene.StepProbe, scene.Clock).HandleAsync(
            new ResolvePlanningSequenceStepPreparationCommand(ticket, failure), CancellationToken.None);
    }

    [Fact]
    public async Task A_refused_claim_that_the_independent_read_proves_absent_revokes_the_step_and_keeps_the_original_reason()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture, maximumAgentAttempts: 1);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync((await scene.ReserveAndClaimAsync(armed.SequenceId)).Claim.AttemptId, StepResult.Proposed);
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        var claim = await scene.ClaimAsync(ticket);
        Assert.Equal("agent_attempts.budget_exhausted", claim.Errors[0].Code);

        var resolved = await ResolveAsync(scene, ticket, claim.Errors[0].Code);

        Assert.True(resolved.IsSuccess);
        Assert.True(resolved.Value.Changed);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.PreparationRefused, sequence.ReasonCode);
        Assert.Equal("agent_attempts.budget_exhausted", sequence.DetailCode);
        var steps = await scene.ReadStepsAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceStepState.Revoked, steps[1].State);
        Assert.Single(await scene.ReadAttemptsAsync());

        // Nothing retries: a later observation of the ended sequence reserves no further step.
        var later = await scene.ObserveAsync(armed.SequenceId);
        Assert.Null(later.Value.ReservedStep);
        Assert.Equal(2, (await scene.ReadStepsAsync(armed.SequenceId)).Count);
    }

    [Theory]
    [InlineData(PlanningSequenceErrors.StepExpiredCode, PlanningSequenceReason.PreparationExpired)]
    [InlineData(PlanningSequenceErrors.StepSourceChangedCode, PlanningSequenceReason.AuthorityChanged)]
    [InlineData("attempts.persistence_failed", PlanningSequenceReason.PreparationFailed)]
    [InlineData("agent_attempts.lease_not_active", PlanningSequenceReason.PreparationRefused)]
    [InlineData(null, PlanningSequenceReason.PreparationRefused)]
    public async Task Each_absent_claim_failure_maps_to_one_fixed_coordinator_reason(string? failure, string reason)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);

        var resolved = await ResolveAsync(scene, ticket, failure);

        Assert.True(resolved.Value.Changed);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
    }

    [Fact]
    public async Task A_claim_that_the_independent_read_proves_persisted_changes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (ticket, _) = await scene.ReserveAndClaimAsync(armed.SequenceId);

        var resolved = await ResolveAsync(scene, ticket, "attempts.persistence_unresolved");

        Assert.True(resolved.IsSuccess);
        Assert.False(resolved.Value.Changed);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(PlanningSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task An_unresolved_read_marks_the_step_unresolved_ends_the_sequence_and_authorizes_no_second_claim()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);

        var resolved = await ResolveAsync(scene, ticket, "attempts.persistence_unresolved", new UnresolvedProbe());

        Assert.True(resolved.Value.Changed);
        Assert.Equal(PlanningSequenceStepState.Unresolved, resolved.Value.StepState);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.PreparationUnresolved, sequence.ReasonCode);
        Assert.Equal("attempts.persistence_unresolved", sequence.DetailCode);
        var late = await scene.ClaimAsync(ticket);
        Assert.True(late.IsFailure);
        Assert.Empty(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_resolution_for_a_step_a_stop_already_settled_writes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await scene.StopAsync(armed.SequenceId);

        var resolved = await ResolveAsync(scene, ticket, "agent_attempts.lease_not_active");

        Assert.False(resolved.Value.Changed);
        Assert.Equal(PlanningSequenceState.Stopped, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task Startup_interrupts_an_armed_sequence_a_preparation_only_session_a_waiting_session_and_a_stopping_one()
    {
        var armedOnly = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armedOnlyId = (await armedOnly.ArmAsync()).SequenceId;

        var preparing = await PlanningSequenceTestScene.CreateAsync(fixture);
        var preparingId = (await preparing.ArmAsync()).SequenceId;
        var reservedTicket = await preparing.ObserveReservingAsync(preparingId);

        var waiting = await PlanningSequenceTestScene.CreateAsync(fixture);
        var waitingId = (await waiting.ArmAsync()).SequenceId;
        var (_, waitingClaim) = await waiting.ReserveAndClaimAsync(waitingId);

        var stopping = await PlanningSequenceTestScene.CreateAsync(fixture);
        var stoppingId = (await stopping.ArmAsync()).SequenceId;
        await stopping.ReserveAndClaimAsync(stoppingId);
        await stopping.StopAsync(stoppingId);

        var finished = await PlanningSequenceTestScene.CreateAsync(fixture);
        var finishedId = (await finished.ArmAsync()).SequenceId;
        await finished.StopAsync(finishedId);

        // The ordinary attempt recovery runs first, exactly like Program.cs: dispatched or not, Running attempts become Interrupted.
        await using (var recovery = fixture.CreateContext())
        {
            await new ReconcileInterruptedAgentAttemptsCommandHandler(recovery, new FixedTimeProvider(Start.AddMinutes(1)))
                .HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
        }

        await using var db = fixture.CreateContext();
        var interrupted = await new InterruptPlanningSequencesCommandHandler(db, new FixedTimeProvider(Start.AddMinutes(2)))
            .HandleAsync(new InterruptPlanningSequencesCommand(), CancellationToken.None);

        Assert.Equal(4, interrupted.Value.InterruptedCount);
        foreach (var (scene, id) in new[] { (armedOnly, armedOnlyId), (preparing, preparingId), (waiting, waitingId), (stopping, stoppingId) })
        {
            var sequence = await scene.ReadSequenceAsync(id);
            Assert.Equal(PlanningSequenceState.Interrupted, sequence.State);
            Assert.Equal(PlanningSequenceReason.HostRestarted, sequence.ReasonCode);
        }

        // An already ended sequence keeps its own state; the reserved step with no attempt is revoked, never adopted or resumed.
        Assert.Equal(PlanningSequenceState.Stopped, (await finished.ReadSequenceAsync(finishedId)).State);
        Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await preparing.ReadStepsAsync(preparingId)).State);
        Assert.Empty(await preparing.ReadAttemptsAsync());
        Assert.Empty(await armedOnly.ReadStepsAsync(armedOnlyId));
        Assert.Equal(PlanningSequenceStepState.Claimed, Assert.Single(await waiting.ReadStepsAsync(waitingId)).State);
        // The never-dispatched claimed attempt stays Running after ordinary recovery; the interrupted sequence refuses its dispatch,
        // so it is never redispatched (the dedicated refusal ends it).
        var waitingAttempt = Assert.Single(await waiting.ReadAttemptsAsync());
        Assert.Equal(AttemptStatus.Running, waitingAttempt.Status);
        Assert.Equal(
            MarkAgentAttemptDispatchedCommandHandler.PlanningSequenceDispatchRefusedCode,
            (await TryDispatchAsync(waiting, waitingAttempt.Id)).Errors[0].Code);
        Assert.Equal(reservedTicket.ReservedAttemptId, Assert.Single(await preparing.ReadStepsAsync(preparingId)).ReservedAttemptId);
        Assert.Equal(waitingClaim.AttemptId, Assert.Single(await waiting.ReadAttemptsAsync()).Id);
    }

    [Fact]
    public async Task An_interrupted_sequence_is_never_resumed_or_advanced_and_a_late_result_creates_no_successor()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);
        await using (var db = fixture.CreateContext())
        {
            await new InterruptPlanningSequencesCommandHandler(db, scene.Clock).HandleAsync(
                new InterruptPlanningSequencesCommand(), CancellationToken.None);
        }

        await scene.CompleteStepAsync(claim.AttemptId, StepResult.Proposed);
        var observed = await scene.ObserveAsync(armed.SequenceId);
        var stopped = await scene.StopAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        Assert.False(stopped.Value.Changed);
        Assert.Equal(PlanningSequenceState.Interrupted, stopped.Value.State);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Single(await scene.ReadAttemptsAsync());
        var replay = await scene.TryArmAsync();
        Assert.True(replay.Value.Replayed);
        Assert.Equal(PlanningSequenceState.Interrupted, replay.Value.State);
    }

    [Fact]
    public async Task Startup_interruption_is_repeatable_and_touches_nothing_that_already_ended()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        await scene.ArmAsync();

        await using var db = fixture.CreateContext();
        var handler = new InterruptPlanningSequencesCommandHandler(db, scene.Clock);
        Assert.Equal(1, (await handler.HandleAsync(new InterruptPlanningSequencesCommand(), CancellationToken.None)).Value.InterruptedCount);
        Assert.Equal(0, (await handler.HandleAsync(new InterruptPlanningSequencesCommand(), CancellationToken.None)).Value.InterruptedCount);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceEnded));
    }

    private async Task<Guid> ClaimedAttemptAsync(PlanningSequenceTestScene scene, Guid sequenceId) =>
        (await scene.ReserveAndClaimAsync(sequenceId)).Claim.AttemptId;

    private async Task EndSequenceAsync(Guid sequenceId, PlanningSequenceState state, string reason)
    {
        await using var context = fixture.CreateContext();
        var sequence = await context.PlanningSequences.SingleAsync(candidate => candidate.Id == sequenceId);
        sequence.End(state, reason, null, Start);
        await context.SaveChangesAsync();
    }

    private async Task<Result<MarkAgentAttemptDispatchedCommandResult>> TryDispatchAsync(PlanningSequenceTestScene scene, Guid attemptId, IDevalCopilotDbContext? db = null)
    {
        await using var owned = db is null ? fixture.CreateContext() : null;
        var context = db ?? owned!;
        var result = await new MarkAgentAttemptDispatchedCommandHandler(context, scene.Clock).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.RunId, attemptId), CancellationToken.None);
        if (result.IsSuccess)
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }

        return result;
    }

    private async Task<Result> TryRecordRefusalAsync(PlanningSequenceTestScene scene, Guid attemptId)
    {
        await using var db = fixture.CreateContext();
        return await new RecordPlanningSequenceDispatchRefusalCommandHandler(db, scene.Clock).HandleAsync(
            new RecordPlanningSequenceDispatchRefusalCommand(scene.RunId, attemptId), CancellationToken.None);
    }

    [Fact]
    public async Task A_linked_attempt_of_a_live_sequence_dispatches_and_a_stopping_sequence_retains_its_claimed_steps_grant()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimedAttemptAsync(scene, armed.SequenceId);
        await scene.StopAsync(armed.SequenceId);

        var dispatched = await TryDispatchAsync(scene, attemptId);

        Assert.True(dispatched.IsSuccess, dispatched.IsFailure ? dispatched.Errors[0].Code : null);
        Assert.NotNull(Assert.Single(await scene.ReadAttemptsAsync()).AgentDispatchedAtUtc);
        Assert.Equal(PlanningSequenceState.Stopping, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
    }

    [Theory]
    [InlineData(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.PreparationExpired)]
    [InlineData(PlanningSequenceState.Interrupted, PlanningSequenceReason.HostRestarted)]
    [InlineData(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed)]
    public async Task An_attempt_whose_sequence_no_longer_authorizes_dispatch_is_refused_and_ends_with_the_dedicated_outcome(
        PlanningSequenceState state, string reason)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimedAttemptAsync(scene, armed.SequenceId);
        await EndSequenceAsync(armed.SequenceId, state, reason);

        var dispatched = await TryDispatchAsync(scene, attemptId);

        Assert.True(dispatched.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.PlanningSequenceDispatchRefusedCode, dispatched.Errors[0].Code);
        Assert.NotEqual(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, dispatched.Errors[0].Code);
        Assert.Null(Assert.Single(await scene.ReadAttemptsAsync()).AgentDispatchedAtUtc);

        var recorded = await TryRecordRefusalAsync(scene, attemptId);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        var attempt = Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.PlanningSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        // No protocol output was written and no budget was refunded: the claim's slot stays consumed.
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.CollaborationMessages.AsNoTracking().Where(message => message.RunId == scene.RunId).ToListAsync());
        Assert.Equal(1, attempt.AgentBudgetSlot);
    }

    [Fact]
    public async Task An_elapsed_result_deadline_refuses_dispatch_even_before_the_sweep_persisted_the_state()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimedAttemptAsync(scene, armed.SequenceId);
        scene.Clock.Advance(TimeSpan.FromMinutes(12));

        var dispatched = await TryDispatchAsync(scene, attemptId);

        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.PlanningSequenceDispatchRefusedCode, dispatched.Errors[0].Code);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.True((await TryRecordRefusalAsync(scene, attemptId)).IsSuccess);
        Assert.Equal(AgentOutcome.PlanningSequenceDispatchRefused, Assert.Single(await scene.ReadAttemptsAsync()).AgentOutcome);
    }

    [Fact]
    public async Task The_recorder_never_fabricates_an_outcome_for_a_dispatchable_dispatched_or_unlinked_attempt()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimedAttemptAsync(scene, armed.SequenceId);

        // Still dispatchable: no refusal exists to record.
        var dispatchable = await TryRecordRefusalAsync(scene, attemptId);
        Assert.True(dispatchable.IsFailure);
        Assert.Equal("attempts.not_eligible", dispatchable.Errors[0].Code);

        // Dispatched: the provider may have run; only its real result can end it.
        Assert.True((await TryDispatchAsync(scene, attemptId)).IsSuccess);
        await EndSequenceAsync(armed.SequenceId, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed);
        var dispatched = await TryRecordRefusalAsync(scene, attemptId);
        Assert.True(dispatched.IsFailure);
        Assert.Equal(AttemptStatus.Running, Assert.Single(await scene.ReadAttemptsAsync()).Status);

        // Unlinked: an ordinary attempt that no step names.
        var ordinary = await PlanningSequenceTestScene.CreateAsync(fixture);
        Guid ordinaryAttempt;
        await using (var context = fixture.CreateContext())
        {
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), ordinary.RunId, 1, ordinary.WorkspaceId, ordinary.CheckpointId, ordinary.FingerprintSha256, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Start, 1);
            ordinaryAttempt = attempt.Id;
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        var unlinked = await TryRecordRefusalAsync(ordinary, ordinaryAttempt);
        Assert.True(unlinked.IsFailure);
        Assert.Equal(AttemptStatus.Running, Assert.Single(await ordinary.ReadAttemptsAsync()).Status);
    }

    [Fact]
    public async Task An_attempt_that_no_step_names_dispatches_exactly_as_before()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        Guid attemptId;
        await using (var context = fixture.CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
            run.Claim(Start);
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, scene.FingerprintSha256, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Start, 1);
            attemptId = attempt.Id;
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        var dispatched = await TryDispatchAsync(scene, attemptId);

        Assert.True(dispatched.IsSuccess, dispatched.IsFailure ? dispatched.Errors[0].Code : null);
    }

    [Fact]
    public async Task A_sequence_that_ends_between_the_gate_and_the_dispatch_commit_refuses_the_marker_by_its_revision_guard()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimedAttemptAsync(scene, armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ => await EndSequenceAsync(armed.SequenceId, PlanningSequenceState.NeedsAttention, PlanningSequenceReason.PreparationExpired),
        };
        var handlerResult = await new MarkAgentAttemptDispatchedCommandHandler(faulting, scene.Clock).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.RunId, attemptId), CancellationToken.None);
        Assert.True(handlerResult.IsSuccess);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => faulting.SaveChangesAsync(CancellationToken.None));

        Assert.Null(Assert.Single(await scene.ReadAttemptsAsync()).AgentDispatchedAtUtc);
    }

    private sealed class UnresolvedProbe : IPlanningSequenceStepProbe
    {
        public Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanningSequenceStepProbeResult.Unresolved);
    }
}
