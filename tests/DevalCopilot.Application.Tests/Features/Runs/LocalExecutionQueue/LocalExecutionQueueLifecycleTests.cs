using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>
/// Stop, deadline, uncertainty and startup (ADR-0062) over real SQLite: a waiting cycle retires atomically with zero claims; an unused
/// reservation is retired only when its whole identity, its unlinked Reserved shape and the absence of the reserved operation in both
/// operation families are proved under the write lock, with its old token fenced in the same commit; anything contradictory or
/// uncertain stays held as Unresolved ownership with attention; claimed and dispatched ownership and the quarantine proof are unchanged;
/// and a restart closes every formerly waiting or advancing cycle without resuming, redispatching or rebuilding a ticket.
/// </summary>
public sealed class LocalExecutionQueueLifecycleTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private sealed class FixedProbe(LocalExecutionStepProbeResult result) : ILocalExecutionStepProbe
    {
        public Task<LocalExecutionStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> QueuedAsync(int recipes = 1)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.QueueAsync(plan.ProposalId)).SequenceId);
    }

    // ---- stop ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_while_waiting_retires_the_admission_and_ends_stopped_atomically_with_zero_claims()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var before = await scene.FootprintAsync();

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.IsSuccess);
        Assert.True(stopped.Value.Changed);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, sequence.ReasonCode);
        Assert.NotNull(sequence.StopRequestedAtUtc);
        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Retired, admission.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, admission.ReasonCode);
        Assert.Empty(await scene.ReadStepsAsync());
        var after = await scene.FootprintAsync();
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(before.Executions, after.Executions);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStopRequested));
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));

        // Idempotent, and no later reservation can start from the retired admission.
        var again = await scene.StopAsync(sequenceId);
        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Changed);
        Assert.Null(await scene.ReserveAsync());
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task Stop_of_an_unused_reservation_retires_it_under_the_lock_and_fences_the_old_token_in_the_same_commit()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await scene.AssessWorkspaceAsync()).Status);

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.IsSuccess);
        Assert.Equal(LocalExecutionSequenceState.Stopped, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync()).State);

        // The retirement committed: the workspace and the global unit are free immediately, never "after the time elapsed".
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync()).RecordedOwners);

        // A late worker holding the old ticket cannot claim: its token is fenced.
        var late = await scene.ClaimAsync(ticket);
        Assert.True(late.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, late.Errors[0].Code);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task A_claim_that_committed_first_wins_the_race_with_stop_and_the_claimed_operation_drains()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        var claim = await scene.ClaimAsync(ticket);
        Assert.True(claim.IsSuccess);

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.IsSuccess);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopping, sequence.State);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        await scene.CompleteClaimedAsync(ticket);
        await scene.ObserveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.Stopped, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedAfterOperation, (await scene.ReadSequenceAsync()).ReasonCode);
        Assert.Single(await scene.ReadAdmissionsAsync());
    }

    [Fact]
    public async Task A_reservation_whose_reserved_operation_is_already_present_cannot_be_proved_unused_and_stays_held_on_stop()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await scene.PlantReservedOperationAsync(ticket);

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.IsSuccess, stopped.IsFailure ? stopped.Errors[0].Code : null);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, Assert.Single(await scene.ReadStepsAsync()).State);

        // Uncertainty is never a release or a convenient count: the unresolved reservation and the present operation are two recorded
        // owners of one workspace, which is conflicted, and the global reading is unavailable rather than a smaller number.
        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync()).Status);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
    }

    // ---- expiry ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_expired_unused_reservation_is_retired_only_by_the_proof_never_by_elapsed_time_alone()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        scene.Clock.Advance(TimeSpan.FromMinutes(3));

        // Before the observation committed the retirement, the elapsed reservation still counts and still owns its workspace.
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.True(observed.IsSuccess);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync()).RecordedOwners);
        var late = await scene.ClaimAsync(ticket);
        Assert.True(late.IsFailure);
    }

    [Fact]
    public async Task The_original_two_hour_deadline_retires_a_reservation_whose_preparation_window_is_still_open()
    {
        var (scene, sequenceId) = await QueuedAsync();
        await scene.ReserveAsync();
        scene.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

        await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, (await scene.ReadSequenceAsync()).ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
    }

    [Fact]
    public async Task A_refused_claim_with_a_probe_that_proves_absence_retires_the_reservation_and_ends_with_attention_without_a_retry()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        scene.Clock.Advance(TimeSpan.FromMinutes(3));
        var claim = await scene.ClaimAsync(ticket);
        Assert.True(claim.IsFailure);

        var resolved = await scene.ResolveAsync(ticket, claim.Errors[0].Code);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Null(await scene.ReserveAsync());
        Assert.Equal(sequenceId, (await scene.ReadSequenceAsync()).Id);
    }

    [Fact]
    public async Task An_unanswered_durability_probe_keeps_the_reservation_held_as_unresolved_ownership()
    {
        var (scene, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;

        var resolved = await scene.ResolveAsync(
            ticket, LocalExecutionSequenceErrors.PersistenceUnresolvedCode, new FixedProbe(LocalExecutionStepProbeResult.Unresolved));

        Assert.True(resolved.IsSuccess);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await scene.AssessWorkspaceAsync()).Status);

        // The held reservation still counts toward the four held preparations and is never offered again.
        Assert.Null(await scene.ReserveAsync());
    }

    [Fact]
    public async Task A_probe_that_says_absent_never_retires_a_reservation_whose_operation_row_exists_under_the_lock()
    {
        var (scene, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await scene.PlantReservedOperationAsync(ticket);

        await scene.ResolveAsync(ticket, "attempts.persistence_failed", new FixedProbe(LocalExecutionStepProbeResult.NotClaimed));

        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, (await scene.ReadSequenceAsync()).ReasonCode);
    }

    // ---- quarantine is unchanged -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dispatched_operation_without_exit_proof_stays_the_one_quarantined_owner_after_its_admission_was_consumed()
    {
        var (scene, _) = await QueuedAsync();
        var host = new LocalExecutionQueueHost((scene, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit)));

        await host.RunUntilAllEndedAsync();

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.ExitEvidenceMissing, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        var status = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.True(status.Sequence!.AwaitingReconciliation);
        Assert.Null(status.Sequence.Queue);
        Assert.Equal("no_supported_recovery_action_in_this_version", status.Sequence.NextActionCode);

        // The operation alone owns the workspace and one global unit: the consumed reservation is not counted a second time.
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);
        Assert.Null(await scene.ReserveAsync());
    }

    // ---- startup ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Startup_closes_a_waiting_queued_cycle_as_interrupted_and_never_resumes_or_rebuilds_a_ticket()
    {
        var (scene, _) = await QueuedAsync();
        var before = await scene.FootprintAsync();

        var closure = await scene.InterruptAsync();

        Assert.Equal(1, closure.InterruptedCount);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, sequence.ReasonCode);
        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Retired, admission.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, admission.ReasonCode);

        // After the restart nothing is resumed: no reservation, no step, no operation.
        Assert.Null(await scene.ReserveAsync());
        Assert.Equal(0, (await scene.ObserveWholeQueueAsync()));
        Assert.Empty(await scene.ReadStepsAsync());
        Assert.Equal(before.Attempts, (await scene.FootprintAsync()).Attempts);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task Startup_retires_a_proved_unused_reservation_and_holds_a_contradictory_one_without_a_release()
    {
        var (unused, _) = await QueuedAsync();
        var (contradictory, _) = await QueuedAsync();
        var unusedTicket = (await unused.ReserveAsync())!;
        var contradictoryTicket = (await contradictory.ReserveAsync())!;
        await contradictory.PlantReservedOperationAsync(contradictoryTicket);

        var closure = await unused.InterruptAsync();

        Assert.Equal(2, closure.InterruptedCount);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await unused.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await unused.ReadStepsAsync()).State);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await unused.AssessWorkspaceAsync()).Status);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(await contradictory.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, Assert.Single(await contradictory.ReadStepsAsync()).State);
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, (await contradictory.AssessWorkspaceAsync()).Status);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await contradictory.ReadSequenceAsync()).State);

        // The old tokens are fenced for both: a worker of the previous launch cannot claim.
        Assert.True((await unused.ClaimAsync(unusedTicket)).IsFailure);
        Assert.True((await contradictory.ClaimAsync(contradictoryTicket)).IsFailure);
    }

    [Fact]
    public async Task Startup_never_releases_a_claimed_or_dispatched_operation_and_keeps_the_exact_undispatched_refusal()
    {
        var (undispatched, undispatchedSequence) = await QueuedAsync();
        var undispatchedTicket = (await undispatched.ReserveAsync())!;
        Assert.True((await undispatched.ClaimAsync(undispatchedTicket)).IsSuccess);
        var (dispatched, _) = await QueuedAsync();
        var dispatchedTicket = (await dispatched.ReserveAsync())!;
        Assert.True((await dispatched.ClaimAsync(dispatchedTicket)).IsSuccess);
        await dispatched.DispatchAsync(dispatchedTicket.Kind, dispatchedTicket.ReservedOperationId);

        var closure = await undispatched.InterruptAsync();

        Assert.Equal(2, closure.InterruptedCount);
        Assert.Equal(1, closure.RefusedUndispatchedCount);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await undispatched.ReadSequenceAsync()).State);
        var refused = (await undispatched.ReadAttemptsAsync()).Single(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, refused.AgentOutcome);

        // The dispatched operation stays an unresolved owner of its workspace and one unit: no false terminal row and no release.
        var running = (await dispatched.ReadAttemptsAsync()).Single(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(AttemptStatus.Running, running.Status);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await dispatched.AssessWorkspaceAsync()).Status);
        Assert.True((await dispatched.ReadCapacityAsync()).RecordedOwners >= 1);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await dispatched.ReadStatusAsync()).ManualFence);
        Assert.Equal(undispatchedSequence, (await undispatched.ReadSequenceAsync()).Id);
    }

    [Fact]
    public async Task Startup_closes_a_queued_cycle_that_was_waiting_between_operations_and_never_reserves_its_pending_step()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await scene.DriveTicketAsync(ticket);
        await scene.ObserveAsync(sequenceId);
        var pending = Assert.Single(await scene.ReadAdmissionsAsync(), admission => admission.State == LocalExecutionAdmissionState.Waiting);
        Assert.Equal(Step.Verification, pending.Kind);

        await scene.InterruptAsync();

        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        var admissions = await scene.ReadAdmissionsAsync();
        Assert.Equal(LocalExecutionAdmissionState.Retired, admissions.Single(admission => admission.Id == pending.Id).State);
        Assert.Equal(LocalExecutionAdmissionState.Consumed, admissions.Single(admission => admission.Id != pending.Id).State);
        Assert.Null(await scene.ReserveAsync());
        Assert.Single(await scene.ReadStepsAsync());
    }

    [Fact]
    public async Task A_late_result_of_the_original_host_never_revives_or_advances_an_ended_queued_cycle()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.InterruptAsync();

        // The original live host may still record its own result under the ordinary rules, but it cannot reopen the ended sequence.
        await scene.RecordAsync(
            new LocalExecutionStepContext(ticket.Kind, 0, null, ticket.Ordinal, ticket.ReservedOperationId, 1), ScriptedResultKind.Implemented);
        var observed = await scene.ObserveAsync(sequenceId);

        Assert.False(observed.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Null(await scene.ReserveAsync());
    }
}
