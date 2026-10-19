using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0055 through the real coordinator, the real mediator and claims, and the three real provider supervisors over a file-backed
/// SQLite database with production migrations: one explicit arming reaches a terminal planning outcome with no per-stage request, no
/// message transfer and no unauthorized external call. Every identity is verified from durable rows, never from display text.
/// </summary>
public sealed class PlanningSequenceHostedPathTests
{
    [Fact]
    public async Task An_accepted_planner_proposal_ends_planning_after_two_attempts_with_exact_durable_identities()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, ended.State);
        Assert.Equal(PlanningSequenceReason.PlanAccepted, ended.ReasonCode);
        Assert.Equal(1, harness.Planner.InvocationCount);
        Assert.Equal(1, harness.Reviewer.InvocationCount);
        Assert.Equal(0, harness.Resolver.InvocationCount);

        await using var context = harness.OpenContext();
        var steps = await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed.SequenceId)
            .OrderBy(step => step.Ordinal).ToListAsync();
        Assert.Equal([0, 1], steps.Select(step => step.Ordinal));
        Assert.All(steps, step => Assert.Equal(PlanningSequenceStepState.Concluded, step.State));
        Assert.All(steps, step => Assert.Equal(step.ReservedAttemptId, step.AttemptId));

        var attempts = await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId)
            .OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        Assert.Equal(steps.Select(step => step.AttemptId!.Value), attempts.Select(attempt => attempt.Id));
        Assert.Equal([AgentRole.Planner, AgentRole.CriticalReviewer], attempts.Select(attempt => attempt.AgentRole!.Value));
        Assert.Equal([1, 2], attempts.Select(attempt => attempt.AgentBudgetSlot!.Value));

        var proposal = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attempts[0].Id && message.Type == CollaborationMessageType.Proposal);
        var acceptance = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attempts[1].Id && message.Type == CollaborationMessageType.Acceptance);
        Assert.Equal(proposal.Id, steps[1].InputProposalMessageId);
        Assert.Equal(attempts[0].Id, steps[1].PredecessorAttemptId);
        var reviewInputs = await context.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attempts[1].Id).OrderBy(input => input.Sequence).ToListAsync();
        Assert.Equal([proposal.Id], reviewInputs.Select(input => input.CollaborationMessageId));

        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(2, attempts.Count);

        var status = (await harness.SendAsync(new GetPlanningSequenceQuery(scene.RunId))).Value;
        Assert.Equal(PlanningSequenceState.ReadyForImplementation, status.State);
        Assert.Equal(PlanningSequenceManualFence.Released, status.ManualFence);
        Assert.Equal(proposal.Id, status.Sequence!.Final!.FinalProposalMessageId);
        Assert.Equal(acceptance.Id, status.Sequence.Final.AcceptanceMessageId);
        Assert.Null(status.Sequence.Final.EscalationMessageId);
        Assert.Equal(attempts[1].Id, status.Sequence.Final.ProvingAttemptId);
    }

    [Fact]
    public async Task A_challenge_then_a_resolved_revision_then_an_acceptance_ends_planning_after_four_attempts()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        harness.Reviewer.Decisions.Enqueue(2);
        harness.Reviewer.Decisions.Enqueue(0);
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, ended.State);
        Assert.Equal(1, harness.Planner.InvocationCount);
        Assert.Equal(2, harness.Reviewer.InvocationCount);
        Assert.Equal(1, harness.Resolver.InvocationCount);

        await using var context = harness.OpenContext();
        var steps = await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed.SequenceId)
            .OrderBy(step => step.Ordinal).ToListAsync();
        Assert.Equal([0, 1, 2, 3], steps.Select(step => step.Ordinal));
        var attempts = await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId)
            .OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        Assert.Equal(steps.Select(step => step.AttemptId!.Value), attempts.Select(attempt => attempt.Id));

        // The resolver consumed the reviewed Proposal and then exactly the complete, ordered Challenge set of that exact review.
        var proposal = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attempts[0].Id && message.Type == CollaborationMessageType.Proposal);
        var challenges = await context.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == attempts[1].Id && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence).ToListAsync();
        Assert.Equal(2, challenges.Count);
        var resolverInputs = await context.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attempts[2].Id).OrderBy(input => input.Sequence).ToListAsync();
        Assert.Equal(
            new[] { proposal.Id }.Concat(challenges.Select(challenge => challenge.Id)),
            resolverInputs.Select(input => input.CollaborationMessageId));
        Assert.Equal(attempts[1].Id, steps[2].PredecessorAttemptId);
        Assert.Equal(proposal.Id, steps[2].InputProposalMessageId);

        // The second review consumed exactly the first revision, never the root.
        var revision = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attempts[2].Id && message.Type == CollaborationMessageType.Proposal);
        var secondReviewInputs = await context.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attempts[3].Id).OrderBy(input => input.Sequence).ToListAsync();
        Assert.Equal([revision.Id], secondReviewInputs.Select(input => input.CollaborationMessageId));
        Assert.Equal(revision.Id, steps[3].InputProposalMessageId);

        var status = (await harness.SendAsync(new GetPlanningSequenceQuery(scene.RunId))).Value;
        Assert.Equal(revision.Id, status.Sequence!.Final!.FinalProposalMessageId);
        Assert.NotNull(status.Sequence.Final.AcceptanceMessageId);
        Assert.Equal(attempts[3].Id, status.Sequence.Final.ProvingAttemptId);
    }

    [Fact]
    public async Task Two_challenge_rounds_end_at_the_existing_human_escalation_after_five_attempts_without_starting_implementation()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        harness.Reviewer.Decisions.Enqueue(1);
        harness.Reviewer.Decisions.Enqueue(2);
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.AwaitingHumanDecision, ended.State);
        Assert.Equal(PlanningSequenceReason.SecondRoundEscalated, ended.ReasonCode);
        Assert.Equal(1, harness.Planner.InvocationCount);
        Assert.Equal(2, harness.Reviewer.InvocationCount);
        Assert.Equal(2, harness.Resolver.InvocationCount);

        await using var context = harness.OpenContext();
        var steps = await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed.SequenceId)
            .OrderBy(step => step.Ordinal).ToListAsync();
        Assert.Equal([0, 1, 2, 3, 4], steps.Select(step => step.Ordinal));
        Assert.All(steps, step => Assert.Equal(PlanningSequenceStepState.Concluded, step.State));
        var attempts = await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId)
            .OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        Assert.Equal(5, attempts.Count);
        Assert.Equal([1, 2, 3, 4, 5], attempts.Select(attempt => attempt.AgentBudgetSlot!.Value));

        // No implementation claim, authorization or completed run ever exists.
        Assert.DoesNotContain(attempts, attempt => attempt.AgentRole == AgentRole.Implementer);
        Assert.Empty(await context.PlanningImplementationAuthorizations.AsNoTracking().Where(grant => grant.RunId == scene.RunId).ToListAsync());
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == scene.RunId);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);

        var status = (await harness.SendAsync(new GetPlanningSequenceQuery(scene.RunId))).Value;
        var final = status.Sequence!.Final!;
        var finalProposal = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == attempts[4].Id && message.Type == CollaborationMessageType.Proposal);
        Assert.Equal(finalProposal.Id, final.FinalProposalMessageId);
        Assert.Null(final.AcceptanceMessageId);
        var escalation = await context.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.Type == CollaborationMessageType.Escalation && message.RunId == scene.RunId);
        Assert.Equal(escalation.Id, final.EscalationMessageId);
        Assert.Equal(finalProposal.Id, escalation.InReplyToMessageId);
        Assert.Equal(PlanningSequenceManualFence.Released, status.ManualFence);
    }
}
