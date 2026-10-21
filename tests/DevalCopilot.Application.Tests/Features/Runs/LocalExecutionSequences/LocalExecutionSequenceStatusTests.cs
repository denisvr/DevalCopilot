using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>The recorded status of a run's local execution sequence (ADR-0060): None, every state, the effective projection of an elapsed
/// deadline without any write, the manual-control fence, quarantine and reconciliation, bounded steps and final references proved only
/// from consistent durable rows.</summary>
public sealed class LocalExecutionSequenceStatusTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId, Guid PlanId)> ArmedAsync(int recipeCount = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, (await scene.ArmAsync(plan.ProposalId)).SequenceId, plan.ProposalId);
    }

    [Fact]
    public async Task A_run_without_a_sequence_reads_as_none_and_releases_the_fence()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);

        var status = await scene.ReadStatusAsync();

        Assert.Null(status.State);
        Assert.Null(status.Sequence);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, status.ManualFence);
        Assert.Equal(scene.RunId, status.RunId);
    }

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture);
        await using var context = fixture.CreateContext();

        var result = await new GetLocalExecutionSequenceQueryHandler(context, scene.Clock).HandleAsync(
            new GetLocalExecutionSequenceQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task An_armed_sequence_reads_its_exact_pinned_tuple_allowance_deadline_and_a_blocked_fence()
    {
        var (scene, sequenceId, planId) = await ArmedAsync(recipeCount: 3);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.Armed, status.State);
        var sequence = status.Sequence!;
        Assert.Equal(sequenceId, sequence.SequenceId);
        Assert.Equal(planId, sequence.PlanProposalMessageId);
        Assert.Equal(LocalExecutionPlanAuthority.AcceptedRoot, sequence.PlanAuthority);
        Assert.Equal(scene.WorkspaceId, sequence.WorkspaceId);
        Assert.Equal(scene.CheckpointId, sequence.CheckpointId);
        Assert.Equal(scene.CheckpointId, sequence.CurrentCheckpointId);
        Assert.Equal(LocalExecutionSequenceTestScene.StartFingerprint, sequence.CurrentFingerprintSha256);
        Assert.Equal(3, sequence.RecipeCount);
        Assert.Equal(15, sequence.MaximumSteps);
        Assert.Equal(2, sequence.CorrectionAllowance);
        Assert.Equal(0, sequence.CorrectionsClaimed);
        Assert.Equal(0, sequence.PassesStarted);
        Assert.Equal(0, sequence.AgentClaims);
        Assert.Empty(sequence.Steps);
        Assert.False(sequence.ObservedBeforePersisted);
        Assert.False(sequence.AwaitingReconciliation);
        Assert.Null(sequence.Final);
        Assert.Equal("wait_or_stop_after_current_operation", sequence.NextActionCode);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.True(status.LatestEventSequence > 0);
    }

    [Fact]
    public async Task Each_live_state_reads_its_recorded_step_and_the_next_action_without_a_write()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        var preparing = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, preparing.State);
        var reserved = Assert.Single(preparing.Sequence!.Steps);
        Assert.Equal(LocalExecutionSequenceStepState.Reserved, reserved.State);
        Assert.Null(reserved.OperationId);
        Assert.Equal(ticket.StepId, reserved.StepId);

        await scene.ClaimAsync(ticket);
        var awaiting = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, awaiting.State);
        var claimed = Assert.Single(awaiting.Sequence!.Steps);
        Assert.Equal(ticket.ReservedOperationId, claimed.OperationId);
        Assert.Equal(nameof(AttemptStatus.Running), claimed.OperationStatus);
        Assert.False(claimed.Dispatched);
        Assert.Equal(ticket.ReservedOperationId, awaiting.Sequence.OutstandingOperationId);
        Assert.Equal(1, awaiting.Sequence.AgentClaims);

        await scene.StopAsync(sequenceId);
        var stopping = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.Stopping, stopping.State);
        Assert.Equal("wait_for_current_operation_to_finish", stopping.Sequence!.NextActionCode);
        Assert.NotNull(stopping.Sequence.StopRequestedAtUtc);
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Stopped, "continue_with_explicit_manual_work")]
    [InlineData(LocalExecutionSequenceState.Interrupted, "inspect_and_reconcile")]
    public async Task An_ended_state_reads_with_its_fixed_reason_and_next_action(LocalExecutionSequenceState state, string nextAction)
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        if (state == LocalExecutionSequenceState.Stopped)
        {
            await scene.StopAsync(sequenceId);
        }
        else
        {
            await scene.InterruptAsync();
        }

        var status = await scene.ReadStatusAsync();

        Assert.Equal(state, status.State);
        Assert.Equal(state, status.Sequence!.RecordedState);
        Assert.Equal(nextAction, status.Sequence.NextActionCode);
        Assert.NotNull(status.Sequence.EndedAtUtc);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, status.ManualFence);
        Assert.NotNull(status.Sequence.ReasonCode);
    }

    [Fact]
    public async Task An_elapsed_advancement_deadline_reads_as_attention_immediately_without_a_write()
    {
        var (scene, _, _) = await ArmedAsync();
        var revision = (await scene.ReadSequenceAsync()).Revision;
        var events = await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded);
        scene.Clock.Advance(LocalExecutionSequencePolicy.AdvancementWindow);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.Sequence!.State);
        Assert.Equal(LocalExecutionSequenceState.Armed, status.Sequence.RecordedState);
        Assert.True(status.Sequence.ObservedBeforePersisted);
        Assert.Equal(LocalExecutionSequenceReason.AdvancementDeadlineElapsed, status.Sequence.ReasonCode);
        // Nothing was persisted by reading.
        Assert.Equal(revision, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(LocalExecutionSequenceState.Armed, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(events, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));
    }

    [Fact]
    public async Task An_elapsed_preparation_deadline_reads_as_attention_before_the_coordinator_persists_it()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        await scene.ObserveReservingAsync(sequenceId);
        scene.Clock.Advance(LocalExecutionSequencePolicy.PreparationDeadline);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationExpired, status.Sequence!.ReasonCode);
        Assert.True(status.Sequence.ObservedBeforePersisted);
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task An_elapsed_result_deadline_of_a_running_or_a_late_finished_operation_reads_as_attention()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        scene.Clock.Advance(TimeSpan.FromMinutes(23));

        var running = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, running.State);
        Assert.Equal(LocalExecutionSequenceReason.ResultDeadlineElapsed, running.Sequence!.ReasonCode);
        Assert.True(running.Sequence.ObservedBeforePersisted);

        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);
        var late = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, late.State);
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, late.Sequence!.RecordedState);
    }

    [Fact]
    public async Task A_stop_that_won_keeps_stopping_even_when_the_finished_result_is_late()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.StopAsync(sequenceId);
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.Stopping, status.State);
    }

    [Fact]
    public async Task A_quarantined_operation_reads_as_awaiting_reconciliation_with_no_supported_recovery()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit));

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.True(status.Sequence!.AwaitingReconciliation);
        var receipt = Assert.Single(status.Sequence.Steps);
        Assert.True(receipt.Quarantined);
        Assert.True(receipt.Dispatched);
        Assert.False(receipt.ExitRecorded);
        Assert.Equal(receipt.OperationId, status.Sequence.OutstandingOperationId);
        Assert.Equal("no_supported_recovery_action_in_this_version", status.Sequence.NextActionCode);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
    }

    [Fact]
    public async Task A_recorded_terminal_result_with_exit_proof_is_not_quarantined_and_releases_the_fence()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit));

        var status = await scene.ReadStatusAsync();

        var receipt = Assert.Single(status.Sequence!.Steps);
        Assert.False(receipt.Quarantined);
        Assert.True(receipt.ExitRecorded);
        Assert.False(status.Sequence.AwaitingReconciliation);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, status.ManualFence);
        Assert.Equal("inspect_and_reconcile", status.Sequence.NextActionCode);
    }

    [Fact]
    public async Task An_incoherent_link_makes_the_fence_unavailable_instead_of_released()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit));
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
        var attemptId = (await scene.ReadStepsAsync())[0].OperationId!.Value;
        await scene.ExecuteSqlAsync("UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = {0}", attemptId);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceManualFence.Unavailable, status.ManualFence);
    }

    [Fact]
    public async Task A_run_without_a_sequence_reads_an_unavailable_fence_when_its_project_has_an_incoherent_link()
    {
        var (scene, sequenceId, _) = await ArmedAsync();
        await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedWithExit));
        var attemptId = (await scene.ReadStepsAsync())[0].OperationId!.Value;
        await scene.ExecuteSqlAsync("UPDATE attempts SET AgentResponseContract = 'Proposal' WHERE Id = {0}", attemptId);
        await using var context = fixture.CreateContext();
        var sibling = Run.RecordClassifiedIntent(
            Guid.NewGuid(), scene.ProjectId, 900, RunExecutionMode.ManualAgent, "Sibling", LocalExecutionSequenceTestScene.Start);
        context.Runs.Add(sibling);
        await context.SaveChangesAsync();

        var result = await new GetLocalExecutionSequenceQueryHandler(context, scene.Clock).HandleAsync(
            new GetLocalExecutionSequenceQuery(sibling.Id), CancellationToken.None);

        Assert.Null(result.Value.State);
        Assert.Equal(LocalExecutionSequenceManualFence.Unavailable, result.Value.ManualFence);
    }

    [Fact]
    public async Task The_receipts_of_a_whole_cycle_are_bounded_ordered_and_count_claims_passes_and_corrections()
    {
        var (scene, sequenceId, _) = await ArmedAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.CodeReview, 2, ReviewChangesRequested);
        await scene.DriveAsync(sequenceId, script);

        var status = await scene.ReadStatusAsync();

        var sequence = status.Sequence!;
        Assert.Equal(LocalExecutionSequencePolicy.MaximumSteps(1), sequence.Steps.Count);
        Assert.Equal(Enumerable.Range(0, sequence.Steps.Count), sequence.Steps.Select(step => step.Ordinal));
        Assert.All(sequence.Steps, step => Assert.Equal(LocalExecutionSequenceStepState.Concluded, step.State));
        Assert.Equal(2, sequence.CorrectionsClaimed);
        Assert.Equal(3, sequence.PassesStarted);
        Assert.Equal(6, sequence.AgentClaims);
        Assert.Equal(["Verification 1"], sequence.Steps.Where(step => step.RecipeName is not null).Select(step => step.RecipeName!).Distinct());
    }

    [Fact]
    public async Task A_database_row_beyond_the_step_bound_is_refused_by_the_guard_instead_of_returning_a_prefix()
    {
        var (scene, sequenceId, _) = await ArmedAsync(recipeCount: 1);
        // One recipe bounds the cycle to nine steps (ordinals zero to eight): the tenth can never be stored.
        await using var context = fixture.CreateContext();
        var row = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, 9, Step.Verification, 1, 0, Guid.NewGuid(), scene.CheckpointId, LocalExecutionSequenceTestScene.StartFingerprint,
            Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), LocalExecutionSequenceTestScene.Start);
        context.LocalExecutionSequenceSteps.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_approved_final_references_are_disclosed_only_when_the_linked_review_proves_them()
    {
        var (scene, sequenceId, _) = await ArmedAsync(recipeCount: 1);
        var ended = await scene.DriveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);

        await scene.ExecuteSqlAsync("UPDATE attempts SET AgentOutcome = 'ReviewChangesRequested' WHERE Id = {0}", ended.FinalReviewAttemptId!.Value);

        var tampered = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, tampered.State);
        Assert.Null(tampered.Sequence!.Final);
    }

    [Fact]
    public async Task The_escalation_final_references_are_disclosed_only_when_the_message_answers_the_named_report()
    {
        var (scene, sequenceId, _) = await ArmedAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested);
        var ended = await scene.DriveAsync(sequenceId, script);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        var proved = (await scene.ReadStatusAsync()).Sequence!.Final!;
        Assert.Equal(ended.FinalEscalationMessageId, proved.EscalationMessageId);
        Assert.Equal(ended.FinalReportMessageId, proved.ReportMessageId);
        Assert.Null(proved.ReviewAttemptId);

        await scene.ExecuteSqlAsync(
            "UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", ended.PlanProposalMessageId, ended.FinalEscalationMessageId!.Value);

        var tampered = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, tampered.State);
        Assert.Null(tampered.Sequence!.Final);
    }

    [Fact]
    public async Task A_stored_value_the_build_cannot_materialize_discloses_nothing_and_makes_the_fence_unavailable()
    {
        var (scene, _, _) = await ArmedAsync();
        await scene.ExecuteSqlAsync("PRAGMA ignore_check_constraints = 1");
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_sequence_immutable");
        await scene.ExecuteSqlAsync("UPDATE local_execution_sequences SET PlanAuthority = 'NotAnAuthority' WHERE RunId = {0}", scene.RunId);

        var status = await scene.ReadStatusAsync();

        Assert.Null(status.Sequence);
        Assert.Null(status.State);
        Assert.Equal(LocalExecutionSequenceManualFence.Unavailable, status.ManualFence);
    }
}
