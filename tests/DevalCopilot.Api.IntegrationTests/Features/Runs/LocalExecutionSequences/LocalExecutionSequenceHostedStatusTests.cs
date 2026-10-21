using System.Net;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060 final references through the protected status read over a REAL recorded cycle: the exact final is disclosed only
/// while the actual ReviewApproval or Escalation message exists. When that message is removed the read still answers 200 with the recorded
/// state, reason and step outcomes, but discloses no report, review or escalation reference.</summary>
public sealed class LocalExecutionSequenceHostedStatusTests
{
    private static async Task<JsonElement> ReadStatusAsync(LocalExecutionSequenceStatusHost host, Guid runId)
    {
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/api/runs/{runId}/local-execution-sequence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task RemoveMessageAsync(LocalExecutionSequenceHostedHarness harness, Func<CollaborationMessage, bool> select)
    {
        await using var db = harness.OpenContext();
        var message = (await db.CollaborationMessages.ToListAsync()).Single(select);
        db.CollaborationMessages.Remove(message);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_approved_cycle_discloses_its_exact_final_and_discloses_none_once_the_review_approval_is_removed()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync(recipeCount: 1);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        await harness.StopHostsAsync();
        using var host = new LocalExecutionSequenceStatusHost(harness.DatabasePath);

        var positive = (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence");
        var final = positive.GetProperty("final");
        Assert.Equal(sequence.FinalReportMessageId, final.GetProperty("reportMessageId").GetGuid());
        Assert.Equal(sequence.FinalReviewAttemptId, final.GetProperty("reviewAttemptId").GetGuid());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("escalationMessageId").ValueKind);

        await RemoveMessageAsync(harness, message => message.AttemptId == sequence.FinalReviewAttemptId
            && message.Type == CollaborationMessageType.ReviewApproval);

        var body = await ReadStatusAsync(host, lineage.RunId);
        var after = body.GetProperty("sequence");
        Assert.Equal("ReadyForHumanApproval", after.GetProperty("state").GetString());
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, after.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("final").ValueKind);
        var steps = after.GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        Assert.All(steps.EnumerateArray(), step => Assert.Equal("Concluded", step.GetProperty("state").GetString()));
        Assert.DoesNotContain(sequence.FinalReportMessageId!.Value.ToString(), after.GetProperty("final").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_exhausted_cycle_discloses_its_escalation_and_discloses_no_final_once_that_message_is_no_longer_the_host_escalation()
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
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        await harness.StopHostsAsync();
        using var host = new LocalExecutionSequenceStatusHost(harness.DatabasePath);

        var final = (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence").GetProperty("final");
        Assert.Equal(sequence.FinalReportMessageId, final.GetProperty("reportMessageId").GetGuid());
        Assert.Equal(sequence.FinalEscalationMessageId, final.GetProperty("escalationMessageId").GetGuid());

        // The sequence row references the message by foreign key, so it cannot be deleted: its recorded provenance is falsified instead.
        await using (var db = harness.OpenContext())
        {
            var message = await db.CollaborationMessages.SingleAsync(candidate => candidate.Id == sequence.FinalEscalationMessageId);
            var property = db.Entry(message).Property(candidate => candidate.Provenance);
            property.CurrentValue = CollaborationMessageProvenance.ProviderObserved;
            property.IsModified = true;
            await db.SaveChangesAsync();
        }

        var after = (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence");
        Assert.Equal("HumanEscalation", after.GetProperty("state").GetString());
        Assert.Equal(LocalExecutionSequenceReason.AllowanceReached, after.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("final").ValueKind);
        Assert.NotEqual(0, after.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task An_approved_cycle_discloses_no_final_once_the_review_source_fingerprint_no_longer_matches_its_step()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync(recipeCount: 1);
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        await harness.StopHostsAsync();
        using var host = new LocalExecutionSequenceStatusHost(harness.DatabasePath);
        Assert.NotEqual(JsonValueKind.Null, (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence").GetProperty("final").ValueKind);

        await using (var db = harness.OpenContext())
        {
            var review = await db.Attempts.SingleAsync(candidate => candidate.Id == sequence.FinalReviewAttemptId);
            db.Entry(review).Property(candidate => candidate.AgentCheckpointFingerprintSha256).CurrentValue = new string('e', 64);
            await db.SaveChangesAsync();
        }

        var after = (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence");
        Assert.Equal("ReadyForHumanApproval", after.GetProperty("state").GetString());
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, after.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("final").ValueKind);
        Assert.Equal(3, after.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task A_diagnosis_escalation_discloses_no_final_once_its_diagnosis_input_report_is_removed()
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
        await harness.StopHostsAsync();
        using var host = new LocalExecutionSequenceStatusHost(harness.DatabasePath);
        Assert.NotEqual(JsonValueKind.Null, (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence").GetProperty("final").ValueKind);
        var diagnosisId = (await harness.StepsAsync(sequence.Id)).Single(step => step.Kind == LocalExecutionStepKind.VerificationDiagnosis).OperationId!.Value;

        await using (var db = harness.OpenContext())
        {
            Assert.Equal(1, await db.AttemptInputMessages.Where(row => row.AttemptId == diagnosisId).ExecuteDeleteAsync());
        }

        var after = (await ReadStatusAsync(host, lineage.RunId)).GetProperty("sequence");
        Assert.Equal("HumanEscalation", after.GetProperty("state").GetString());
        Assert.Equal(LocalExecutionSequenceReason.DiagnosisEscalated, after.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("final").ValueKind);
        Assert.NotEqual(0, after.GetProperty("steps").GetArrayLength());
    }
}
