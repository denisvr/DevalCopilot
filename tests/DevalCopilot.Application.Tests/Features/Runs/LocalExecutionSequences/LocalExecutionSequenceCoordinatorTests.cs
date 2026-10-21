using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.FaultInjectingDbContext;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The coordinator's decision step (ADR-0060) through the real observation, claim, resolve and stop handlers over real SQLite and
/// independent connections: it reserves one preparation intent only after proving the whole predecessor outcome, observes the three
/// deadlines without terminating or releasing an operation, drains a stopping sequence, never advances a closed one, and never lets a
/// failed or uncertain claim authorize another.
/// </summary>
public sealed class LocalExecutionSequenceCoordinatorTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId)> ArmedAsync(int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    [Fact]
    public async Task An_armed_sequence_reserves_exactly_the_implementation_step_and_hands_out_one_ticket()
    {
        var (scene, sequenceId) = await ArmedAsync();

        var ticket = await scene.ObserveReservingAsync(sequenceId);

        Assert.Equal(0, ticket.Ordinal);
        Assert.Equal(Step.Implementation, ticket.Kind);
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
        Assert.Equal(ticket.ReservedOperationId, step.ReservedOperationId);
        Assert.Equal(scene.CheckpointId, step.SourceCheckpointId);
        Assert.Equal(LocalExecutionSequenceTestScene.StartFingerprint, step.SourceFingerprintSha256);
        Assert.Equal(step.PreparationReservedAtUtc + LocalExecutionSequencePolicy.PreparationDeadline, step.PreparationDeadlineUtc);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStepReserved));
        // Reserving claims nothing and consumes nothing: no attempt, no manifest, no Git capture beyond the arming one.
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(0, scene.Artifacts.SealCount);
        Assert.Equal(1, scene.Evidence.Captures);
    }

    [Fact]
    public async Task Two_concurrent_observations_reserve_exactly_one_step_and_only_one_receives_the_ticket()
    {
        var (scene, sequenceId) = await ArmedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => scene.ObserveAsync(sequenceId))));

        Assert.Single(results, result => result.IsSuccess && result.Value.ReservedStep is not null);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStepReserved));
    }

    [Fact]
    public async Task An_unknown_sequence_observes_as_one_fixed_not_found()
    {
        var (scene, _) = await ArmedAsync();

        var result = await scene.ObserveAsync(Guid.NewGuid());

        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task Observing_a_claimed_running_step_changes_nothing_and_reserves_no_successor()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.ReserveAndClaimAsync(sequenceId);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.False(observed.Value.Changed);
        Assert.Null(observed.Value.ReservedStep);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, observed.Value.State);
        Assert.Single(await scene.ReadStepsAsync());
    }

    // ---- deadlines -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_expired_preparation_revokes_the_step_and_ends_in_attention_with_nothing_claimed()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        scene.Clock.Advance(LocalExecutionSequencePolicy.PreparationDeadline);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, step.ReasonCode);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, (await scene.ReadSequenceAsync()).ReasonCode);
        // The expired ticket claims nothing, and the original worker cannot retry it.
        var claim = await scene.ClaimAsync(ticket);
        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        Assert.Equal(0, scene.Artifacts.SealCount);
    }

    [Fact]
    public async Task A_preparation_that_is_one_tick_short_of_its_deadline_is_still_claimable()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        scene.Clock.Advance(LocalExecutionSequencePolicy.PreparationDeadline - TimeSpan.FromTicks(1));

        var observed = await scene.ObserveAsync(sequenceId);
        var claim = await scene.ClaimAsync(ticket);

        Assert.False(observed.Value.Changed);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
    }

    [Fact]
    public async Task The_advancement_deadline_ends_an_armed_or_preparing_sequence()
    {
        var (armedScene, armedId) = await ArmedAsync();
        armedScene.Clock.Advance(LocalExecutionSequencePolicy.AdvancementWindow);

        var armed = await armedScene.ObserveAsync(armedId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, armed.Value.State);
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, (await armedScene.ReadSequenceAsync()).ReasonCode);
        Assert.Empty(await armedScene.ReadStepsAsync());

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(other);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var preparing = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        await scene.ObserveReservingAsync(preparing);
        scene.Clock.Advance(LocalExecutionSequencePolicy.AdvancementWindow);
        await scene.ObserveAsync(preparing);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, sequence.ReasonCode);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync()).State);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task The_result_deadline_is_claim_time_plus_the_actual_timeout_plus_two_minutes_capped_by_the_advancement_deadline()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.ReserveAndClaimAsync(sequenceId);
        var step = Assert.Single(await scene.ReadStepsAsync());
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(step.ClaimedAtUtc + TimeSpan.FromMinutes(20) + LocalExecutionSequencePolicy.ResultGrace, step.ResultDeadlineUtc);

        // A claim late in the window is capped by the absolute advancement deadline.
        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var late = await LocalExecutionSequenceTestScene.CreateAsync(other);
        var plan = await late.AddAcceptedRootPlanAsync();
        var lateId = (await late.ArmAsync(plan.ProposalId)).SequenceId;
        var ticket = await late.ObserveReservingAsync(lateId);
        late.Clock.Advance(TimeSpan.FromMinutes(100) + TimeSpan.FromSeconds(110));
        // Keep the preparation inside its own deadline by observing a fresh reservation time is not possible: claim at once.
        late.Clock.Advance(TimeSpan.Zero);
        Assert.Equal(LocalExecutionSequenceErrors.StepExpiredCode, (await late.ClaimAsync(ticket)).Errors[0].Code);
        Assert.Equal(sequence.AdvancementDeadlineUtc, LocalExecutionSequencePolicy.ResultDeadlineOf(
            sequence.AdvancementDeadlineUtc - TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), sequence.AdvancementDeadlineUtc));
        await other.DisposeAsync();
    }

    [Fact]
    public async Task An_elapsed_result_deadline_ends_the_sequence_without_terminating_or_releasing_the_running_operation()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        scene.Clock.Advance(TimeSpan.FromMinutes(22));

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        Assert.Equal(LocalExecutionSequenceReason.ResultDeadlineElapsed, (await scene.ReadSequenceAsync()).ReasonCode);
        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        // The deadline proves neither process death nor safe release: the fence stays held and no successor can ever be reserved.
        Assert.Equal(
            Application.Features.Runs.Queries.GetLocalExecutionSequence.LocalExecutionSequenceManualFence.Blocked,
            (await scene.ReadStatusAsync()).ManualFence);
        Assert.Null((await scene.ObserveAsync(sequenceId)).Value.ReservedStep);
    }

    [Fact]
    public async Task A_result_recorded_after_its_deadline_is_judged_late_even_when_valid()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        scene.Clock.Advance(TimeSpan.FromMinutes(23));
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        Assert.Equal(LocalExecutionSequenceReason.ResultDeadlineElapsed, (await scene.ReadSequenceAsync()).ReasonCode);
        Assert.Single(await scene.ReadStepsAsync());
    }

    // ---- stop ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stopping_an_armed_sequence_ends_it_stopped_before_any_claim()
    {
        var (scene, sequenceId) = await ArmedAsync();

        var stopped = await scene.StopAsync(sequenceId);

        Assert.True(stopped.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.Stopped, stopped.Value.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, stopped.Value.ReasonCode);
        Assert.Empty(await scene.ReadStepsAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStopRequested));
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));
        Assert.Null((await scene.ObserveAsync(sequenceId)).Value.ReservedStep);
    }

    [Fact]
    public async Task Stop_before_the_claim_revokes_the_preparation_with_zero_consumption()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var before = await scene.CountWorkAsync();

        var stopped = await scene.StopAsync(sequenceId);
        var claim = await scene.ClaimAsync(ticket);

        Assert.Equal(LocalExecutionSequenceState.Stopped, stopped.Value.State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        var after = await scene.CountWorkAsync();
        Assert.Equal((before.Attempts, before.Executions, before.Seals), (after.Attempts, after.Executions, after.Seals));
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
    }

    [Fact]
    public async Task Stop_after_the_claim_drains_the_one_operation_then_ends_stopped_without_a_successor()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var stopping = await scene.StopAsync(sequenceId);

        Assert.True(stopping.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.Stopping, stopping.Value.State);
        Assert.Equal(LocalExecutionSequenceState.Stopping, (await scene.ReadSequenceAsync()).State);

        // The claimed operation may still dispatch and finish; nothing else starts.
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        var observedRunning = await scene.ObserveAsync(sequenceId);
        Assert.False(observedRunning.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.Stopping, observedRunning.Value.State);
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        var drained = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.Stopped, drained.Value.State);
        Assert.Null(drained.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.StoppedAfterOperation, sequence.ReasonCode);
        Assert.NotNull(sequence.StopRequestedAtUtc);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadExecutionsAsync());
        Assert.Null(sequence.FinalReportMessageId);
    }

    [Fact]
    public async Task A_stop_that_won_keeps_stopped_even_over_a_valid_late_looking_completion()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.StopAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromMinutes(25));
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        await scene.ObserveAsync(sequenceId);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedAfterOperation, sequence.ReasonCode);
    }

    [Fact]
    public async Task Stop_is_idempotent_and_a_late_stop_of_an_ended_sequence_returns_it_unchanged()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.ReserveAndClaimAsync(sequenceId);
        var first = await scene.StopAsync(sequenceId);
        var second = await scene.StopAsync(sequenceId);

        Assert.True(first.Value.Changed);
        Assert.False(second.Value.Changed);
        Assert.Equal(first.Value.Revision, second.Value.Revision);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceStopRequested));

        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var ended = await LocalExecutionSequenceTestScene.CreateAsync(other);
        var plan = await ended.AddAcceptedRootPlanAsync();
        var endedId = (await ended.ArmAsync(plan.ProposalId)).SequenceId;
        var stopped = await ended.StopAsync(endedId);
        var late = await ended.StopAsync(endedId);
        Assert.False(late.Value.Changed);
        Assert.Equal(stopped.Value.State, late.Value.State);
        Assert.Equal(stopped.Value.Revision, late.Value.Revision);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task A_stop_for_another_runs_sequence_identity_is_a_non_disclosing_not_found()
    {
        var (scene, sequenceId) = await ArmedAsync();

        await using var context = fixture.CreateContext();
        var result = await new Application.Features.Runs.Commands.StopLocalExecutionSequence.StopLocalExecutionSequenceCommandHandler(context, scene.Clock)
            .HandleAsync(new(Guid.NewGuid(), sequenceId), CancellationToken.None);

        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, result.Errors[0].Code);
        Assert.Equal(LocalExecutionSequenceState.Armed, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task A_stop_and_a_claim_racing_on_independent_connections_are_decided_by_the_locks_with_no_partial_write()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var other = new SqliteDatabaseFixture();
            await other.InitializeAsync();
            var scene = await LocalExecutionSequenceTestScene.CreateAsync(other);
            var plan = await scene.AddAcceptedRootPlanAsync();
            var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
            var ticket = await scene.ObserveReservingAsync(sequenceId);

            var claim = Task.Run(() => scene.ClaimAsync(ticket));
            var stop = Task.Run(() => scene.StopAsync(sequenceId));
            await Task.WhenAll(claim, stop);

            var sequence = await scene.ReadSequenceAsync();
            var step = Assert.Single(await scene.ReadStepsAsync());
            var attempts = (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
            var claimResult = await claim;
            if (claimResult.IsSuccess)
            {
                // The claim committed first: one operation may drain while Stopping.
                Assert.Equal(1, attempts);
                Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
                Assert.Equal(LocalExecutionSequenceState.Stopping, sequence.State);
            }
            else
            {
                // The stop won: the preparation was revoked and nothing was consumed.
                Assert.Equal(0, attempts);
                Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State);
                Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
                Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, claimResult.Errors[0].Code);
            }

            await other.DisposeAsync();
        }
    }

    // ---- late results and closed sequences -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_late_result_finishes_its_own_row_but_never_reopens_an_ended_sequence()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        scene.Clock.Advance(TimeSpan.FromMinutes(25));
        await scene.ObserveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);

        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);
        var observed = await scene.ObserveAsync(sequenceId);

        Assert.False(observed.Value.Changed);
        Assert.Null(observed.Value.ReservedStep);
        Assert.Equal(AttemptStatus.Completed, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await scene.ReadSequenceAsync()).State);
        Assert.Single(await scene.ReadStepsAsync());
        // With coherent host exit evidence recorded, the fence of the ended sequence is released: never a resumption.
        Assert.Equal(
            Application.Features.Runs.Queries.GetLocalExecutionSequence.LocalExecutionSequenceManualFence.Released,
            (await scene.ReadStatusAsync()).ManualFence);
    }

    // ---- fence-refused dispatch ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_claim_that_the_capacity_fence_will_not_let_dispatch_ends_in_attention_and_a_later_dispatch_is_refused_dedicatedly()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        // Another project's owner plus a recorded limit of one: the ordinary global gate refuses this dispatch.
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.CapacityRefused, sequence.ReasonCode);

        var dispatch = await scene.DispatchAgentAsync(ticket.ReservedOperationId);

        Assert.True(dispatch.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode, dispatch.Errors[0].Code);
        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);

        // The dedicated, idempotent refusal recorder closes the proved-undispatched claim: no protocol result, no process evidence.
        Assert.True((await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId)).IsSuccess);
        Assert.True((await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId)).IsFailure);
        var refused = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, refused.AgentOutcome);
        Assert.Null(refused.AgentProcessOutcome);
        Assert.DoesNotContain(await scene.ReadMessagesAsync(), message => message.AttemptId == ticket.ReservedOperationId);
    }

    [Fact]
    public async Task A_stopping_sequence_whose_claim_the_fence_refuses_ends_stopped_not_attention()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.ReserveAndClaimAsync(sequenceId);
        await scene.StopAsync(sequenceId);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        await scene.ObserveAsync(sequenceId);

        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedAfterOperation, sequence.ReasonCode);
    }

    [Fact]
    public async Task A_capacity_conflict_at_the_claim_consumes_nothing_and_resolves_to_attention_with_its_stable_detail()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        await ExecutionCapacitySeed.AddAsync(fixture, OwnerFamily.Agent);
        await ExecutionCapacitySeed.SetLimitAsync(fixture, 1);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        Assert.Equal(0, scene.Artifacts.SealCount - scene.Artifacts.DeletedSealedFiles.Count);
        var resolved = await scene.ResolveAsync(ticket, claim.Errors[0].Code);

        Assert.True(resolved.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, resolved.Value.State);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, resolved.Value.StepState);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.CapacityRefused, sequence.ReasonCode);
        Assert.Equal(LocalExecutionSequenceReason.NormalizeDetail(claim.Errors[0].Code), sequence.DetailCode);
    }

    // ---- uncertain claims and resolution ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Resolving_a_claim_that_the_independent_read_proves_persisted_changes_nothing()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var resolved = await scene.ResolveAsync(ticket, "attempts.persistence_unresolved");

        Assert.False(resolved.Value.Changed);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, resolved.Value.State);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, resolved.Value.StepState);
    }

    [Fact]
    public async Task Resolving_an_unresolved_probe_marks_the_step_unresolved_and_holds_the_fence_without_a_second_claim()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        var resolved = await scene.ResolveAsync(
            ticket, "attempts.persistence_unresolved", new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved));

        Assert.True(resolved.Value.Changed);
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, resolved.Value.StepState);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, sequence.ReasonCode);
        Assert.Equal(
            Application.Features.Runs.Queries.GetLocalExecutionSequence.LocalExecutionSequenceManualFence.Blocked,
            (await scene.ReadStatusAsync()).ManualFence);
        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, (await scene.ClaimAsync(ticket)).Errors[0].Code);
    }

    [Fact]
    public async Task A_throwing_probe_never_authorizes_anything_and_leaves_the_reservation_to_expire()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scene.ResolveAsync(
            ticket, "attempts.persistence_failed",
            new ConfigurableLocalExecutionStepProbe(LocalExecutionStepProbeResult.Unresolved, new InvalidOperationException("probe"))));

        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task A_commit_that_failed_before_committing_is_absent_and_resolves_to_a_preparation_failure()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var mediator = scene.MediatorWith(() => new FaultInjectingDbContext(fixture.CreateContext()) { CommitFailure = CommitFailureMode.BeforeCommit });

        var claim = await scene.ClaimAsync(ticket, mediator: mediator);

        Assert.True(claim.IsFailure);
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        var resolved = await scene.ResolveAsync(ticket, claim.Errors[0].Code);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, resolved.Value.StepState);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationFailed, sequence.ReasonCode);
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
    }

    [Fact]
    public async Task A_commit_that_failed_after_committing_resolves_as_the_claimed_step_and_is_never_claimed_twice()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var mediator = scene.MediatorWith(() => new FaultInjectingDbContext(fixture.CreateContext()) { CommitFailure = CommitFailureMode.AfterCommit });

        var claim = await scene.ClaimAsync(ticket, mediator: mediator);

        // Whatever the handler reported, the independent read proves the claim persisted: the step is linked exactly once.
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        Assert.Equal(ticket.ReservedOperationId, step.OperationId);
        Assert.Single(await scene.ReadAttemptsAsync(), attempt => attempt.Id == ticket.ReservedOperationId);
        var resolved = await scene.ResolveAsync(ticket, claim.IsFailure ? claim.Errors[0].Code : null);
        Assert.False(resolved.Value.Changed);
        Assert.Contains((await scene.ClaimAsync(ticket)).Errors[0].Code, new[] { LocalExecutionSequenceErrors.StepAuthorityLostCode, "attempts.run_has_active_attempt" });
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
    }

    [Fact]
    public async Task A_cancellation_after_the_commit_still_resolves_by_the_independent_read()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var mediator = scene.MediatorWith(
            () => new FaultInjectingDbContext(fixture.CreateContext()) { CommitFailure = CommitFailureMode.CancellationAfterCommit });

        await Assert.ThrowsAsync<OperationCanceledException>(() => scene.ClaimAsync(ticket, mediator: mediator));

        Assert.Equal(LocalExecutionSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
        Assert.False((await scene.ResolveAsync(ticket, null)).Value.Changed);
    }

    [Fact]
    public async Task A_cancellation_before_the_commit_proves_absence_and_removes_the_orphan_manifest()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var mediator = scene.MediatorWith(
            () => new FaultInjectingDbContext(fixture.CreateContext()) { CommitFailure = CommitFailureMode.CancellationBeforeCommit });

        await Assert.ThrowsAsync<OperationCanceledException>(() => scene.ClaimAsync(ticket, mediator: mediator));

        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        Assert.Equal(LocalExecutionStepProbeResult.NotClaimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
    }

    [Fact]
    public async Task A_save_that_failed_rolls_everything_back_and_leaves_the_one_preparation_untried_by_the_coordinator()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        var mediator = scene.MediatorWith(
            () => new FaultInjectingDbContext(fixture.CreateContext()) { SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave });

        var claim = await scene.ClaimAsync(ticket, mediator: mediator);

        Assert.True(claim.IsFailure);
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync()).State);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionStepProbeResult.NotClaimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
    }

    // ---- duplicate workers and forged identity ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_duplicate_worker_can_never_reclaim_a_step_that_was_claimed()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var again = await scene.ClaimAsync(ticket);

        Assert.Contains(again.Errors[0].Code, new[] { LocalExecutionSequenceErrors.StepAuthorityLostCode, "attempts.run_has_active_attempt" });
        Assert.Single(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Single(await scene.ReadStepsAsync());
    }

    [Fact]
    public async Task Two_workers_claiming_the_same_ticket_concurrently_commit_exactly_one_operation()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        // The same ticket is only ever held by its original worker; two tasks sharing it still commit one operation (a loser may even
        // collide on the one manifest file of the shared operation identity and fail outright).
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            try
            {
                return await scene.ClaimAsync(ticket);
            }
            catch (IOException)
            {
                return null;
            }
        })));

        Assert.Single(results, result => result is { IsSuccess: true });
        Assert.Single(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task A_ticket_with_a_different_preparation_token_is_never_honored()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var real = await scene.ObserveReservingAsync(sequenceId);
        var forged = new LocalExecutionStepTicket(
            real.SequenceId, real.StepId, real.Ordinal, real.Kind, real.ReservedOperationId, Guid.NewGuid(), real.SourceCheckpointId,
            real.ReportMessageId, real.SubjectAttemptId, null);

        var claim = await scene.ClaimAsync(forged);

        Assert.Equal(LocalExecutionSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task A_ticket_naming_a_different_operation_identity_than_the_reserved_one_is_never_honored()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var real = await scene.ObserveReservingAsync(sequenceId);
        var forged = new LocalExecutionStepTicket(
            real.SequenceId, real.StepId, real.Ordinal, real.Kind, Guid.NewGuid(), real.OwnerToken, real.SourceCheckpointId,
            real.ReportMessageId, real.SubjectAttemptId, null);

        var claim = await scene.ClaimAsync(forged);

        Assert.True(claim.IsFailure);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task A_source_change_between_the_reservation_and_the_claim_refuses_the_claim_without_consuming_anything()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        scene.Evidence.Fingerprint = new string('9', 64);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.DoesNotContain(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync()).State);
    }

    [Fact]
    public async Task The_composed_claim_goes_through_exactly_the_one_existing_command_of_the_step_kind()
    {
        var (scene, sequenceId) = await ArmedAsync();

        await scene.ReserveAndClaimAsync(sequenceId);

        Assert.Equal(["CreateImplementationAttemptCommand"], scene.Mediator.Dispatched);
    }
}
