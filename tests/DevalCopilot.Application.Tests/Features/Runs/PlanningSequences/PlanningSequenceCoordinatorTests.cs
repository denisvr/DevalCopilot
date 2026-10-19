using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningSequences.PlanningSequenceTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// The coordinator's decision step (ADR-0055), exercised through the real observation handler over real SQLite: it reserves the one
/// preparation intent of the next step only after proving the complete predecessor outcome, observes both deadlines without ever
/// terminating or releasing an attempt, drains a stopping sequence, and never advances a closed one. It selects nothing from a latest
/// message, a subset of challenges or a loaded window.
/// </summary>
public sealed class PlanningSequenceCoordinatorTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static async Task<Guid> ClaimNextAsync(PlanningSequenceTestScene scene, Guid sequenceId)
    {
        var (_, claim) = await scene.ReserveAndClaimAsync(sequenceId);
        return claim.AttemptId;
    }

    [Fact]
    public async Task An_armed_sequence_reserves_exactly_the_planner_step_and_hands_out_one_ticket()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);

        Assert.Equal(0, ticket.Ordinal);
        Assert.Null(ticket.PredecessorAttemptId);
        Assert.Null(ticket.InputProposalMessageId);
        var step = Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(PlanningSequenceStepState.Reserved, step.State);
        Assert.Equal(AgentRole.Planner, step.Role);
        Assert.Equal(ticket.ReservedAttemptId, step.ReservedAttemptId);
        Assert.Equal(PlanningSequenceState.PreparingStep, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceStepReserved));
        // Reserving claims nothing and consumes nothing.
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(0, scene.Artifacts.Seals);
    }

    [Fact]
    public async Task Two_concurrent_observations_reserve_exactly_one_step_and_only_one_receives_the_ticket()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        var results = await Task.WhenAll(
            Task.Run(() => scene.ObserveAsync(armed.SequenceId)),
            Task.Run(() => scene.ObserveAsync(armed.SequenceId)),
            Task.Run(() => scene.ObserveAsync(armed.SequenceId)));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null));
        Assert.Single(results, result => result.Value.ReservedStep is not null);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceStepReserved));
    }

    [Fact]
    public async Task A_repeated_observation_while_the_step_is_preparing_changes_nothing_and_never_hands_out_a_second_ticket()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.ObserveReservingAsync(armed.SequenceId);

        var again = await scene.ObserveAsync(armed.SequenceId);

        Assert.True(again.IsSuccess);
        Assert.Null(again.Value.ReservedStep);
        Assert.False(again.Value.Changed);
        Assert.Equal(PlanningSequenceState.PreparingStep, again.Value.State);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
    }

    [Fact]
    public async Task An_armed_sequence_whose_pinned_source_drifted_ends_as_attention_and_reserves_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), Start, new string('a', 40), new string('e', 64), []));
            await context.SaveChangesAsync();
        }

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.AuthorityChanged, sequence.ReasonCode);
        Assert.Empty(await scene.ReadStepsAsync(armed.SequenceId));
    }

    [Fact]
    public async Task A_preparation_that_outlives_two_minutes_is_revoked_and_the_sequence_needs_attention_without_a_claim()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        scene.Clock.Advance(PlanningSequencePolicy.PreparationDeadline - TimeSpan.FromSeconds(1));
        Assert.False((await scene.ObserveAsync(armed.SequenceId)).Value.Changed);

        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await scene.ObserveAsync(armed.SequenceId);

        Assert.True(expired.Value.Changed);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.PreparationExpired, sequence.ReasonCode);
        var step = Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(PlanningSequenceStepState.Revoked, step.State);
        Assert.Empty(await scene.ReadAttemptsAsync());

        // The original worker can no longer claim it and a later tick never re-reserves.
        var late = await scene.ClaimAsync(ticket);
        Assert.Equal(PlanningSequenceErrors.StepAuthorityLostCode, late.Errors[0].Code);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Empty(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_running_attempt_before_its_result_deadline_changes_nothing_and_after_it_the_sequence_needs_attention()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimNextAsync(scene, armed.SequenceId);
        var claimedAt = (await scene.ReadAttemptsAsync()).Single().ClaimedAtUtc;
        var deadline = claimedAt + TimeSpan.FromMinutes(10) + PlanningSequencePolicy.ResultGrace;

        scene.Clock.Advance(deadline - scene.Clock.GetUtcNow() - TimeSpan.FromSeconds(1));
        var waiting = await scene.ObserveAsync(armed.SequenceId);
        Assert.False(waiting.Value.Changed);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, (await scene.ReadSequenceAsync(armed.SequenceId)).State);

        scene.Clock.Advance(TimeSpan.FromSeconds(1));
        var elapsed = await scene.ObserveAsync(armed.SequenceId);

        Assert.True(elapsed.Value.Changed);
        Assert.Null(elapsed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.ResultDeadlineElapsed, sequence.ReasonCode);

        // Expiry never terminates the attempt, cancels a process, releases ownership or concludes the step.
        var attempt = Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(attemptId, attempt.Id);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Equal(PlanningSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task A_dispatched_attempt_that_outlives_its_deadline_is_reported_but_never_marked_terminal_by_the_coordinator()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimNextAsync(scene, armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.MarkAgentDispatched(scene.Clock.GetUtcNow());
            await context.SaveChangesAsync();
        }

        scene.Clock.Advance(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(1));
        await scene.ObserveAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.NeedsAttention, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        var persisted = Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.NotNull(persisted.AgentDispatchedAtUtc);

        // A late terminal record is retained but starts no next step.
        await scene.CompleteStepAsync(attemptId, StepResult.Proposed, dispatch: false);
        var late = await scene.ObserveAsync(armed.SequenceId);
        Assert.Null(late.Value.ReservedStep);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(PlanningSequenceState.NeedsAttention, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task An_accepted_review_of_the_root_ends_planning_as_ready_after_two_attempts_with_the_exact_references()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var planner = await ClaimNextAsync(scene, armed.SequenceId);
        await scene.CompleteStepAsync(planner, StepResult.Proposed);
        var review = await ClaimNextAsync(scene, armed.SequenceId);
        await scene.CompleteStepAsync(review, StepResult.Accepted);

        var ended = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(ended.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.ReadyForImplementation, sequence.State);
        Assert.Equal(PlanningSequenceReason.PlanAccepted, sequence.ReasonCode);
        Assert.All(await scene.ReadStepsAsync(armed.SequenceId), step => Assert.Equal(PlanningSequenceStepState.Concluded, step.State));
        Assert.Equal(2, (await scene.ReadAttemptsAsync()).Count);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceEnded));
    }

    [Fact]
    public async Task A_challenge_a_resolved_revision_and_an_acceptance_end_planning_after_four_attempts()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 2);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Resolved);
        var fourth = await ClaimNextAsync(scene, armed.SequenceId);
        await scene.CompleteStepAsync(fourth, StepResult.Accepted);

        await scene.ObserveAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(4, (await scene.ReadAttemptsAsync()).Count);
        Assert.Equal([0, 1, 2, 3], (await scene.ReadStepsAsync(armed.SequenceId)).Select(step => step.Ordinal));
    }

    [Fact]
    public async Task Two_challenge_rounds_end_at_the_human_escalation_after_five_attempts_and_never_a_sixth()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 1);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Resolved);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 2);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Resolved);

        var ended = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(ended.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.AwaitingHumanDecision, sequence.State);
        Assert.Equal(PlanningSequenceReason.SecondRoundEscalated, sequence.ReasonCode);
        Assert.Equal(5, (await scene.ReadAttemptsAsync()).Count);
        Assert.Equal(5, (await scene.ReadStepsAsync(armed.SequenceId)).Count);
        var again = await scene.ObserveAsync(armed.SequenceId);
        Assert.Null(again.Value.ReservedStep);
        Assert.Equal(5, (await scene.ReadStepsAsync(armed.SequenceId)).Count);
    }

    [Theory]
    [InlineData(StepResult.InvalidOutput, "invalid_structured_output")]
    [InlineData(StepResult.ProviderFailed, "provider_invocation_failed")]
    public async Task A_failed_step_ends_the_sequence_as_attention_with_its_closed_outcome_and_no_successor_or_retry(
        StepResult result, string detail)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), result);

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepFailed, sequence.ReasonCode);
        Assert.Equal(detail, sequence.DetailCode);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Single(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_dispatch_refused_attempt_is_a_failed_step_with_its_own_dedicated_outcome_detail()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimNextAsync(scene, armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.CompletePlanningSequenceDispatchRefused(scene.Clock.GetUtcNow());
            await context.SaveChangesAsync();
        }

        await scene.ObserveAsync(armed.SequenceId);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal("planning_sequence_dispatch_refused", sequence.DetailCode);
    }

    [Fact]
    public async Task An_interrupted_attempt_is_a_failed_step()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimNextAsync(scene, armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.Interrupt(scene.Clock.GetUtcNow());
            await context.SaveChangesAsync();
        }

        await scene.ObserveAsync(armed.SequenceId);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepFailed, sequence.ReasonCode);
    }

    [Fact]
    public async Task A_stop_that_won_before_classification_keeps_Stopped_even_when_the_provider_produced_a_valid_acceptance()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        var review = await ClaimNextAsync(scene, armed.SequenceId);
        Assert.Equal(PlanningSequenceState.Stopping, (await scene.StopAsync(armed.SequenceId)).Value.State);

        await scene.CompleteStepAsync(review, StepResult.Accepted);
        var drained = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(drained.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.Stopped, sequence.State);
        Assert.Equal(PlanningSequenceReason.StoppedAfterStep, sequence.ReasonCode);
        Assert.Equal(2, (await scene.ReadAttemptsAsync()).Count);
    }

    [Fact]
    public async Task A_stopping_sequence_does_not_advance_after_a_valid_proposal_and_creates_no_successor()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var planner = await ClaimNextAsync(scene, armed.SequenceId);
        Assert.Equal(PlanningSequenceState.Stopping, (await scene.StopAsync(armed.SequenceId)).Value.State);

        await scene.CompleteStepAsync(planner, StepResult.Proposed);
        await scene.ObserveAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.Stopped, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Single(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_late_completion_of_an_attempt_of_a_closed_sequence_is_retained_and_starts_nothing()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var attemptId = await ClaimNextAsync(scene, armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            var sequence = await context.PlanningSequences.SingleAsync(candidate => candidate.Id == armed.SequenceId);
            sequence.End(PlanningSequenceState.Interrupted, PlanningSequenceReason.HostRestarted, null, scene.Clock.GetUtcNow());
            await context.SaveChangesAsync();
        }

        await scene.CompleteStepAsync(attemptId, StepResult.Proposed);
        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        Assert.False(observed.Value.Changed);
        Assert.Equal(PlanningSequenceState.Interrupted, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(AttemptStatus.Completed, Assert.Single(await scene.ReadAttemptsAsync()).Status);
    }

    [Fact]
    public async Task Source_drift_between_steps_ends_the_sequence_and_a_newer_checkpoint_is_never_followed()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), Start, new string('a', 40), new string('e', 64), []));
            await context.SaveChangesAsync();
        }

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.AuthorityChanged, sequence.ReasonCode);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
    }

    [Fact]
    public async Task A_planner_result_that_cannot_be_proved_ends_the_sequence_without_a_successor()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var planner = await ClaimNextAsync(scene, armed.SequenceId);
        // Completed as Proposed but no Proposal message was ever recorded: nothing proves the output.
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == planner);
            attempt.MarkAgentDispatched(scene.Clock.GetUtcNow());
            attempt.CompleteAgent(AgentOutcome.Proposed, scene.FingerprintSha256, scene.Clock.GetUtcNow(), processEvidence: TestProcessEvidence.CleanExit);
            await context.SaveChangesAsync();
        }

        await scene.ObserveAsync(armed.SequenceId);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepOutcomeInvalid, sequence.ReasonCode);
        Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
    }

    [Theory]
    [InlineData("acceptance-missing")]
    [InlineData("challenge-wrong-reply")]
    [InlineData("zero-challenges")]
    public async Task A_review_result_that_cannot_be_proved_ends_the_sequence_without_a_successor(string flaw)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        var review = await ClaimNextAsync(scene, armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == review);
            attempt.MarkAgentDispatched(scene.Clock.GetUtcNow());
            var outcome = flaw == "acceptance-missing" ? AgentOutcome.Accepted : AgentOutcome.Challenged;
            attempt.CompleteAgent(outcome, scene.FingerprintSha256, scene.Clock.GetUtcNow(), processEvidence: TestProcessEvidence.CleanExit);
            if (flaw == "challenge-wrong-reply")
            {
                // A challenge that replies to something other than the reviewed Proposal.
                var elsewhere = await context.CollaborationMessages.AsNoTracking().FirstAsync(message => message.RunId == scene.RunId);
                context.CollaborationMessages.Add(CollaborationMessage.Record(
                    Guid.NewGuid(), scene.RunId, attempt.Id, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge, Guid.NewGuid(),
                    "Challenge.", """{"disputedItem":"d","materialImpact":"m","reasoning":"r","alternativeOrQuestion":"a"}""",
                    CollaborationMessageProvenance.ProviderObserved, scene.Clock.GetUtcNow()));
                Assert.NotNull(elsewhere);
            }

            await context.SaveChangesAsync();
        }

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepOutcomeInvalid, sequence.ReasonCode);
        Assert.Equal(2, (await scene.ReadStepsAsync(armed.SequenceId)).Count);
    }

    [Fact]
    public async Task A_depth_two_result_without_the_host_escalation_is_not_proved_and_never_reads_as_a_human_decision()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 1);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Resolved);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 1);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Resolved);
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM collaboration_messages WHERE RunId = {0} AND Type = 'Escalation'", scene.RunId);
        }

        await scene.ObserveAsync(armed.SequenceId);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepOutcomeInvalid, sequence.ReasonCode);
    }

    [Fact]
    public async Task A_resolver_that_decided_only_a_subset_of_the_challenges_is_not_a_proved_resolution()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Proposed);
        await scene.CompleteStepAsync(await ClaimNextAsync(scene, armed.SequenceId), StepResult.Challenged, challenges: 3);
        var resolver = await ClaimNextAsync(scene, armed.SequenceId);
        await scene.CompleteStepAsync(resolver, StepResult.Resolved);
        await using (var context = fixture.CreateContext())
        {
            // Drop one recorded input: the resolver's inputs are no longer the whole ordered challenge set of the review.
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 3", resolver);
        }

        await scene.ObserveAsync(armed.SequenceId);

        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.StepOutcomeInvalid, sequence.ReasonCode);
        Assert.Equal(3, (await scene.ReadStepsAsync(armed.SequenceId)).Count);
    }

    [Fact]
    public async Task A_proposal_of_another_run_of_the_project_never_replaces_the_predecessor_the_step_names()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);

        // Another run of the project already holds a completed Proposal by a manual attempt: a proposal that is not the step's.
        Guid foreignProposal;
        await using (var context = fixture.CreateContext())
        {
            var otherRun = Run.RecordClassifiedIntent(
                Guid.NewGuid(), scene.ProjectId, 2, RunExecutionMode.ManualAgent, "Other", Start);
            context.Runs.Add(otherRun);
            var manual = Attempt.ClaimAgent(
                Guid.NewGuid(), otherRun.Id, 1, scene.WorkspaceId, scene.CheckpointId, Fingerprint(), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Start, 1);
            manual.MarkAgentDispatched(Start);
            manual.CompleteAgent(AgentOutcome.Proposed, Fingerprint(), Start, processEvidence: TestProcessEvidence.CleanExit);
            context.Attempts.Add(manual);
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), otherRun.Id, manual.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null, "A later plan.",
                PlanningLineageSeeder.ProposalJson("Later"), CollaborationMessageProvenance.ProviderObserved, Start);
            foreignProposal = message.Id;
            context.CollaborationMessages.Add(message);
            await context.SaveChangesAsync();
        }

        var armed = await scene.ArmAsync();
        var planner = await ClaimNextAsync(scene, armed.SequenceId);
        await scene.CompleteStepAsync(planner, StepResult.Proposed);

        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);

        await using var verify = fixture.CreateContext();
        var ownProposal = await verify.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == planner && message.Type == CollaborationMessageType.Proposal);
        Assert.NotEqual(foreignProposal, ticket.InputProposalMessageId);
        Assert.Equal(ownProposal.Id, ticket.InputProposalMessageId);
        Assert.Equal(planner, ticket.PredecessorAttemptId);
    }

    private static string Fingerprint() => SecondChallengeRoundTestSupport.Fingerprint;
}
