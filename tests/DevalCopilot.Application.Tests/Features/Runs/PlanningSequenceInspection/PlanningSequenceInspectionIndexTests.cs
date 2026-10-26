using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.ResolvePlanningSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequenceInspection;

/// <summary>The recorded index of one run's planning sequence (ADR-0068) over real file-backed SQLite and the whole ADR-0055 sequence
/// driven through the production arming, observation, claim and stop handlers: NotRecorded, zero through five steps, every fixed role,
/// stopped, stopping, interrupted, attention and unresolved shapes, exact members equal to the recorded rows, historical stability after
/// legitimate newer facts, and a read-only, no-forbidden-column, no-external-observation proof.</summary>
public sealed class PlanningSequenceInspectionIndexTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task An_unknown_run_is_one_fixed_not_found()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);

        var result = await harness.TryIndexAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PlanningSequenceInspectionErrors.NotFoundCode, result.Errors[0].Code);
    }

    [Theory]
    [InlineData(RunExecutionMode.ManualAgent)]
    [InlineData(RunExecutionMode.Legacy)]
    public async Task A_known_run_without_a_sequence_is_not_recorded_with_no_header_and_no_steps(RunExecutionMode mode)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture, mode: mode);

        var index = await new PlanningSequenceInspectionHarness(scene, Guid.Empty).IndexAsync();

        Assert.Equal(scene.RunId, index.RunId);
        Assert.Equal(PlanningSequenceInspectionStatus.NotRecorded, index.Status);
        Assert.Null(index.Sequence);
        Assert.Empty(index.Steps);
    }

    [Fact]
    public async Task An_armed_sequence_without_steps_is_recorded_with_its_exact_stored_header()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var stored = await harness.Scene.ReadSequenceAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.Empty(index.Steps);
        var header = index.Sequence!;
        Assert.Equal(harness.SequenceId, header.SequenceId);
        Assert.Equal(harness.Scene.WorkspaceId, header.WorkspaceId);
        Assert.Equal(harness.Scene.CheckpointId, header.CheckpointId);
        Assert.Equal(harness.Scene.FingerprintSha256, header.FingerprintSha256);
        Assert.Equal(PlanningSequenceState.Armed, header.RecordedState);
        Assert.Null(header.ReasonCode);
        Assert.Equal(stored.Revision, header.Revision);
        Assert.Equal(stored.ArmedAtUtc, header.ArmedAtUtc);
        Assert.Equal(stored.UpdatedAtUtc, header.UpdatedAtUtc);
        Assert.Null(header.StopRequestedAtUtc);
        Assert.Null(header.EndedAtUtc);
    }

    [Fact]
    public async Task A_reserved_step_and_a_claimed_step_show_their_recorded_state_and_only_the_claimed_one_a_link()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var ticket = await harness.Scene.ObserveReservingAsync(harness.SequenceId);

        var reserved = Assert.Single((await harness.IndexAsync()).Steps);

        Assert.Equal(PlanningSequenceStepState.Reserved, reserved.RecordedState);
        Assert.Equal(0, reserved.Ordinal);
        Assert.Equal(AgentRole.Planner, reserved.Role);
        Assert.Equal(AgentProvider.Codex, reserved.Provider);
        Assert.Equal(AgentResponseContract.Proposal, reserved.ResponseContract);
        Assert.Null(reserved.AttemptId);
        Assert.Null(reserved.PredecessorAttemptId);
        Assert.Null(reserved.InputProposalMessageId);
        Assert.Null(reserved.ReasonCode);

        var claim = await harness.Scene.ClaimAsync(ticket);
        var claimed = Assert.Single((await harness.IndexAsync()).Steps);

        Assert.Equal(PlanningSequenceStepState.Claimed, claimed.RecordedState);
        Assert.Equal(claim.Value.AttemptId, claimed.AttemptId);
        Assert.Equal(ticket.ReservedAttemptId, claimed.AttemptId);
        Assert.Equal(reserved.StepId, claimed.StepId);
        Assert.True(claimed.Revision > reserved.Revision);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Every_count_of_steps_lists_exactly_that_many_members_with_fixed_roles_and_members_equal_to_the_recorded_rows(int count)
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var attempts = await harness.DriveStepsAsync(count);
        var rows = await harness.RowsAsync();

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(count, index.Steps.Count);
        Assert.Equal(count, rows.Count);
        AgentRole[] roles = [AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.CriticalReviewer, AgentRole.Resolver];
        AgentProvider[] providers = [AgentProvider.Codex, AgentProvider.ClaudeCode, AgentProvider.Codex, AgentProvider.ClaudeCode, AgentProvider.Codex];
        AgentResponseContract[] contracts =
        [
            AgentResponseContract.Proposal,
            AgentResponseContract.CriticalReview,
            AgentResponseContract.ChallengeResolution,
            AgentResponseContract.CriticalReview,
            AgentResponseContract.ChallengeResolution,
        ];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var member = index.Steps[ordinal];
            var row = rows[ordinal];
            Assert.Equal(row.Id, member.StepId);
            Assert.Equal(ordinal, member.Ordinal);
            Assert.Equal(roles[ordinal], member.Role);
            Assert.Equal(providers[ordinal], member.Provider);
            Assert.Equal(contracts[ordinal], member.ResponseContract);
            Assert.Equal(row.State, member.RecordedState);
            Assert.Equal(row.ReasonCode, member.ReasonCode);
            Assert.Equal(row.Revision, member.Revision);
            Assert.Equal(row.PredecessorAttemptId, member.PredecessorAttemptId);
            Assert.Equal(row.InputProposalMessageId, member.InputProposalMessageId);
            Assert.Equal(row.AttemptId, member.AttemptId);
            Assert.Equal(attempts[ordinal], member.AttemptId);
            Assert.Equal(row.PreparationReservedAtUtc, member.PreparationReservedAtUtc);
            Assert.Equal(row.PreparationDeadlineUtc, member.PreparationDeadlineUtc);
            Assert.Equal(row.UpdatedAtUtc, member.UpdatedAtUtc);
            Assert.Equal(ordinal == count - 1 ? PlanningSequenceStepState.Claimed : PlanningSequenceStepState.Concluded, member.RecordedState);
            if (ordinal > 0)
            {
                Assert.Equal(attempts[ordinal - 1], member.PredecessorAttemptId);
                Assert.NotNull(member.InputProposalMessageId);
            }
        }

        // A Resolver reviews the very Proposal its preceding review reviewed.
        if (count >= 3)
        {
            Assert.Equal(index.Steps[1].InputProposalMessageId, index.Steps[2].InputProposalMessageId);
        }

        if (count == 5)
        {
            Assert.Equal(index.Steps[3].InputProposalMessageId, index.Steps[4].InputProposalMessageId);
            Assert.NotEqual(index.Steps[1].InputProposalMessageId, index.Steps[3].InputProposalMessageId);
        }
    }

    [Fact]
    public async Task A_sequence_that_ended_after_five_steps_shows_its_recorded_state_reason_and_every_step_concluded()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.DriveStepsAsync(5);
        await harness.Scene.ObserveAsync(harness.SequenceId);
        var stored = await harness.Scene.ReadSequenceAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(5, index.Steps.Count);
        Assert.All(index.Steps, step => Assert.Equal(PlanningSequenceStepState.Concluded, step.RecordedState));
        Assert.Equal(PlanningSequenceState.AwaitingHumanDecision, index.Sequence!.RecordedState);
        Assert.Equal(PlanningSequenceReason.SecondRoundEscalated, index.Sequence.ReasonCode);
        Assert.Equal(stored.EndedAtUtc, index.Sequence.EndedAtUtc);
        Assert.Equal(stored.Revision, index.Sequence.Revision);
    }

    [Fact]
    public async Task An_accepted_root_ends_after_two_steps_and_the_index_asserts_no_acceptance_beyond_recorded_bookkeeping()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.RunStepAsync(StepResult.Proposed);
        await harness.RunStepAsync(StepResult.Accepted);
        await harness.Scene.ObserveAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(2, index.Steps.Count);
        Assert.Equal(PlanningSequenceState.ReadyForImplementation, index.Sequence!.RecordedState);
        Assert.Equal(PlanningSequenceReason.PlanAccepted, index.Sequence.ReasonCode);
    }

    [Fact]
    public async Task A_failed_step_is_attention_with_its_fixed_reason_and_the_provider_failure_detail_is_never_disclosed()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.RunStepAsync(StepResult.ProviderFailed);
        await harness.Scene.ObserveAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceState.NeedsAttention, index.Sequence!.RecordedState);
        Assert.Equal(PlanningSequenceReason.StepFailed, index.Sequence.ReasonCode);
        Assert.Equal(PlanningSequenceStepState.Concluded, Assert.Single(index.Steps).RecordedState);
        Assert.DoesNotContain(typeof(PlanningSequenceInspectionHeader).GetProperties(), property => property.Name.Contains("Detail"));
    }

    [Fact]
    public async Task A_stop_before_any_claim_is_recorded_as_a_stopped_sequence_with_a_revoked_step_and_no_link()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ObserveReservingAsync(harness.SequenceId);
        await harness.Scene.StopAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceState.Stopped, index.Sequence!.RecordedState);
        Assert.Equal(PlanningSequenceReason.StoppedBeforeClaim, index.Sequence.ReasonCode);
        Assert.NotNull(index.Sequence.StopRequestedAtUtc);
        Assert.NotNull(index.Sequence.EndedAtUtc);
        var step = Assert.Single(index.Steps);
        Assert.Equal(PlanningSequenceStepState.Revoked, step.RecordedState);
        Assert.Equal(PlanningSequenceReason.StoppedBeforeClaim, step.ReasonCode);
        Assert.Null(step.AttemptId);
    }

    [Fact]
    public async Task A_stop_after_a_claim_is_stopping_with_the_stop_time_and_the_still_claimed_step_linked()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        await harness.Scene.StopAsync(harness.SequenceId);

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceState.Stopping, index.Sequence!.RecordedState);
        Assert.NotNull(index.Sequence.StopRequestedAtUtc);
        Assert.Null(index.Sequence.EndedAtUtc);
        Assert.Null(index.Sequence.ReasonCode);
        var step = Assert.Single(index.Steps);
        Assert.Equal(PlanningSequenceStepState.Claimed, step.RecordedState);
        Assert.NotNull(step.AttemptId);
    }

    [Fact]
    public async Task An_interrupted_sequence_with_a_claimed_attempt_that_never_settled_is_recorded_without_requiring_it_to_be_terminal()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var (_, claim) = await harness.Scene.ReserveAndClaimAsync(harness.SequenceId);
        await using (var db = fixture.CreateContext())
        {
            await new InterruptPlanningSequencesCommandHandler(db, harness.Scene.Clock).HandleAsync(
                new InterruptPlanningSequencesCommand(), CancellationToken.None);
        }

        var index = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.Equal(PlanningSequenceState.Interrupted, index.Sequence!.RecordedState);
        Assert.Equal(PlanningSequenceReason.HostRestarted, index.Sequence.ReasonCode);
        var step = Assert.Single(index.Steps);
        Assert.Equal(PlanningSequenceStepState.Claimed, step.RecordedState);
        Assert.Equal(claim.AttemptId, step.AttemptId);
        Assert.Equal(AttemptStatus.Running, (await harness.Scene.ReadAttemptsAsync()).Single().Status);
    }

    [Fact]
    public async Task An_interrupted_preparation_is_a_revoked_step_and_an_unresolved_preparation_stays_unresolved_with_no_link()
    {
        var revoked = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await revoked.Scene.ObserveReservingAsync(revoked.SequenceId);
        await using (var db = fixture.CreateContext())
        {
            await new InterruptPlanningSequencesCommandHandler(db, revoked.Scene.Clock).HandleAsync(
                new InterruptPlanningSequencesCommand(), CancellationToken.None);
        }

        var unresolved = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        var ticket = await unresolved.Scene.ObserveReservingAsync(unresolved.SequenceId);
        await using (var db = fixture.CreateContext())
        {
            await new ResolvePlanningSequenceStepPreparationCommandHandler(db, new UnresolvedProbe(), unresolved.Scene.Clock).HandleAsync(
                new ResolvePlanningSequenceStepPreparationCommand(ticket, "attempts.persistence_unresolved"), CancellationToken.None);
        }

        var revokedIndex = await revoked.IndexAsync();
        var unresolvedIndex = await unresolved.IndexAsync();

        var revokedStep = Assert.Single(revokedIndex.Steps);
        Assert.Equal(PlanningSequenceStepState.Revoked, revokedStep.RecordedState);
        Assert.Null(revokedStep.AttemptId);
        Assert.Equal(PlanningSequenceState.Interrupted, revokedIndex.Sequence!.RecordedState);
        var unresolvedStep = Assert.Single(unresolvedIndex.Steps);
        Assert.Equal(PlanningSequenceStepState.Unresolved, unresolvedStep.RecordedState);
        Assert.Equal(PlanningSequenceReason.PreparationUnresolved, unresolvedStep.ReasonCode);
        Assert.Null(unresolvedStep.AttemptId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, unresolvedIndex.Sequence!.RecordedState);
    }

    [Fact]
    public async Task Later_legitimate_facts_do_not_invalidate_a_coherent_recorded_sequence()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.DriveStepsAsync(2);
        await harness.Scene.ObserveAsync(harness.SequenceId);
        var before = await harness.IndexAsync();

        // A newer checkpoint and workspace of the same project, a newer run and manual attempts elsewhere change the latest rows only.
        await using (var db = fixture.CreateContext())
        {
            var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == harness.Scene.WorkspaceId);
            var number = workspace.ReserveCheckpointNumber();
            db.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), workspace.Id, number, harness.Scene.Clock.GetUtcNow(), new string('b', 40), new string('c', 64), []));
            await db.SaveChangesAsync();
        }

        await PlanningSequenceTestScene.CreateAsync(fixture);

        var after = await harness.IndexAsync();

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, after.Status);
        Assert.Equal(before.Sequence, after.Sequence);
        Assert.Equal(before.Steps, after.Steps);
    }

    [Fact]
    public async Task The_read_is_select_only_never_selects_a_forbidden_column_and_observes_nothing_externally()
    {
        var harness = await PlanningSequenceInspectionHarness.ArmedAsync(fixture);
        await harness.DriveStepsAsync(3);
        var before = await harness.ChangeFingerprintAsync();
        var captures = harness.Scene.Evidence.Captures;
        var seals = harness.Scene.Artifacts.Seals;
        var log = new StatementLog();

        var index = await harness.IndexAsync(null, log);

        Assert.Equal(PlanningSequenceInspectionStatus.Recorded, index.Status);
        Assert.NotEmpty(log.Statements);
        log.AssertReadOnly();
        log.AssertNeverSelects(
            "ObjectiveSha256", "DetailCode", "PreparationOwnerToken", "StructuredContentJson", "\"Summary\"", "WorkspacePath", "BranchName",
            "SourceCommitSha", "Provenance");
        Assert.Equal(before, await harness.ChangeFingerprintAsync());
        Assert.Equal(captures, harness.Scene.Evidence.Captures);
        Assert.Equal(seals, harness.Scene.Artifacts.Seals);
    }

    private sealed class UnresolvedProbe : IPlanningSequenceStepProbe
    {
        public Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanningSequenceStepProbeResult.Unresolved);
    }
}
