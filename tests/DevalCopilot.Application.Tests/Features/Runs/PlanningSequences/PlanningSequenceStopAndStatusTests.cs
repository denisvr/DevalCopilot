using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055 "Stop after current step" and the recorded status read: stop is exact and idempotent per state, bound to the exact
/// run and sequence, and the status is a recorded read with the effective deadline overlay, the manual-control fence, the arming
/// availability and the exact final references only when proved.</summary>
public sealed class PlanningSequenceStopAndStatusTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<GetPlanningSequenceQueryResult> StatusAsync(PlanningSequenceTestScene scene, Guid? runId = null)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetPlanningSequenceQueryHandler(db, scene.Clock).HandleAsync(
            new GetPlanningSequenceQuery(runId ?? scene.RunId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    [Fact]
    public async Task Stopping_an_armed_sequence_ends_it_as_Stopped_with_nothing_claimed()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        var stopped = await scene.StopAsync(armed.SequenceId);

        Assert.True(stopped.Value.Changed);
        Assert.Equal(PlanningSequenceState.Stopped, stopped.Value.State);
        Assert.Equal(PlanningSequenceReason.StoppedBeforeClaim, stopped.Value.ReasonCode);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceStopRequested));
    }

    [Fact]
    public async Task Stopping_a_reserved_step_revokes_its_authority_and_consumes_no_attempt_or_budget()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.ObserveReservingAsync(armed.SequenceId);

        var stopped = await scene.StopAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.Stopped, stopped.Value.State);
        Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(0, scene.Artifacts.Seals);
    }

    [Fact]
    public async Task Repeated_stop_requests_are_idempotent_and_a_stopped_or_completed_sequence_is_returned_unchanged()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);

        var first = await scene.StopAsync(armed.SequenceId);
        var second = await scene.StopAsync(armed.SequenceId);

        Assert.True(first.Value.Changed);
        Assert.False(second.Value.Changed);
        Assert.Equal(PlanningSequenceState.Stopping, second.Value.State);
        Assert.Equal(first.Value.Revision, second.Value.Revision);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceStopRequested));

        await scene.CompleteStepAsync(claim.AttemptId, StepResult.Proposed);
        await scene.ObserveAsync(armed.SequenceId);
        var after = await scene.StopAsync(armed.SequenceId);
        Assert.False(after.Value.Changed);
        Assert.Equal(PlanningSequenceState.Stopped, after.Value.State);

        // A terminal completed sequence is returned unchanged by a late stop.
        var done = await PlanningSequenceTestScene.CreateAsync(fixture);
        var doneArmed = await done.ArmAsync();
        await done.CompleteStepAsync((await done.ReserveAndClaimAsync(doneArmed.SequenceId)).Claim.AttemptId, StepResult.Proposed);
        await done.CompleteStepAsync((await done.ReserveAndClaimAsync(doneArmed.SequenceId)).Claim.AttemptId, StepResult.Accepted);
        await done.ObserveAsync(doneArmed.SequenceId);
        var late = await done.StopAsync(doneArmed.SequenceId);
        Assert.False(late.Value.Changed);
        Assert.Equal(PlanningSequenceState.ReadyForImplementation, late.Value.State);
    }

    [Fact]
    public async Task A_foreign_run_or_unknown_sequence_is_one_fixed_not_found_and_changes_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var other = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        await using var db = fixture.CreateContext();
        var handler = new DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence.StopPlanningSequenceCommandHandler(db, scene.Clock);
        var foreign = await handler.HandleAsync(
            new DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence.StopPlanningSequenceCommand(other.RunId, armed.SequenceId), default);
        var unknown = await handler.HandleAsync(
            new DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence.StopPlanningSequenceCommand(scene.RunId, Guid.NewGuid()), default);

        Assert.Equal(PlanningSequenceErrors.NotFoundCode, foreign.Errors[0].Code);
        Assert.Equal(PlanningSequenceErrors.NotFoundCode, unknown.Errors[0].Code);
        Assert.Equal(PlanningSequenceState.Armed, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task The_status_of_a_run_without_a_sequence_is_None_with_the_exact_candidate_tuple_and_a_released_fence()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        var status = await StatusAsync(scene);

        Assert.Null(status.State);
        Assert.Null(status.Sequence);
        Assert.Equal(PlanningSequenceManualFence.Released, status.ManualFence);
        Assert.True(status.Arming!.Available);
        Assert.Equal(scene.WorkspaceId, status.Arming.WorkspaceId);
        Assert.Equal(scene.CheckpointId, status.Arming.CheckpointId);
        Assert.Equal(scene.FingerprintSha256, status.Arming.FingerprintSha256);
        // A recorded read: no Git observation happened.
        Assert.Equal(0, scene.Evidence.Captures);
    }

    [Fact]
    public async Task The_status_reports_one_fixed_refusal_when_arming_is_not_available()
    {
        var legacy = await PlanningSequenceTestScene.CreateAsync(fixture, mode: RunExecutionMode.Legacy);

        var status = await StatusAsync(legacy);

        Assert.False(status.Arming!.Available);
        Assert.Equal(PlanningSequenceErrors.RunNotManualCode, status.Arming.RefusalCode);
        Assert.Null(status.Arming.WorkspaceId);
    }

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        await using var db = fixture.CreateContext();
        var result = await new GetPlanningSequenceQueryHandler(db, scene.Clock).HandleAsync(
            new GetPlanningSequenceQuery(Guid.NewGuid()), default);

        Assert.Equal(PlanningSequenceErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task An_existing_sequence_exposes_its_immutable_tuple_and_never_a_replacement_target()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            context.GitCheckpoints.Add(DevalCopilot.Domain.Features.Projects.GitCheckpoint.Capture(
                Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), PlanningSequenceTestScene.Start, new string('a', 40),
                new string('e', 64), []));
            await context.SaveChangesAsync();
        }

        var status = await StatusAsync(scene);

        Assert.Equal(PlanningSequenceState.Armed, status.State);
        Assert.Null(status.Arming);
        Assert.Equal(scene.CheckpointId, status.Sequence!.CheckpointId);
        Assert.Equal(scene.FingerprintSha256, status.Sequence.FingerprintSha256);
        Assert.Equal(armed.SequenceId, status.Sequence.SequenceId);
        Assert.Equal(PlanningSequenceManualFence.Blocked, status.ManualFence);
    }

    [Fact]
    public async Task An_elapsed_deadline_reads_as_attention_immediately_while_the_recorded_state_is_unchanged()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.ObserveReservingAsync(armed.SequenceId);
        scene.Clock.Advance(PlanningSequencePolicy.PreparationDeadline);

        var preparing = await StatusAsync(scene);

        Assert.Equal(PlanningSequenceState.NeedsAttention, preparing.State);
        Assert.Equal(PlanningSequenceState.PreparingStep, preparing.Sequence!.RecordedState);
        Assert.Equal(preparing.State, preparing.Sequence.State);
        Assert.Equal(PlanningSequenceReason.PreparationExpired, preparing.Sequence.ReasonCode);
        Assert.True(preparing.Sequence.ObservedBeforePersisted);
        Assert.Equal(PlanningSequenceManualFence.Blocked, preparing.ManualFence);

        var waiting = await PlanningSequenceTestScene.CreateAsync(fixture);
        var waitingArmed = await waiting.ArmAsync();
        var (_, claim) = await waiting.ReserveAndClaimAsync(waitingArmed.SequenceId);
        waiting.Clock.Advance(TimeSpan.FromMinutes(12));
        var awaiting = await StatusAsync(waiting);
        Assert.Equal(PlanningSequenceState.NeedsAttention, awaiting.State);
        Assert.Equal(awaiting.State, awaiting.Sequence!.State);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, awaiting.Sequence.RecordedState);
        Assert.Equal(PlanningSequenceReason.ResultDeadlineElapsed, awaiting.Sequence.ReasonCode);
        Assert.True(awaiting.Sequence.ObservedBeforePersisted);
        Assert.Equal(PlanningSequenceStepState.Claimed, awaiting.Sequence.Steps.Single().State);
        Assert.Equal(claim.AttemptId, awaiting.Sequence.Steps.Single().AttemptId);
        Assert.Equal(AttemptStatus.Running, awaiting.Sequence.Steps.Single().AttemptStatus);
    }

    [Fact]
    public async Task A_persisted_attention_with_a_running_linked_attempt_stays_visible_as_awaiting_reconciliation_and_keeps_the_fence()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);
        scene.Clock.Advance(TimeSpan.FromMinutes(13));
        await scene.ObserveAsync(armed.SequenceId);

        var status = await StatusAsync(scene);

        Assert.Equal(PlanningSequenceState.NeedsAttention, status.Sequence!.State);
        Assert.False(status.Sequence.ObservedBeforePersisted);
        Assert.True(status.Sequence.AwaitingReconciliation);
        Assert.Equal(claim.AttemptId, status.Sequence.OutstandingAttemptId);
        Assert.Equal(PlanningSequenceManualFence.Blocked, status.ManualFence);
    }

    [Fact]
    public async Task The_final_references_are_proved_for_an_accepted_plan_and_an_escalation_and_never_for_a_tampered_record()
    {
        var accepted = await PlanningSequenceTestScene.CreateAsync(fixture);
        var acceptedArmed = await accepted.ArmAsync();
        var planner = (await accepted.ReserveAndClaimAsync(acceptedArmed.SequenceId)).Claim.AttemptId;
        await accepted.CompleteStepAsync(planner, StepResult.Proposed);
        var review = (await accepted.ReserveAndClaimAsync(acceptedArmed.SequenceId)).Claim.AttemptId;
        await accepted.CompleteStepAsync(review, StepResult.Accepted);
        await accepted.ObserveAsync(acceptedArmed.SequenceId);

        var status = await StatusAsync(accepted);

        Assert.Equal(PlanningSequenceManualFence.Released, status.ManualFence);
        var final = status.Sequence!.Final!;
        await using (var context = fixture.CreateContext())
        {
            var proposal = await context.CollaborationMessages.AsNoTracking().SingleAsync(
                message => message.AttemptId == planner && message.Type == CollaborationMessageType.Proposal);
            var acceptance = await context.CollaborationMessages.AsNoTracking().SingleAsync(
                message => message.AttemptId == review && message.Type == CollaborationMessageType.Acceptance);
            Assert.Equal(proposal.Id, final.FinalProposalMessageId);
            Assert.Equal(acceptance.Id, final.AcceptanceMessageId);
            Assert.Null(final.EscalationMessageId);
            Assert.Equal(review, final.ProvingAttemptId);

            // The recorded acceptance disappears: the reference is no longer proved and is not disclosed.
            await context.Database.ExecuteSqlRawAsync("DELETE FROM collaboration_messages WHERE Id = {0}", acceptance.Id);
        }

        Assert.Null((await StatusAsync(accepted)).Sequence!.Final);
    }

    [Fact]
    public async Task The_manual_fence_is_blocked_while_authority_is_live_and_released_after_it_ended_without_a_running_attempt()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        Assert.Equal(PlanningSequenceManualFence.Blocked, (await StatusAsync(scene)).ManualFence);

        await scene.StopAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceManualFence.Released, (await StatusAsync(scene)).ManualFence);
    }
}
