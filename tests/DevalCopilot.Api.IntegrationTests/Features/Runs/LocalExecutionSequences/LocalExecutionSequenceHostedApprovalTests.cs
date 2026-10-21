using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The ADR-0060 cycle end to end through the real coordinator and the real supervisors over deterministic process and adapter doubles:
/// one arming request, then every claim, dispatch, verification, review, diagnosis and correction is composed by the host. Every plan
/// row is written by the production planning commands. Each scenario asserts durable rows and the exact invocation counts of every
/// double; none proves real-provider reliability, process death or recovery.
/// </summary>
public sealed class LocalExecutionSequenceHostedApprovalTests
{
    [Fact]
    public async Task Zero_correction_cycle_runs_every_recipe_once_in_order_and_ends_at_the_ordinary_approved_review()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        var armed = await harness.ArmAsync(lineage);
        Assert.False(armed.Replayed);
        Assert.Equal(0, harness.Process.ClaudeInvocations + harness.Process.VerificationInvocations + harness.Review.InvocationCount);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, sequence.ReasonCode);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", .. Verifications(1, 2), "CodeReview"], Trail(steps));
        AssertEveryStepConcluded(steps);
        await AssertEveryOperationIsLinkedAsync(harness, lineage, steps);
        AssertSerialPinnedOrder(harness, passes: 1, recipes: 2);

        // Exactly one provider invocation per operation and no further request.
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        Assert.Equal(1, harness.Review.InvocationCount);
        Assert.Equal(0, harness.Diagnosis.InvocationCount);
        Assert.Equal(steps.Where(step => step.Kind == LocalExecutionStepKind.Implementation).Select(step => step.OperationId!.Value), harness.Process.ClaudeAttemptIds);

        // The final references are the exact implementation report and the exact approving review attempt.
        var implementation = steps.Single(step => step.Kind == LocalExecutionStepKind.Implementation);
        var review = steps.Single(step => step.Kind == LocalExecutionStepKind.CodeReview);
        var report = await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == implementation.OperationId && message.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(report.Id, sequence.FinalReportMessageId);
        Assert.Equal(review.OperationId, sequence.FinalReviewAttemptId);
        Assert.Equal(report.Id, review.ReportMessageId);
        Assert.Null(sequence.FinalEscalationMessageId);
        var approval = await harness.InDbAsync(db => db.CheckpointReviews.Include(candidate => candidate.Evidence).SingleAsync());
        Assert.Equal(ReviewDecision.Approved, approval.Decision);
        Assert.Equal(ReviewActorKind.FutureAgent, approval.ActorKind);
        var passExecutions = (await harness.ExecutionsAsync(lineage.ProjectId)).Select(execution => execution.Id).ToHashSet();
        Assert.Equal(passExecutions, approval.Evidence.Select(item => item.VerificationExecutionId).ToHashSet());

        Assert.Equal(0, CorrectionsClaimed(await harness.AgentAttemptsAsync(lineage.RunId)));
        await AssertNothingBeyondTheCycleAsync(harness);
        var events = await EventCountsAsync(harness, lineage.RunId);
        Assert.Equal(1, events[RunEventType.LocalExecutionSequenceArmed]);
        Assert.Equal(steps.Count, events[RunEventType.LocalExecutionSequenceStepClaimed]);
        Assert.Equal(1, events[RunEventType.LocalExecutionSequenceEnded]);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    private static int CorrectionsClaimed(IEnumerable<Attempt> attempts) =>
        attempts.Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection);
}
