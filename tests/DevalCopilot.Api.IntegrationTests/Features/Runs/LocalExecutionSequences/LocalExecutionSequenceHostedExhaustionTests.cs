using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The ADR-0060 ends that are not an approval: exhaustion of the shared allowance with unresolved findings records exactly one
/// fixed host escalation (never an ordinary claim that could consume a Human grant), and a provider diagnosis Escalation is preserved
/// exactly, with no host message added over it.</summary>
public sealed class LocalExecutionSequenceHostedExhaustionTests
{
    private static readonly string[] FixedContent =
    [
        "The authorized local execution cycle has unresolved findings.",
        "Inspect the recorded evidence and decide on an explicit manual next action. This cycle cannot continue.",
        "No further operation is authorized by this sequence; the run remains awaiting human action.",
        "The authorized automatic correction allowance was reached.",
        "Inspect the evidence before requesting any manual continuation.",
    ];

    private static async Task AssertOneFixedHostEscalationAsync(
        LocalExecutionSequenceHostedHarness harness, LocalExecutionSequenceLineage lineage, LocalExecutionSequence sequence, Guid lastReportId)
    {
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AllowanceReached, sequence.ReasonCode);
        Assert.Equal(lastReportId, sequence.FinalReportMessageId);
        Assert.Null(sequence.FinalReviewAttemptId);
        var escalations = await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking()
            .Where(message => message.RunId == lineage.RunId && message.Type == CollaborationMessageType.Escalation).ToListAsync());
        var escalation = Assert.Single(escalations);
        Assert.Equal(escalation.Id, sequence.FinalEscalationMessageId);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, escalation.Provenance);
        Assert.Equal(ParticipantKind.Orchestrator, escalation.ActorKind);
        Assert.Equal(ParticipantKind.Human, escalation.RecipientKind);
        Assert.Equal(lastReportId, escalation.InReplyToMessageId);
        Assert.Null(escalation.AttemptId);
        Assert.Equal("Local execution cycle requires a human decision.", escalation.Summary);
        var content = JsonSerializer.Deserialize<JsonElement>(escalation.StructuredContentJson);
        Assert.Equal(
            ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            content.EnumerateObject().Select(member => member.Name));
        Assert.Equal(FixedContent, content.EnumerateObject().Select(member => member.Value.GetString()));
        Assert.Equal(1, (await EventCountsAsync(harness, lineage.RunId))[RunEventType.LocalExecutionSequenceEnded]);
        // The ordinary escalation and the Human extra-correction grants were never touched.
        await AssertNothingBeyondTheCycleAsync(harness);
    }

    [Fact]
    public async Task Review_findings_left_after_the_second_correction_end_in_one_fixed_host_escalation_on_the_exact_current_report()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync(recipeCount: 1);
        for (var round = 0; round < 3; round++)
        {
            harness.Review.Answers.Enqueue(LocalExecutionSequenceResponses.ChangesRequested(1));
        }

        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(
            [
                "Implementation", .. Verifications(1, 1), "CodeReview", "ReviewCorrection",
                .. Verifications(2, 1), "CodeReview", "ReviewCorrection", .. Verifications(3, 1), "CodeReview",
            ],
            Trail(steps));
        AssertEveryStepConcluded(steps);
        var lastReport = await ReportOfAsync(harness, steps.Last(step => step.Kind == LocalExecutionStepKind.ReviewCorrection).OperationId);
        await AssertOneFixedHostEscalationAsync(harness, lineage, sequence, lastReport.Id);

        // The allowance was never probed by a third claim: exactly two corrections, three reviews, three Claude-free passes of the recipe.
        var attempts = await harness.AgentAttemptsAsync(lineage.RunId);
        Assert.Equal(2, attempts.Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        Assert.Equal(3, harness.Review.InvocationCount);
        Assert.Equal(3, harness.Process.ClaudeInvocations);
        Assert.Equal(3, harness.Process.VerificationInvocations);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    [Fact]
    public async Task Diagnosis_findings_left_after_the_second_correction_end_in_the_same_fixed_host_escalation()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync(recipeCount: 1);
        harness.Process.Verification = _ => LocalExecutionSequenceVerificationBehavior.Fails;
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(
            [
                "Implementation", .. Verifications(1, 1), "VerificationDiagnosis", "DiagnosisCorrection",
                .. Verifications(2, 1), "VerificationDiagnosis", "DiagnosisCorrection", .. Verifications(3, 1), "VerificationDiagnosis",
            ],
            Trail(steps));
        AssertEveryStepConcluded(steps);
        var lastReport = await ReportOfAsync(harness, steps.Last(step => step.Kind == LocalExecutionStepKind.DiagnosisCorrection).OperationId);
        await AssertOneFixedHostEscalationAsync(harness, lineage, sequence, lastReport.Id);
        Assert.Equal(3, harness.Diagnosis.InvocationCount);
        Assert.Equal(0, harness.Review.InvocationCount);
        Assert.Equal(3, harness.Process.ClaudeInvocations);
        await AssertQuietAfterEndAsync(harness, lineage);
    }

    [Fact]
    public async Task A_diagnosis_escalation_is_preserved_as_the_final_escalation_and_no_host_escalation_is_added()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.Verification = call => call.RecipeNumber == 2
            ? LocalExecutionSequenceVerificationBehavior.Fails
            : LocalExecutionSequenceVerificationBehavior.Passes;
        harness.Diagnosis.Answers.Enqueue(LocalExecutionSequenceResponses.DiagnosisEscalationJson);
        var armed = await harness.ArmAsync(lineage);

        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.DiagnosisEscalated, sequence.ReasonCode);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", .. Verifications(1, 2), "VerificationDiagnosis"], Trail(steps));
        AssertEveryStepConcluded(steps);
        var diagnosis = steps[^1];
        var escalation = await harness.InDbAsync(db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.RunId == lineage.RunId && message.Type == CollaborationMessageType.Escalation));
        Assert.Equal(diagnosis.OperationId, escalation.AttemptId);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, escalation.Provenance);
        Assert.Equal(escalation.Id, sequence.FinalEscalationMessageId);
        var report = await ReportOfAsync(harness, steps[0].OperationId);
        Assert.Equal(report.Id, sequence.FinalReportMessageId);
        Assert.Equal(report.Id, escalation.InReplyToMessageId);

        // Nothing was corrected, reviewed or approved, and no correction authority was created.
        Assert.Equal(1, harness.Process.ClaudeInvocations);
        Assert.Equal(0, harness.Review.InvocationCount);
        Assert.Equal(1, harness.Diagnosis.InvocationCount);
        Assert.Empty(await harness.InDbAsync(db => db.DiagnosisCorrectionEscalations.ToListAsync()));
        await AssertNothingBeyondTheCycleAsync(harness);
        await AssertQuietAfterEndAsync(harness, lineage);
    }
}
