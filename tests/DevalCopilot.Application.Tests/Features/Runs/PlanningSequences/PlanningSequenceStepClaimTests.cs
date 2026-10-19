using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DevalCopilot.Domain.Features.Projects;
using static DevalCopilot.Application.Tests.Features.Runs.FaultInjectingDbContext;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningSequences.PlanningSequenceTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0055 step claims: the step-to-attempt link, the attempt, its ordered inputs, its artifact metadata and its budget consumption
/// commit in one transaction with no claim-then-link gap; a Stop, an expiry or a drifted source that wins leaves nothing consumed;
/// a commit that is ambiguous is resolved only by an independent read of the exact step/attempt association and never authorizes
/// another claim attempt.
/// </summary>
public sealed class PlanningSequenceStepClaimTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task A_planner_step_commits_the_attempt_the_manifest_the_budget_slot_and_the_link_together()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();

        var (ticket, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);

        Assert.Equal(ticket.ReservedAttemptId, claim.AttemptId);
        var step = Assert.Single(await scene.ReadStepsAsync(armed.SequenceId));
        Assert.Equal(PlanningSequenceStepState.Claimed, step.State);
        Assert.Equal(claim.AttemptId, step.AttemptId);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, sequence.State);

        var attempt = Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(claim.AttemptId, attempt.Id);
        Assert.Equal(AgentRole.Planner, attempt.AgentRole);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentResponseContract.Proposal, attempt.AgentResponseContract);
        Assert.Equal(1, attempt.AgentBudgetSlot);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(scene.WorkspaceId, attempt.AgentGitWorkspaceId);
        Assert.Equal(scene.CheckpointId, attempt.AgentGitCheckpointId);
        Assert.Equal(Fingerprint, attempt.AgentCheckpointFingerprintSha256);

        await using var context = fixture.CreateContext();
        var artifact = Assert.Single(await context.Artifacts.AsNoTracking().Where(candidate => candidate.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(ArtifactPurpose.AgentContextManifest, artifact.Purpose);
        Assert.Equal(attempt.AgentContextManifestArtifactId, artifact.Id);
        Assert.Empty(await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == attempt.Id).ToListAsync());
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.PlanningSequenceStepClaimed));
        Assert.Equal(1, scene.Artifacts.Seals);
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
    }

    [Fact]
    public async Task A_later_step_claims_exactly_the_predecessor_proposal_and_a_resolver_the_whole_ordered_challenge_set()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, planner) = await scene.ReserveAndClaimAsync(armed.SequenceId);
        await scene.CompleteStepAsync(planner.AttemptId, StepResult.Proposed);
        var review = await scene.ReserveAndClaimAsync(armed.SequenceId);
        await scene.CompleteStepAsync(review.Claim.AttemptId, StepResult.Challenged, challenges: 3);

        // The observation that classified the review reserved the resolver step; its inputs are the proposal then all three challenges.
        var resolverTicket = await scene.ObserveReservingAsync(armed.SequenceId);
        var resolver = await scene.ClaimAsync(resolverTicket);
        Assert.True(resolver.IsSuccess, resolver.IsFailure ? resolver.Errors[0].Code : null);

        await using var context = fixture.CreateContext();
        var proposal = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == planner.AttemptId && message.Type == CollaborationMessageType.Proposal);
        var challenges = await context.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == review.Claim.AttemptId && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();
        Assert.Equal(3, challenges.Count);

        var reviewInputs = await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == review.Claim.AttemptId)
            .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync();
        Assert.Equal([proposal.Id], reviewInputs);
        var resolverInputs = await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == resolver.Value.AttemptId)
            .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync();
        Assert.Equal(new[] { proposal.Id }.Concat(challenges), resolverInputs);

        var steps = await scene.ReadStepsAsync(armed.SequenceId);
        Assert.Equal(review.Claim.AttemptId, steps[2].PredecessorAttemptId);
        Assert.Equal(proposal.Id, steps[2].InputProposalMessageId);
        Assert.Equal([1, 2, 3], (await scene.ReadAttemptsAsync()).Select(attempt => attempt.AgentBudgetSlot!.Value));
    }

    [Fact]
    public async Task A_stop_that_revoked_the_preparation_first_leaves_the_claim_with_nothing_consumed()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        Assert.True((await scene.StopAsync(armed.SequenceId)).IsSuccess);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
        Assert.Equal(0, scene.Artifacts.Seals);
    }

    [Fact]
    public async Task A_stop_that_lands_between_the_early_read_and_the_commit_seam_revokes_the_claim_at_the_seam()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            // After the claim's early reads, the Git capture and the manifest seal, and before its transaction begins.
            BeforeBeginTransaction = async _ => Assert.True((await scene.StopAsync(armed.SequenceId)).IsSuccess),
        };

        var claim = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceState.Stopped, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
    }

    [Fact]
    public async Task A_claim_that_wins_before_a_stop_lets_the_claimed_step_finish_and_the_stop_only_forbids_a_successor()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, claim) = await scene.ReserveAndClaimAsync(armed.SequenceId);

        var stopped = await scene.StopAsync(armed.SequenceId);

        Assert.True(stopped.IsSuccess);
        Assert.True(stopped.Value.Changed);
        Assert.Equal(PlanningSequenceState.Stopping, stopped.Value.State);
        var attempt = Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(claim.AttemptId, attempt.Id);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(PlanningSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
    }

    [Fact]
    public async Task A_step_whose_preparation_deadline_elapsed_cannot_be_claimed()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        scene.Clock.Advance(PlanningSequencePolicy.PreparationDeadline);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepExpiredCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Equal(0, scene.Artifacts.Seals);
    }

    [Fact]
    public async Task The_deadline_is_rechecked_at_the_seam_after_external_work_consumed_the_time()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        scene.Evidence.OnCapture = () =>
        {
            scene.Clock.Advance(PlanningSequencePolicy.PreparationDeadline + TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        };

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepExpiredCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
    }

    [Theory]
    [InlineData("newer-checkpoint")]
    [InlineData("lease-released")]
    [InlineData("workspace-not-ready")]
    [InlineData("mode-simulated")]
    [InlineData("capture-drifted")]
    public async Task Source_lease_and_mode_drift_refuses_the_claim_without_consuming_anything(string drift)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            switch (drift)
            {
                case "newer-checkpoint":
                    var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
                    context.GitCheckpoints.Add(GitCheckpoint.Capture(
                        Guid.NewGuid(), scene.WorkspaceId, workspace.ReserveCheckpointNumber(), Start, new string('a', 40), new string('e', 64), []));
                    await context.SaveChangesAsync();
                    break;
                case "lease-released":
                    await context.RepositoryMutationLeases.Where(candidate => candidate.WorkspaceId == scene.WorkspaceId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, DevalCopilot.Domain.Features.Projects.LeaseStatus.Released));
                    break;
                case "workspace-not-ready":
                    await context.GitWorkspaces.Where(candidate => candidate.Id == scene.WorkspaceId).ExecuteUpdateAsync(setters =>
                        setters.SetProperty(candidate => candidate.Status, DevalCopilot.Domain.Features.Projects.WorkspaceStatus.NeedsAttention));
                    break;
                case "mode-simulated":
                    await RunExecutionModeTestSupport.SetStoredModeAsync(context, scene.RunId, (int)RunExecutionMode.Simulated);
                    break;
                default:
                    scene.Evidence.Fingerprint = new string('9', 64);
                    break;
            }
        }

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
    }

    [Fact]
    public async Task A_changed_objective_is_source_drift_and_a_newer_checkpoint_is_never_followed()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE runs SET Objective = 'A different objective' WHERE Id = {0}", scene.RunId);
        }

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepSourceChangedCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_forged_ticket_for_a_step_that_was_never_reserved_is_refused_before_any_external_work()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var real = await scene.ObserveReservingAsync(armed.SequenceId);
        var forged = new PlanningSequenceStepTicket(
            real.SequenceId, real.StepId, real.Ordinal, real.ReservedAttemptId, Guid.NewGuid(), real.PredecessorAttemptId, real.InputProposalMessageId);

        var claim = await scene.ClaimAsync(forged);

        Assert.True(claim.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepAuthorityLostCode, claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(0, scene.Artifacts.Seals);
    }

    [Fact]
    public async Task Two_workers_that_race_the_same_ticket_create_exactly_one_attempt_and_the_loser_never_deletes_the_winners_manifest()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        var bothPastTheEarlyChecks = new Barrier(2);
        var sharedStore = new RecordingArtifactStore(onSeal: () =>
        {
            bothPastTheEarlyChecks.SignalAndWait(TimeSpan.FromSeconds(10));
            return Task.CompletedTask;
        });

        var first = Task.Run(() => scene.ClaimAsync(ticket, store: sharedStore));
        var second = Task.Run(() => scene.ClaimAsync(ticket, store: sharedStore));
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailure);
        Assert.Equal(PlanningSequenceErrors.StepAuthorityLostCode, loser.Errors[0].Code);
        Assert.Single(await scene.ReadAttemptsAsync());
        await using var context = fixture.CreateContext();
        Assert.Single(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
        Assert.Empty(sharedStore.DeletedSealedFiles);
    }

    [Fact]
    public async Task A_claim_that_fails_to_save_rolls_everything_back_and_the_independent_read_proves_it_absent()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave };

        var claim = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(claim.IsFailure);
        Assert.Equal("attempts.persistence_failed", claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Equal(PlanningSequenceState.PreparingStep, (await scene.ReadSequenceAsync(armed.SequenceId)).State);
        Assert.Equal(PlanningSequenceStepProbeResult.NotClaimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Artifacts.AsNoTracking().Where(artifact => artifact.RunId == scene.RunId).ToListAsync());
    }

    [Fact]
    public async Task A_commit_that_failed_after_committing_is_resolved_as_the_claimed_step_and_keeps_the_manifest()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.AfterCommit };

        var claim = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(claim.IsSuccess);
        Assert.Equal(ticket.ReservedAttemptId, claim.Value.AttemptId);
        Assert.Equal(PlanningSequenceStepState.Claimed, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
        Assert.Equal(PlanningSequenceStepProbeResult.Claimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
    }

    [Fact]
    public async Task A_commit_that_failed_before_committing_is_absent_and_removes_the_orphaned_manifest()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.BeforeCommit };

        var claim = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(claim.IsFailure);
        Assert.Equal("attempts.persistence_failed", claim.Errors[0].Code);
        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Reserved, Assert.Single(await scene.ReadStepsAsync(armed.SequenceId)).State);
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
    }

    [Fact]
    public async Task An_unresolved_outcome_preserves_a_possibly_referenced_manifest_and_authorizes_no_retry()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.AfterCommit };

        var claim = await scene.ClaimAsync(ticket, db: faulting, stepProbe: new UnresolvedStepProbe());

        Assert.True(claim.IsFailure);
        Assert.Equal("attempts.persistence_unresolved", claim.Errors[0].Code);
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
        // The claim actually committed: the coordinator's independent resolution reads it back and never claims again.
        Assert.Single(await scene.ReadAttemptsAsync());
    }

    [Fact]
    public async Task A_claim_without_a_step_probe_treats_an_ambiguous_outcome_as_unresolved_never_as_absent()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.AfterCommit };
        await using var claimContext = fixture.CreateContext();
        var handler = new DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt.CreateCodexPlanningAttemptCommandHandler(
            faulting, scene.Evidence, scene.Artifacts, scene.Clock, scene.AttemptProbe);

        var result = await handler.HandleAsync(
            new DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt.CreateCodexPlanningAttemptCommand(scene.RunId)
            { PlanningStep = ticket }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_unresolved", result.Errors[0].Code);
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
    }

    [Fact]
    public async Task A_cancellation_after_the_commit_still_resolves_by_the_independent_read_and_propagates()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.CancellationAfterCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(() => scene.ClaimAsync(ticket, db: faulting));

        Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Empty(scene.Artifacts.DeletedSealedFiles);
        Assert.Equal(PlanningSequenceStepProbeResult.Claimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancellation_before_the_commit_proves_absence_and_removes_the_orphan()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { CommitFailure = CommitFailureMode.CancellationBeforeCommit };

        await Assert.ThrowsAsync<OperationCanceledException>(() => scene.ClaimAsync(ticket, db: faulting));

        Assert.Empty(await scene.ReadAttemptsAsync());
        Assert.Single(scene.Artifacts.DeletedSealedFiles);
        Assert.Equal(PlanningSequenceStepProbeResult.NotClaimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));
    }

    [Fact]
    public async Task The_review_step_claim_commits_atomically_and_a_failed_review_save_leaves_the_step_reserved()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (_, planner) = await scene.ReserveAndClaimAsync(armed.SequenceId);
        await scene.CompleteStepAsync(planner.AttemptId, StepResult.Proposed);
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);
        await using var inner = fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner) { SaveChangesFailure = SaveChangesFailureMode.UpdateExceptionBeforeSave };

        var failed = await scene.ClaimAsync(ticket, db: faulting);

        Assert.True(failed.IsFailure);
        Assert.Single(await scene.ReadAttemptsAsync());
        Assert.Equal(PlanningSequenceStepState.Reserved, (await scene.ReadStepsAsync(armed.SequenceId))[1].State);
        Assert.Equal(PlanningSequenceStepProbeResult.NotClaimed, await scene.StepProbe.CheckAsync(ticket.StepId, CancellationToken.None));

        // The reservation authority itself was never consumed by the failure: this one preparation intent may still be tried by its
        // original worker until its deadline (the coordinator never does: an absent result ends advancement instead).
        var claimed = await scene.ClaimAsync(ticket);
        Assert.True(claimed.IsSuccess, claimed.IsFailure ? claimed.Errors[0].Code : null);
        var attempts = await scene.ReadAttemptsAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(AgentRole.CriticalReviewer, attempts[1].AgentRole);
        Assert.Equal(PlanningSequenceStepState.Claimed, (await scene.ReadStepsAsync(armed.SequenceId))[1].State);
    }

    [Fact]
    public async Task A_claim_that_exceeds_the_budget_is_refused_without_consuming_a_slot_and_keeps_the_original_reason()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture, maximumAgentAttempts: 1);
        var armed = await scene.ArmAsync();
        var (_, planner) = await scene.ReserveAndClaimAsync(armed.SequenceId);
        await scene.CompleteStepAsync(planner.AttemptId, StepResult.Proposed);
        var ticket = await scene.ObserveReservingAsync(armed.SequenceId);

        var claim = await scene.ClaimAsync(ticket);

        Assert.True(claim.IsFailure);
        Assert.Equal("agent_attempts.budget_exhausted", claim.Errors[0].Code);
        Assert.Single(await scene.ReadAttemptsAsync());
    }

    private sealed class UnresolvedStepProbe : IPlanningSequenceStepProbe
    {
        public Task<PlanningSequenceStepProbeResult> CheckAsync(Guid stepId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanningSequenceStepProbeResult.Unresolved);
    }
}
