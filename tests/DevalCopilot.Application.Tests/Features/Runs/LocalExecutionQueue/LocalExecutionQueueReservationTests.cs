using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>
/// Admission selection and pre-claim reservation (ADR-0062) over real SQLite with independent connections: the first-in-first-out choice
/// among eligible queued steps, independent projects never blocked behind another workspace, both limits, the four-preparation bound,
/// a reservation as the exclusive owner of its workspace and (when mutating) of one global unit, exact self-exclusion and the atomic
/// transfer into the claim's own save.
/// </summary>
public sealed class LocalExecutionQueueReservationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid Plan, Guid SequenceId)> QueuedAsync(int recipes = 1)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipes);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var receipt = await scene.QueueAsync(plan.ProposalId);
        return (scene, plan.ProposalId, receipt.SequenceId);
    }

    /// <summary>Drives the only queued cycle through its implementation and its one verification, then records its next pending admission
    /// without reserving it: the cycle waits with a ReadOnly code review at the tail of the queue.</summary>
    private static async Task<Guid> DriveToWaitingReviewAsync(LocalExecutionSequenceTestScene scene, Guid sequenceId)
    {
        while (true)
        {
            var ticket = await scene.ReserveAsync();
            Assert.NotNull(ticket);
            await scene.DriveTicketAsync(ticket);
            await scene.ObserveAsync(sequenceId);
            if (ticket.Kind == Step.Verification)
            {
                break;
            }
        }

        var review = Assert.Single(await scene.ReadAdmissionsAsync(), admission => admission.State == LocalExecutionAdmissionState.Waiting);
        Assert.Equal(Step.CodeReview, review.Kind);
        return review.Id;
    }

    // ---- a reservation is an exclusive owner -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_reservation_atomically_creates_the_reserved_step_and_token_and_owns_its_workspace_and_one_global_unit()
    {
        var (scene, _, sequenceId) = await QueuedAsync();

        var ticket = await scene.ReserveAsync();

        Assert.NotNull(ticket);
        Assert.Equal(sequenceId, ticket.SequenceId);
        Assert.Equal(Step.Implementation, ticket.Kind);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, sequence.State);
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
        Assert.Equal(ticket.StepId, step.Id);
        Assert.Equal(ticket.ReservedOperationId, step.ReservedOperationId);
        Assert.Equal(step.PreparationReservedAtUtc.AddMinutes(2), step.PreparationDeadlineUtc);
        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Reserved, admission.State);
        Assert.Equal(step.Id, admission.StepId);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStepReserved));

        // Reservation ownership, derived from the same rows every entrance reads.
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);

        // Reserving claims nothing: no operation exists and no budget was consumed.
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task Waiting_owns_no_workspace_and_no_global_unit_and_no_operation_budget_or_slot()
    {
        var (scene, _, _) = await QueuedAsync();
        await scene.ObserveWholeQueueAsync();

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync()).RecordedOwners);
        Assert.Empty(await scene.ReadStepsAsync());
        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, admission.State);
    }

    [Fact]
    public async Task Exact_self_exclusion_needs_the_whole_internal_ticket_proof_and_a_forged_ticket_makes_the_reading_unavailable()
    {
        var (scene, _, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync(ticket)).Status);
        Assert.Equal(0, (await scene.ReadCapacityAsync(ticket)).RecordedOwners);

        foreach (var forged in new[]
        {
            LocalExecutionSequenceTestScene.Forge(ticket, ownerToken: Guid.NewGuid()),
            LocalExecutionSequenceTestScene.Forge(ticket, reservedOperationId: Guid.NewGuid()),
            LocalExecutionSequenceTestScene.Forge(ticket, stepId: Guid.NewGuid()),
            LocalExecutionSequenceTestScene.Forge(ticket, kind: Step.CodeReview),
            LocalExecutionSequenceTestScene.Forge(ticket, ordinal: ticket.Ordinal + 1),
            LocalExecutionSequenceTestScene.Forge(ticket, sequenceId: Guid.NewGuid()),
        })
        {
            Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync(forged)).Status);
            Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync(forged)).Status);
        }

        // A ticket never subtracts by an identifier alone: the unforged reading is still owned by the reservation.
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);
    }

    [Fact]
    public async Task The_claim_transfers_ownership_into_the_operation_in_its_own_save_with_exactly_one_owner_before_and_after()
    {
        var (scene, _, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        var admission = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Consumed, admission.State);
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        var attempt = Assert.Single(await scene.ReadAttemptsAsync(), candidate => candidate.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(step.OperationId, attempt.Id);
        Assert.Equal(AttemptStatus.Running, attempt.Status);

        // One effective owner after the transfer: the operation. The reservation is Consumed and owns nothing; no double count.
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);
    }

    [Fact]
    public async Task A_failed_claim_save_leaves_the_reservation_held_with_no_operation_and_never_retries()
    {
        var (scene, _, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        var faulty = new FaultInjectingDbContext(fixture.CreateContext())
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };

        var claim = await scene.ClaimAsync(ticket, mediator: scene.MediatorWith(() => faulty));

        Assert.True(claim.IsFailure);
        Assert.Equal(LocalExecutionAdmissionState.Reserved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(1, (await scene.ReadCapacityAsync()).RecordedOwners);
    }

    // ---- selection: first-in-first-out among eligible work ----------------------------------------------------------------------

    [Fact]
    public async Task The_oldest_eligible_admission_for_a_workspace_is_reserved_and_the_younger_waits_for_the_workspace_then_takes_its_turn()
    {
        var (older, _, olderSequence) = await QueuedAsync();
        var younger = await older.SiblingRunAsync();
        var youngerPlan = await younger.AddAcceptedRootPlanAsync();
        var youngerReceipt = await younger.QueueAsync(youngerPlan.ProposalId);

        var first = await older.ReserveAsync();
        Assert.Equal(olderSequence, first!.SequenceId);
        Assert.Null(await older.ReserveAsync());
        await older.ObserveWholeQueueAsync();
        var waiting = Assert.Single(await younger.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, waiting.State);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, waiting.WaitingReason);
        Assert.True(waiting.AdmissionNumber > (await older.ReadAdmissionsAsync()).Single().AdmissionNumber);

        // The older one stops before any claim: its reservation is retired and the younger one is now the oldest eligible.
        Assert.True((await older.StopAsync(olderSequence)).IsSuccess);
        var second = await older.ReserveAsync();
        Assert.Equal(youngerReceipt.SequenceId, second!.SequenceId);
    }

    [Fact]
    public async Task A_blocked_workspace_never_blocks_an_independent_project_whatever_the_age_of_the_queued_work()
    {
        var (blocked, _, blockedSequence) = await QueuedAsync();
        await blocked.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        var (independent, _, independentSequence) = await QueuedAsync();

        var ticket = await blocked.ReserveAsync();

        Assert.NotNull(ticket);
        Assert.Equal(independentSequence, ticket.SequenceId);
        await blocked.ObserveWholeQueueAsync();
        var waiting = Assert.Single(await blocked.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, waiting.State);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, waiting.WaitingReason);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, (await blocked.ReadSequenceAsync()).State);
        Assert.Equal(blockedSequence, (await blocked.ReadSequenceAsync()).Id);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await independent.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task The_global_limit_makes_the_second_mutating_admission_wait_and_a_read_only_reservation_consumes_no_unit()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var (first, _, firstSequence) = await QueuedAsync();
        var (second, _, secondSequence) = await QueuedAsync();

        var firstTicket = await first.ReserveAsync();
        Assert.Equal(firstSequence, firstTicket!.SequenceId);
        Assert.Null(await first.ReserveAsync());
        await first.ObserveWholeQueueAsync();
        var waiting = Assert.Single(await second.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionWaitingReason.WaitingForCapacity, waiting.WaitingReason);

        // Releasing the first reservation (a stop before its claim) frees the unit; the second now reserves it.
        Assert.True((await first.StopAsync(firstSequence)).IsSuccess);
        var secondTicket = await first.ReserveAsync();
        Assert.Equal(secondSequence, secondTicket!.SequenceId);
        Assert.Equal(1, (await first.ReadCapacityAsync()).RecordedOwners);
    }

    [Fact]
    public async Task A_read_only_step_reserves_its_workspace_without_a_global_unit_while_a_mutating_step_of_another_project_still_fits()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var (review, _, reviewSequence) = await QueuedAsync();
        await DriveToWaitingReviewAsync(review, reviewSequence);
        var (other, _, otherSequence) = await QueuedAsync();

        var readOnly = await review.ReserveAsync();

        // The older queued step is the read-only review: it owns its workspace but consumes no mutating unit, so the limit of one is
        // still free for the younger mutating implementation of another project.
        Assert.Equal(reviewSequence, readOnly!.SequenceId);
        Assert.Equal(Step.CodeReview, readOnly.Kind);
        Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await review.AssessWorkspaceAsync()).Status);
        Assert.Equal(0, (await review.ReadCapacityAsync()).RecordedOwners);
        var mutating = await review.ReserveAsync();
        Assert.Equal(otherSequence, mutating!.SequenceId);
        Assert.Equal(1, (await review.ReadCapacityAsync()).RecordedOwners);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await other.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task At_most_four_preparations_are_held_and_the_fifth_eligible_admission_waits_for_preparation_capacity()
    {
        // Three cycles of three projects each reserve their ReadOnly review (no mutating unit) one after the other, so the oldest
        // eligible admission is always the one just recorded.
        var reviews = new List<LocalExecutionSequenceTestScene>();
        for (var index = 0; index < 3; index++)
        {
            var (scene, _, sequenceId) = await QueuedAsync();
            await DriveToWaitingReviewAsync(scene, sequenceId);
            var held = await scene.ReserveAsync();
            Assert.Equal(Step.CodeReview, held!.Kind);
            reviews.Add(scene);
        }

        var (fourth, _, _) = await QueuedAsync();
        var (fifth, _, _) = await QueuedAsync();
        Assert.Equal(Step.Implementation, (await fourth.ReserveAsync())!.Kind);
        Assert.Equal(4, (await fourth.ReadAllAdmissionsAsync()).Count(admission => admission.State == LocalExecutionAdmissionState.Reserved));

        Assert.Null(await fifth.ReserveAsync());
        await fifth.ObserveWholeQueueAsync();
        var waiting = Assert.Single(await fifth.ReadAdmissionsAsync());
        Assert.Equal(LocalExecutionAdmissionState.Waiting, waiting.State);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForPreparationCapacity, waiting.WaitingReason);

        // Retiring one held reservation (a stop before any claim) frees a place for the fifth.
        var stopped = await reviews[0].ReadSequenceAsync();
        Assert.True((await reviews[0].StopAsync(stopped.Id)).IsSuccess);
        Assert.NotNull(await fifth.ReserveAsync());
    }

    [Fact]
    public async Task An_unavailable_in_process_worker_only_labels_the_waiting_reason_and_never_bypasses_the_database()
    {
        var (scene, _, _) = await QueuedAsync();

        await scene.ObserveWholeQueueAsync(workerAvailable: false);

        Assert.Equal(LocalExecutionWaitingReason.WaitingForPreparationCapacity, Assert.Single(await scene.ReadAdmissionsAsync()).WaitingReason);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);

        // The database still decides: a later reservation with a worker succeeds.
        Assert.NotNull(await scene.ReserveAsync());
    }

    [Fact]
    public async Task An_unchanged_observation_records_no_new_reason_event_or_timestamp_and_a_change_records_exactly_one()
    {
        var (scene, _, _) = await QueuedAsync();
        await scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);

        await scene.ObserveWholeQueueAsync();
        var recorded = Assert.Single(await scene.ReadAdmissionsAsync());
        var events = await scene.CountEventsAsync(RunEventType.LocalExecutionAdmissionWaiting);
        Assert.Equal(1, events);

        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await scene.ObserveWholeQueueAsync();
        await scene.ObserveWholeQueueAsync();

        var again = Assert.Single(await scene.ReadAdmissionsAsync());
        Assert.Equal(recorded.Revision, again.Revision);
        Assert.Equal(recorded.WaitingReasonRecordedAtUtc, again.WaitingReasonRecordedAtUtc);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionAdmissionWaiting));

        await scene.ObserveWholeQueueAsync(workerAvailable: true);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionAdmissionWaiting));
    }

    // ---- limit orderings -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lowering_the_limit_counts_the_mutating_reservation_as_a_recorded_owner()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        var (scene, _, _) = await QueuedAsync();
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await scene.ReserveAsync();

        await using var limitContext = fixture.CreateContext();
        var lowered = await new DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit.SetExecutionCapacityLimitCommandHandler(limitContext)
            .HandleAsync(new DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit.SetExecutionCapacityLimitCommand(1), CancellationToken.None);

        Assert.True(lowered.IsFailure);
        Assert.Equal(ExecutionCapacityErrors.LimitBelowRecordedOwnersCode, lowered.Errors[0].Code);
    }

    [Fact]
    public async Task A_limit_lowered_after_a_reservation_refuses_its_claim_with_attention_and_retires_the_unused_reservation_without_a_retry()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        var (scene, _, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(ExecutionCapacityErrors.LimitReachedCode, claim.Errors[0].Code);
        await scene.ResolveAsync(ticket, claim.Errors[0].Code);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.CapacityRefused, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Equal(sequenceId, sequence.Id);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
    }

    // ---- every other entrance obeys the reservation ----------------------------------------------------------------------------

    [Fact]
    public async Task Every_other_entrance_obeys_a_held_reservation_through_the_shared_workspace_and_capacity_policies()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);
        var (scene, _, _) = await QueuedAsync();
        await scene.ReserveAsync();

        await using var context = fixture.CreateContext();
        var workspace = await WorkspaceExecutionFence.CheckClaimAsync(context, scene.ProjectId, scene.WorkspaceId, CancellationToken.None);
        var capacity = await ExecutionCapacityFence.CheckClaimAsync(context, CancellationToken.None);

        Assert.Equal(WorkspaceExecutionErrors.PreparationReservedCode, workspace!.Code);
        Assert.Equal(ExecutionCapacityErrors.LimitReachedCode, capacity!.Code);

        // A manual implementation claim of another run of the same project is refused by that very policy, with no residue.
        var sibling = await scene.SiblingRunAsync();
        var siblingPlan = await sibling.AddAcceptedRootPlanAsync();
        var before = await sibling.FootprintAsync();
        var claim = await sibling.Mediator.SendAsync(
            new DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommand(
                sibling.RunId, siblingPlan.ProposalId));
        Assert.True(claim.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.PreparationReservedCode, claim.Errors[0].Code);
        Assert.Equal(before.Attempts, (await sibling.FootprintAsync()).Attempts);
    }

    [Fact]
    public async Task A_held_reservation_blocks_the_abandonment_of_any_run_of_its_project_and_the_dispatch_of_any_other_owner_of_its_workspace()
    {
        var (scene, _, _) = await QueuedAsync();
        await scene.ReserveAsync();
        await using var context = fixture.CreateContext();

        var blocker = await DevalCopilot.Application.Features.Runs.Policies.Abandonment.RunAbandonmentAuthority.FindBlockerAsync(
            context, scene.ProjectId, CancellationToken.None);
        Assert.Equal(RunAbandonmentErrors.WorkspaceBusyCode, blocker!.Code);

        // A foreign owner recorded on the reserved workspace is judged beside the reservation: nothing is dispatched into a workspace
        // that a reservation still owns, even excluding that owner's own row.
        var owner = await scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        var dispatch = await WorkspaceExecutionFence.CheckAgentDispatchAsync(
            context, scene.ProjectId, scene.WorkspaceId, owner.Id, CancellationToken.None);
        Assert.Equal(WorkspaceExecutionErrors.PreparationReservedCode, dispatch!.Code);
    }

    // ---- paging ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bounded_keyset_pages_visit_more_than_thirty_two_live_records_and_observe_the_later_deadlines()
    {
        var scenes = new List<LocalExecutionSequenceTestScene>();
        for (var index = 0; index < 40; index++)
        {
            scenes.Add((await QueuedAsync()).Scene);
        }

        scenes[0].Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        var first = await scenes[0].ObserveQueueAsync();
        Assert.Equal(32, first.Visited);
        Assert.False(first.SweepFinished);
        var second = await scenes[0].ObserveQueueAsync(first.LastCoordinationNumber, first.HighWaterCoordinationNumber);
        Assert.Equal(8, second.Visited);
        Assert.True(second.SweepFinished);

        foreach (var scene in scenes)
        {
            var sequence = await scene.ReadSequenceAsync();
            Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
            Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, sequence.ReasonCode);
            Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        }
    }

    [Fact]
    public async Task A_sweep_finishes_its_fixed_range_and_a_later_arrival_is_visited_by_the_next_sweep()
    {
        var (first, _, _) = await QueuedAsync();
        var page = await first.ObserveQueueAsync();
        var (late, _, _) = await QueuedAsync();

        var continued = await first.ObserveQueueAsync(page.LastCoordinationNumber, page.HighWaterCoordinationNumber);

        Assert.True(page.SweepFinished);
        Assert.Equal(0, continued.Visited);
        var next = await first.ObserveQueueAsync();
        Assert.Equal(2, next.Visited);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, (await late.ReadSequenceAsync()).State);
    }

    // ---- stale pins and deadlines ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_changed_pinned_authority_ends_the_waiting_cycle_with_attention_and_retires_its_admission()
    {
        var (scene, _, _) = await QueuedAsync();
        await scene.ReleaseLeaseAsync();

        await scene.ObserveWholeQueueAsync();

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AuthorityChanged, sequence.ReasonCode);
        Assert.Equal(LocalExecutionAdmissionState.Retired, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Null(await scene.ReserveAsync());
        Assert.Empty(await scene.ReadStepsAsync());
    }

    [Fact]
    public async Task The_original_two_hour_deadline_never_resets_and_an_expired_read_withholds_starts_before_any_tick()
    {
        var (scene, _, sequenceId) = await QueuedAsync();
        scene.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

        // The read projects the expiry immediately, without any mutation tick having persisted it.
        var status = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.True(status.Sequence!.ObservedBeforePersisted);
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, status.Sequence.RecordedState);
        Assert.Null(status.Sequence.Queue);

        // And no reservation is made from an elapsed pending admission.
        Assert.Null(await scene.ReserveAsync());
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, (await scene.ReadSequenceAsync()).State);
        await scene.ObserveWholeQueueAsync();
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, (await scene.ReadSequenceAsync()).ReasonCode);
        Assert.Equal(sequenceId, (await scene.ReadSequenceAsync()).Id);
    }

    [Fact]
    public async Task The_waiting_status_discloses_the_recorded_admission_and_a_reservation_discloses_a_preparation_never_a_process()
    {
        var (scene, _, _) = await QueuedAsync();
        await scene.AddWorkspaceOwnerAsync(OwnerFamily.Agent);
        await scene.ObserveWholeQueueAsync();

        var waiting = (await scene.ReadStatusAsync()).Sequence!;
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, waiting.State);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, waiting.AdmissionMode);
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, waiting.Queue!.WaitingReason);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, waiting.Queue.AdmissionState);
        Assert.Equal(Step.Implementation, waiting.Queue.Kind);
        Assert.Empty(waiting.Steps);
        Assert.Equal("wait_or_stop_after_current_operation", waiting.NextActionCode);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
    }
}
