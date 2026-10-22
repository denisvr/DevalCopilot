using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueue;

/// <summary>
/// Concurrent writers, malformed storage and counter exhaustion against the queue (ADR-0062), all through independent connections of one
/// file-backed SQLite database: duplicate reservation workers, a reservation racing a manual claim in both orders, a claim racing a stop,
/// reservation links that are missing, foreign or contradictory (never a zero count) and an exhausted order counter (never wrapped).
/// </summary>
public sealed class LocalExecutionQueueRaceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid Plan, Guid SequenceId)> QueuedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, plan.ProposalId, (await scene.QueueAsync(plan.ProposalId)).SequenceId);
    }

    // ---- duplicate reservation workers ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Racing_reservation_workers_reserve_one_queued_admission_exactly_once()
    {
        for (var round = 0; round < 4; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(database, 1);
            await scene.QueueAsync((await scene.AddAcceptedRootPlanAsync()).ProposalId);

            var tickets = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => scene.ReserveAsync()));

            Assert.Single(tickets, ticket => ticket is not null);
            Assert.Single(await scene.ReadStepsAsync());
            Assert.Equal(LocalExecutionAdmissionState.Reserved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
            Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStepReserved));
        }
    }

    [Fact]
    public async Task Racing_reservations_of_two_admissions_for_one_workspace_admit_exactly_one()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var first = await LocalExecutionSequenceTestScene.CreateAsync(database, 1);
            await first.QueueAsync((await first.AddAcceptedRootPlanAsync()).ProposalId);
            var second = await first.SiblingRunAsync();
            await second.QueueAsync((await second.AddAcceptedRootPlanAsync()).ProposalId);

            var tickets = await Task.WhenAll(first.ReserveAsync(), second.ReserveAsync(), first.ReserveAsync(), second.ReserveAsync());

            Assert.Single(tickets, ticket => ticket is not null);
            Assert.Equal(WorkspaceExecutionStatus.PreparationReserved, (await first.AssessWorkspaceAsync()).Status);
            Assert.Single(await first.ReadAllAdmissionsAsync(), admission => admission.State == LocalExecutionAdmissionState.Reserved);
        }
    }

    [Fact]
    public async Task Racing_reservations_of_two_projects_never_exceed_a_limit_of_one()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            await ExecutionCapacitySeed.SetLimitAsync(database, 1);
            var first = await LocalExecutionSequenceTestScene.CreateAsync(database, 1);
            await first.QueueAsync((await first.AddAcceptedRootPlanAsync()).ProposalId);
            var second = await LocalExecutionSequenceTestScene.CreateAsync(database, 1);
            await second.QueueAsync((await second.AddAcceptedRootPlanAsync()).ProposalId);

            var tickets = await Task.WhenAll(first.ReserveAsync(), second.ReserveAsync(), first.ReserveAsync());

            Assert.Single(tickets, ticket => ticket is not null);
            Assert.Equal(1, (await first.ReadCapacityAsync()).RecordedOwners);
        }
    }

    // ---- claim against stop, in both orders ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_claim_racing_a_stop_is_decided_by_the_locks_and_never_leaves_a_claimed_operation_of_a_stopped_before_claim_cycle()
    {
        for (var round = 0; round < 4; round++)
        {
            await using var database = new SqliteDatabaseFixture();
            await database.InitializeAsync();
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(database, 1);
            var receipt = await scene.QueueAsync((await scene.AddAcceptedRootPlanAsync()).ProposalId);
            var ticket = (await scene.ReserveAsync())!;

            var claim = scene.ClaimAsync(ticket);
            var stop = scene.StopAsync(receipt.SequenceId);
            await Task.WhenAll(claim, stop);

            var sequence = await scene.ReadSequenceAsync();
            var attempts = (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
            var admission = Assert.Single(await scene.ReadAdmissionsAsync());
            if ((await claim).IsSuccess)
            {
                Assert.Equal(1, attempts);
                Assert.Equal(LocalExecutionAdmissionState.Consumed, admission.State);
                Assert.Equal(LocalExecutionSequenceState.Stopping, sequence.State);
            }
            else
            {
                Assert.Equal(0, attempts);
                Assert.Equal(LocalExecutionAdmissionState.Retired, admission.State);
                Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
                Assert.Equal(WorkspaceExecutionStatus.Idle, (await scene.AssessWorkspaceAsync()).Status);
            }
        }
    }

    [Fact]
    public async Task A_manual_claim_that_committed_first_makes_the_queued_admission_wait_and_one_that_arrives_later_is_refused()
    {
        var (queued, _, _) = await QueuedAsync();
        var sibling = await queued.SiblingRunAsync();
        var siblingPlan = await sibling.AddAcceptedRootPlanAsync();

        // Order one: the manual implementation claim commits before any reservation.
        var manual = await sibling.Mediator.SendAsync(
            new DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommand(
                sibling.RunId, siblingPlan.ProposalId));
        Assert.True(manual.IsSuccess, manual.IsFailure ? manual.Errors[0].Code : null);
        Assert.Null(await queued.ReserveAsync());
        await queued.ObserveWholeQueueAsync();
        Assert.Equal(LocalExecutionWaitingReason.WaitingForWorkspace, Assert.Single(await queued.ReadAdmissionsAsync()).WaitingReason);
        Assert.Equal(LocalExecutionAdmissionState.Waiting, Assert.Single(await queued.ReadAdmissionsAsync()).State);
    }

    [Fact]
    public async Task A_manual_claim_arriving_after_a_reservation_is_refused_with_no_attempt_and_the_reservation_keeps_its_claim()
    {
        var (queued, _, _) = await QueuedAsync();
        var ticket = (await queued.ReserveAsync())!;
        var sibling = await queued.SiblingRunAsync();
        var siblingPlan = await sibling.AddAcceptedRootPlanAsync();
        var before = await sibling.FootprintAsync();

        var manual = await sibling.Mediator.SendAsync(
            new DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt.CreateImplementationAttemptCommand(
                sibling.RunId, siblingPlan.ProposalId));

        Assert.True(manual.IsFailure);
        Assert.Equal(WorkspaceExecutionErrors.PreparationReservedCode, manual.Errors[0].Code);
        Assert.Equal(before.Attempts, (await sibling.FootprintAsync()).Attempts);
        Assert.True((await queued.ClaimAsync(ticket)).IsSuccess);
    }

    // ---- limit orderings against a claim ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Once_the_claim_committed_the_operation_alone_is_counted_and_lowering_below_it_is_refused()
    {
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 2);
        var (scene, _, _) = await QueuedAsync();
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        var ticket = (await scene.ReserveAsync())!;
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);

        await using var context = fixture.CreateContext();
        var lowered = await new DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit.SetExecutionCapacityLimitCommandHandler(context)
            .HandleAsync(
                new DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit.SetExecutionCapacityLimitCommand(1), CancellationToken.None);

        Assert.True(lowered.IsFailure);
        Assert.Equal(ExecutionCapacityErrors.LimitBelowRecordedOwnersCode, lowered.Errors[0].Code);
        Assert.Equal(2, (await scene.ReadCapacityAsync()).RecordedOwners);
    }

    // ---- malformed or contradictory links are never a zero count ---------------------------------------------------------------

    [Fact]
    public async Task A_reservation_whose_step_was_revoked_behind_its_back_makes_both_readings_unavailable_and_blocks_reservations()
    {
        var (scene, _, _) = await QueuedAsync();
        var (other, _, _) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await scene.ExecuteSqlAsync(
            "UPDATE local_execution_sequence_steps SET \"State\" = 'Revoked', \"ReasonCode\" = 'stopped_before_claim' WHERE \"Id\" = {0}", ticket.StepId);

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await scene.AssessWorkspaceAsync()).Status);
        Assert.Equal(ExecutionCapacityStatus.Unavailable, (await scene.ReadCapacityAsync()).Status);
        Assert.Null(await other.ReserveAsync());
        var unavailable = await other.ReadQueueAvailabilityAsync((await other.ReadSequenceAsync()).PlanProposalMessageId);
        Assert.False(unavailable.Available);
    }

    [Fact]
    public async Task A_reserved_operation_identity_present_in_the_other_operation_family_cannot_be_proved_unused()
    {
        var (scene, sequenceId) = await QueuedSequenceAsync();
        var ticket = (await scene.ReserveAsync())!;

        // The database guard refuses such a row for an Agent step, so the guard is dropped on this test database only: the proof must
        // still refuse retirement when the colliding UUID names a verification execution.
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_verification_executions");
        await using (var context = fixture.CreateContext())
        {
            var recipe = (await context.VerificationCommands.AsNoTracking().FirstAsync(command => command.ProjectId == scene.ProjectId));
            var workspace = await context.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            var checkpoint = await context.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.CheckpointId);
            context.VerificationExecutions.Add(VerificationExecution.Claim(
                ticket.ReservedOperationId, scene.ProjectId, 500, workspace, checkpoint, recipe, LocalExecutionSequenceTestScene.Start));
            await context.SaveChangesAsync();
        }

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.IsSuccess, stopped.IsFailure ? stopped.Errors[0].Code : null);
        Assert.Equal(LocalExecutionAdmissionState.Unresolved, Assert.Single(await scene.ReadAdmissionsAsync()).State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, (await scene.ReadSequenceAsync()).ReasonCode);
    }

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> QueuedSequenceAsync()
    {
        var (scene, _, sequenceId) = await QueuedAsync();
        return (scene, sequenceId);
    }

    // ---- the order counter never wraps -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_exhausted_order_counter_refuses_intake_instead_of_wrapping_and_queues_nothing()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var command = await scene.QueueCommandAsync(plan.ProposalId);
        await scene.ExecuteSqlAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 9223372036854775806");

        var result = await scene.TryArmAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(LocalExecutionSequenceErrors.QueueUnavailableCode, result.Errors[0].Code);
        Assert.Null(await scene.FindSequenceAsync());
        Assert.Empty(await scene.ReadAllAdmissionsAsync());
    }

    [Fact]
    public async Task An_exhausted_order_counter_ends_a_cycle_that_needs_a_successor_with_attention_instead_of_reusing_an_order()
    {
        var (scene, _, sequenceId) = await QueuedAsync();
        var ticket = (await scene.ReserveAsync())!;
        await scene.DriveTicketAsync(ticket);
        await scene.ExecuteSqlAsync("UPDATE local_execution_order_counters SET \"LastNumber\" = 9223372036854775807");

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.True(observed.IsSuccess);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationFailed, sequence.ReasonCode);
        Assert.Equal("queue_unavailable", sequence.DetailCode);
        Assert.Single(await scene.ReadAdmissionsAsync());
    }

    [Fact]
    public async Task The_order_numbers_of_coordination_and_admissions_are_strictly_increasing_and_stored_as_integers()
    {
        var first = await QueuedAsync();
        var second = await QueuedAsync();

        var admissions = await first.Scene.ReadAllAdmissionsAsync();
        var firstSequence = await first.Scene.ReadSequenceAsync();
        var secondSequence = await second.Scene.ReadSequenceAsync();

        Assert.True(firstSequence.CoordinationNumber < secondSequence.CoordinationNumber);
        Assert.True(admissions[0].AdmissionNumber < admissions[1].AdmissionNumber);
        Assert.True(admissions[0].AdmissionNumber > firstSequence.CoordinationNumber);
        await using var context = fixture.CreateContext();
        Assert.Equal(admissions[1].AdmissionNumber, (await context.LocalExecutionOrderCounters.AsNoTracking().SingleAsync()).LastNumber);
    }
}
