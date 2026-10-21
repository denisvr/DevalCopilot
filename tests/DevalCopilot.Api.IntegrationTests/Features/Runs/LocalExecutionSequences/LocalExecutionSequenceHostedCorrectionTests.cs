using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>Both correction origins of the ADR-0060 cycle through the real hosts: a review-origin correction, a diagnosis-origin
/// correction (the whole failed pass finishes first) and the two mixed in order, sharing the one cap of two automatic corrections.</summary>
public sealed class LocalExecutionSequenceHostedCorrectionTests
{
    [Fact]
    public async Task A_changes_requested_review_selects_every_finding_for_one_correction_then_a_second_pass_ends_approved()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Review.Answers.Enqueue(LocalExecutionSequenceResponses.ChangesRequested(2));
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(
            ["Implementation", .. Verifications(1, 2), "CodeReview", "ReviewCorrection", .. Verifications(2, 2), "CodeReview"],
            Trail(steps));
        AssertEveryStepConcluded(steps);
        await AssertEveryOperationIsLinkedAsync(harness, lineage, steps);
        AssertSerialPinnedOrder(harness, passes: 2, recipes: 2);
        Assert.Equal(2, harness.Process.ClaudeInvocations);
        Assert.Equal(2, harness.Review.InvocationCount);
        Assert.Equal(0, harness.Diagnosis.InvocationCount);

        var implementation = steps[0];
        var firstReview = steps.First(step => step.Kind == LocalExecutionStepKind.CodeReview);
        var correction = steps.Single(step => step.Kind == LocalExecutionStepKind.ReviewCorrection);
        var secondReview = steps.Last(step => step.Kind == LocalExecutionStepKind.CodeReview);
        await AssertCorrectionSelectedEveryFindingAsync(harness, correction, firstReview);
        Assert.Equal(2, await harness.InDbAsync(db => db.CollaborationMessages.CountAsync(
            message => message.AttemptId == firstReview.OperationId && message.Type == CollaborationMessageType.ReviewFinding)));
        await AssertPassRanAgainstAsync(harness, steps, 1, implementation.OperationId!.Value);
        await AssertPassRanAgainstAsync(harness, steps, 2, correction.OperationId!.Value);

        // The second review judges the correction's exact report, and its approval rests on the second pass alone.
        var correctedReport = await ReportOfAsync(harness, correction.OperationId);
        Assert.Equal(correctedReport.Id, secondReview.ReportMessageId);
        Assert.Equal(correctedReport.Id, sequence.FinalReportMessageId);
        Assert.Equal(secondReview.OperationId, sequence.FinalReviewAttemptId);
        var secondPass = steps.Where(step => step is { Kind: LocalExecutionStepKind.Verification, PassNumber: 2 }).Select(step => step.OperationId!.Value);
        var approval = await harness.InDbAsync(db => db.CheckpointReviews.Include(review => review.Evidence)
            .SingleAsync(review => review.Decision == ReviewDecision.Approved));
        Assert.Equal(secondPass.ToHashSet(), approval.Evidence.Select(item => item.VerificationExecutionId).ToHashSet());
        await AssertNothingBeyondTheCycleAsync(harness);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    [Fact]
    public async Task A_failed_recipe_finishes_the_whole_pass_then_one_diagnosis_selects_its_findings_for_a_correction()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.Verification = call => call is { RecipeNumber: 1, RecipeCall: 1 }
            ? LocalExecutionSequenceVerificationBehavior.Fails
            : LocalExecutionSequenceVerificationBehavior.Passes;
        harness.Diagnosis.Answers.Enqueue(LocalExecutionSequenceResponses.DiagnosisFindings(2));
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(
            ["Implementation", .. Verifications(1, 2), "VerificationDiagnosis", "DiagnosisCorrection", .. Verifications(2, 2), "CodeReview"],
            Trail(steps));
        AssertEveryStepConcluded(steps);
        await AssertEveryOperationIsLinkedAsync(harness, lineage, steps);
        AssertSerialPinnedOrder(harness, passes: 2, recipes: 2);

        // The failed recipe did not skip the second one, no review was claimed over a failed pass, and each provider ran once.
        Assert.Equal(1, harness.Diagnosis.InvocationCount);
        Assert.Equal(1, harness.Review.InvocationCount);
        Assert.Equal(2, harness.Process.ClaudeInvocations);
        var executions = await harness.ExecutionsAsync(lineage.ProjectId);
        Assert.Equal(
            [VerificationExecutionStatus.Failed, VerificationExecutionStatus.Passed, VerificationExecutionStatus.Passed, VerificationExecutionStatus.Passed],
            executions.Select(execution => execution.Status));

        var diagnosis = steps.Single(step => step.Kind == LocalExecutionStepKind.VerificationDiagnosis);
        var correction = steps.Single(step => step.Kind == LocalExecutionStepKind.DiagnosisCorrection);
        await AssertCorrectionSelectedEveryFindingAsync(harness, correction, diagnosis);
        var firstReport = await ReportOfAsync(harness, steps[0].OperationId);
        Assert.Equal(firstReport.Id, diagnosis.ReportMessageId);
        await AssertPassRanAgainstAsync(harness, steps, 1, steps[0].OperationId!.Value);
        await AssertPassRanAgainstAsync(harness, steps, 2, correction.OperationId!.Value);
        var manifest = harness.Diagnosis.ReceivedManifests.Single().Text;
        Assert.Contains(LocalExecutionSequenceResponses.FailureLine, manifest, StringComparison.Ordinal);
        var correctedReport = await ReportOfAsync(harness, correction.OperationId);
        Assert.Equal(correctedReport.Id, sequence.FinalReportMessageId);
        await AssertNothingBeyondTheCycleAsync(harness);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    [Fact]
    public async Task A_diagnosis_then_a_review_correction_share_the_cap_of_two_and_end_approved_after_the_third_pass()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.Verification = call => call is { RecipeNumber: 2, RecipeCall: 1 }
            ? LocalExecutionSequenceVerificationBehavior.Fails
            : LocalExecutionSequenceVerificationBehavior.Passes;
        harness.Diagnosis.Answers.Enqueue(LocalExecutionSequenceResponses.DiagnosisFindings(1));
        harness.Review.Answers.Enqueue(LocalExecutionSequenceResponses.ChangesRequested(1));
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId, TimeSpan.FromSeconds(120));

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, sequence.ReasonCode);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(
            [
                "Implementation", .. Verifications(1, 2), "VerificationDiagnosis", "DiagnosisCorrection",
                .. Verifications(2, 2), "CodeReview", "ReviewCorrection",
                .. Verifications(3, 2), "CodeReview",
            ],
            Trail(steps));
        Assert.Equal(LocalExecutionSequencePolicy.MaximumSteps(2), steps.Count);
        AssertEveryStepConcluded(steps);
        await AssertEveryOperationIsLinkedAsync(harness, lineage, steps);
        AssertSerialPinnedOrder(harness, passes: 3, recipes: 2);

        // Six Agent claims in total: the implementation, one diagnosis, two reviews and exactly the two shared corrections.
        var attempts = await harness.AgentAttemptsAsync(lineage.RunId);
        Assert.Equal(6, attempts.Count(attempt => steps.Any(step => step.OperationId == attempt.Id)));
        Assert.Equal(2, attempts.Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        Assert.Equal(3, harness.Process.ClaudeInvocations);
        Assert.Equal(2, harness.Review.InvocationCount);
        Assert.Equal(1, harness.Diagnosis.InvocationCount);

        var diagnosis = steps.Single(step => step.Kind == LocalExecutionStepKind.VerificationDiagnosis);
        var diagnosisCorrection = steps.Single(step => step.Kind == LocalExecutionStepKind.DiagnosisCorrection);
        var firstReview = steps.First(step => step.Kind == LocalExecutionStepKind.CodeReview);
        var reviewCorrection = steps.Single(step => step.Kind == LocalExecutionStepKind.ReviewCorrection);
        var lastReview = steps.Last(step => step.Kind == LocalExecutionStepKind.CodeReview);
        await AssertCorrectionSelectedEveryFindingAsync(harness, diagnosisCorrection, diagnosis);
        await AssertCorrectionSelectedEveryFindingAsync(harness, reviewCorrection, firstReview);
        await AssertPassRanAgainstAsync(harness, steps, 2, diagnosisCorrection.OperationId!.Value);
        await AssertPassRanAgainstAsync(harness, steps, 3, reviewCorrection.OperationId!.Value);

        // The chain of reports: each correction answers the report that came before it.
        var implementationReport = await ReportOfAsync(harness, steps[0].OperationId);
        var firstCorrectedReport = await ReportOfAsync(harness, diagnosisCorrection.OperationId);
        var secondCorrectedReport = await ReportOfAsync(harness, reviewCorrection.OperationId);
        Assert.Equal(implementationReport.Id, diagnosis.ReportMessageId);
        Assert.Equal(firstCorrectedReport.Id, firstReview.ReportMessageId);
        Assert.Equal(secondCorrectedReport.Id, lastReview.ReportMessageId);
        Assert.Equal(secondCorrectedReport.Id, sequence.FinalReportMessageId);
        Assert.Equal(lastReview.OperationId, sequence.FinalReviewAttemptId);
        await AssertNothingBeyondTheCycleAsync(harness);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    [Fact]
    public async Task A_depth_two_human_authorized_plan_runs_the_cycle_and_consumes_only_its_own_grant()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync(depthTwo: true);
        Assert.NotEqual(lineage.RootId, lineage.PlanId);
        Assert.Null(await harness.InDbAsync(db => db.PlanningImplementationAuthorizations.Select(grant => grant.ConsumedByAttemptId).SingleAsync()));
        var armed = await harness.ArmAsync(lineage);
        Assert.Equal(lineage.PlanId, armed.PlanProposalMessageId);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(LocalExecutionPlanAuthority.HumanAuthorizedRevision, sequence.PlanAuthority);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", .. Verifications(1, 2), "CodeReview"], Trail(steps));
        // The initial implementation consumed the one separate Human grant; nothing else was granted.
        Assert.Equal(steps[0].OperationId, await harness.InDbAsync(db => db.PlanningImplementationAuthorizations.Select(grant => grant.ConsumedByAttemptId).SingleAsync()));
        await AssertNothingBeyondTheCycleAsync(harness, depthTwo: true);
    }
}
