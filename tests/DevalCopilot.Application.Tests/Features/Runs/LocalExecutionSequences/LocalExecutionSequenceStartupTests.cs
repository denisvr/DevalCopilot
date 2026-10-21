using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The startup closure of local execution sequences (ADR-0060), run before any recovery or supervisor: it interrupts every live
/// sequence, revokes unclaimed preparations (or marks one unresolved when its reserved operation unexpectedly exists), closes a proved
/// undispatched claim with the dedicated refusal without any adapter call, leaves a linked dispatched operation exactly as recorded,
/// never creates a successor and is idempotent.
/// </summary>
public sealed class LocalExecutionSequenceStartupTests : IAsyncLifetime
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
    public async Task An_armed_sequence_is_interrupted_with_the_host_restart_reason_and_never_resumed()
    {
        var (scene, sequenceId) = await ArmedAsync();

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(0, closed.RefusedUndispatchedCount);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, sequence.ReasonCode);
        Assert.NotNull(sequence.EndedAtUtc);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));
        var observed = await scene.ObserveAsync(sequenceId);
        Assert.Null(observed.Value.ReservedStep);
        Assert.Empty(await scene.ReadStepsAsync());
        Assert.Equal(0, scene.Artifacts.SealCount);
    }

    [Fact]
    public async Task The_closure_is_idempotent()
    {
        var (scene, _) = await ArmedAsync();
        await scene.InterruptAsync();
        var revision = (await scene.ReadSequenceAsync()).Revision;

        var second = await scene.InterruptAsync();

        Assert.Equal(0, second.InterruptedCount);
        Assert.Equal(0, second.RefusedUndispatchedCount);
        Assert.Equal(revision, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));
    }

    [Fact]
    public async Task A_reserved_preparation_is_revoked_and_its_ticket_can_never_claim()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, step.ReasonCode);
        Assert.Equal(
            Application.Features.Runs.Errors.LocalExecutionSequenceErrors.StepAuthorityLostCode, (await scene.ClaimAsync(ticket)).Errors[0].Code);
        Assert.Empty(await scene.ReadLocalAttemptsAsync());
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task A_reserved_step_whose_operation_unexpectedly_exists_is_marked_unresolved_and_keeps_the_fence()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ObserveReservingAsync(sequenceId);
        // A committed claim the step never recorded (as a crash between operation and link would leave): the exact reserved identity exists.
        await using (var context = fixture.CreateContext())
        {
            context.Attempts.Add(Attempt.ClaimAgentImplementation(
                ticket.ReservedOperationId, scene.RunId, 90, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceTestScene.StartFingerprint,
                Guid.NewGuid(), TimeSpan.FromMinutes(20), 1024, 2048, LocalExecutionSequenceTestScene.Start, 90));
            await context.SaveChangesAsync();
        }

        await scene.InterruptAsync();

        var step = Assert.Single(await scene.ReadStepsAsync());
        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, step.State);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task A_proved_undispatched_agent_claim_is_closed_with_the_dedicated_refusal_without_any_adapter_call()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        var captures = scene.Evidence.Captures;
        var seals = scene.Artifacts.SealCount;

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(1, closed.RefusedUndispatchedCount);
        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.NotEqual(AttemptStatus.Running, attempt.Status);
        Assert.Equal(captures, scene.Evidence.Captures);
        Assert.Equal(seals, scene.Artifacts.SealCount);
        // The row is a recognized terminal one with no process to wait for: the fence releases and nothing is advanced.
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadMessagesAsync(CollaborationMessageType.ExecutionReport));
    }

    [Fact]
    public async Task A_proved_undispatched_verification_claim_is_closed_with_the_dedicated_status()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var driven = await scene.StepAsync(sequenceId);
        Assert.Equal(Step.Implementation, driven.Ticket!.Kind);
        var verification = await scene.ReserveAndClaimAsync(sequenceId);
        Assert.Equal(Step.Verification, verification.Kind);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(1, closed.RefusedUndispatchedCount);
        var execution = await scene.ReadExecutionAsync(verification.ReservedOperationId);
        Assert.Equal(VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused, execution.Status);
        Assert.Null(execution.DispatchedAtUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.CompletionFingerprintSha256);
        Assert.Equal(LocalExecutionSequenceManualFence.Released, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task A_linked_dispatched_agent_operation_is_left_exactly_as_recorded_and_keeps_every_owner_and_the_fence()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        var before = await scene.ReadAttemptAsync(ticket.ReservedOperationId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(0, closed.RefusedUndispatchedCount);
        var after = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AttemptStatus.Running, after.Status);
        Assert.Equal(before.AgentDispatchedAtUtc, after.AgentDispatchedAtUtc);
        Assert.Null(after.AgentOutcome);
        Assert.Null(after.CompletedAtUtc);
        var status = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, status.State);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.True(status.Sequence!.AwaitingReconciliation);
        Assert.Equal(ticket.ReservedOperationId, status.Sequence.OutstandingOperationId);
        Assert.Equal("no_supported_recovery_action_in_this_version", status.Sequence.NextActionCode);
        // No successor, no repair, no import of potentially live output.
        Assert.Null((await scene.ObserveAsync(sequenceId)).Value.ReservedStep);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadMessagesAsync(CollaborationMessageType.ExecutionReport));
    }

    [Fact]
    public async Task A_linked_dispatched_verification_is_left_running_and_still_owns_the_workspace()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var verification = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(verification.Kind, verification.ReservedOperationId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(0, closed.RefusedUndispatchedCount);
        var execution = await scene.ReadExecutionAsync(verification.ReservedOperationId);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.NotNull(execution.DispatchedAtUtc);
        Assert.Equal(LocalExecutionSequenceManualFence.Blocked, (await scene.ReadStatusAsync()).ManualFence);
    }

    [Fact]
    public async Task A_result_that_returns_late_in_its_original_host_is_recorded_but_never_reopens_the_interrupted_sequence()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.InterruptAsync();

        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);
        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(AttemptStatus.Completed, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        Assert.False(observed.Value.Changed);
        Assert.Null(observed.Value.ReservedStep);
        var status = await scene.ReadStatusAsync();
        Assert.Equal(LocalExecutionSequenceState.Interrupted, status.State);
        // Coherent host exit evidence now exists: the fence of the closed sequence releases, never resumes it.
        Assert.Equal(LocalExecutionSequenceManualFence.Released, status.ManualFence);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadExecutionsAsync());
    }

    [Fact]
    public async Task A_stopping_sequence_is_interrupted_too()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.ReserveAndClaimAsync(sequenceId);
        await scene.StopAsync(sequenceId);

        var closed = await scene.InterruptAsync();

        Assert.Equal(1, closed.InterruptedCount);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
    }

    [Fact]
    public async Task Ended_sequences_are_never_touched_and_a_dispatched_result_between_phases_is_not_adopted()
    {
        var (scene, sequenceId) = await ArmedAsync(recipeCount: 1);
        var ended = await scene.DriveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);

        var closed = await scene.InterruptAsync();

        Assert.Equal(0, closed.InterruptedCount);
        Assert.Equal(0, closed.RefusedUndispatchedCount);
        var after = await scene.ReadSequenceAsync();
        Assert.Equal(ended.Revision, after.Revision);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, after.State);
    }

    [Fact]
    public async Task A_result_known_at_restart_between_two_phases_is_never_classified_into_a_successor()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);
        await scene.RecordImplementationAsync(ticket.ReservedOperationId, Implemented);

        await scene.InterruptAsync();
        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.Interrupted, (await scene.ReadSequenceAsync()).State);
        Assert.Null(observed.Value.ReservedStep);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Empty(await scene.ReadExecutionsAsync());
    }

    [Fact]
    public async Task Only_the_sequences_that_can_still_advance_are_interrupted_across_runs()
    {
        var (scene, _) = await ArmedAsync();
        await using var other = new SqliteDatabaseFixture();
        await other.InitializeAsync();
        var second = await LocalExecutionSequenceTestScene.CreateAsync(other);
        var plan = await second.AddAcceptedRootPlanAsync();
        var secondId = (await second.ArmAsync(plan.ProposalId)).SequenceId;
        await second.StopAsync(secondId);

        var closedFirst = await scene.InterruptAsync();
        var closedSecond = await second.InterruptAsync();

        Assert.Equal(1, closedFirst.InterruptedCount);
        Assert.Equal(0, closedSecond.InterruptedCount);
        Assert.Equal(LocalExecutionSequenceState.Stopped, (await second.ReadSequenceAsync()).State);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task The_dedicated_refusal_recorders_refuse_a_dispatched_or_unlinked_operation_and_fabricate_nothing()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.DispatchAsync(ticket.Kind, ticket.ReservedOperationId);

        var dispatched = await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId);
        var unknown = await scene.RecordAgentDispatchRefusalAsync(Guid.NewGuid());

        Assert.True(dispatched.IsFailure);
        Assert.True(unknown.IsFailure);
        var attempt = await scene.ReadAttemptAsync(ticket.ReservedOperationId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public async Task A_still_dispatchable_undispatched_claim_is_never_refused_by_the_recorder()
    {
        var (scene, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveAndClaimAsync(sequenceId);

        var refused = await scene.RecordAgentDispatchRefusalAsync(ticket.ReservedOperationId);

        Assert.True(refused.IsFailure);
        Assert.Equal(AttemptStatus.Running, (await scene.ReadAttemptAsync(ticket.ReservedOperationId)).Status);
        Assert.True((await scene.DispatchAgentAsync(ticket.ReservedOperationId)).IsSuccess);
    }

    [Fact]
    public async Task A_verification_dispatch_marker_is_refused_dedicatedly_once_the_sequence_ended()
    {
        var (scene, sequenceId) = await ArmedAsync();
        await scene.StepAsync(sequenceId);
        var verification = await scene.ReserveAndClaimAsync(sequenceId);
        await scene.StopAsync(sequenceId);
        scene.Clock.Advance(TimeSpan.FromMinutes(10));
        await scene.ObserveAsync(sequenceId);
        await scene.InterruptAsync();

        var dispatch = await scene.DispatchVerificationAsync(verification.ReservedOperationId);

        // The startup closure already closed the proved-undispatched claim with the dedicated status.
        Assert.True(dispatch.IsFailure);
        Assert.Equal(
            VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused,
            (await scene.ReadExecutionAsync(verification.ReservedOperationId)).Status);
        Assert.Contains(
            dispatch.Errors[0].Code,
            new[] { MarkVerificationExecutionDispatchedCommandHandler.LocalExecutionSequenceDispatchRefusedCode, "verification.execution_not_dispatchable" });
        Assert.True((await scene.RecordVerificationDispatchRefusalAsync(verification.ReservedOperationId)).IsSuccess);
    }
}
