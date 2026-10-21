using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// LCO-R2 completion (ADR-0060): both Agent finals prove the exact source fingerprint of their linked step, and the provider diagnosis
/// Escalation also proves its exact report input and the exact recorded pass (missing, substituted, extra, reordered or wrong-pass
/// members all fail). The same proof gates the coordinator's classification (attention) and the later status disclosure (no final
/// references, immutable state and outcomes untouched). Intact positive cases still prove every final.
/// </summary>
public sealed class LocalExecutionFinalEvidenceSourceTests : IAsyncLifetime
{
    public static IEnumerable<object[]> DiagnosisBreaks() => new[]
    {
        "input_removed", "input_substituted", "input_added", "evidence_removed", "evidence_substituted", "evidence_added", "wrong_fingerprint",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> DiagnosisClassificationBreaks() => new[]
    {
        "input_removed", "input_substituted", "evidence_removed", "wrong_fingerprint",
    }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private static LocalExecutionStepScript EscalatingScript(int pass = 1) =>
        new LocalExecutionStepScript().Verify(pass, 0, VerificationFailed).On(Step.VerificationDiagnosis, DiagnosisEscalated);

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan, Guid SequenceId)> ArmedAsync(int recipeCount = 1)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, plan, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    private async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private async Task BreakDiagnosisAsync(string kind, LocalExecutionSequenceTestScene.SeededPlan plan, Guid diagnosisId)
    {
        switch (kind)
        {
            case "input_removed":
                await SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0}", diagnosisId);
                break;
            case "input_substituted":
                await SqlAsync("UPDATE attempt_input_messages SET CollaborationMessageId = {0} WHERE AttemptId = {1}", plan.ProposalId, diagnosisId);
                break;
            case "input_added":
                await SqlAsync(
                    "INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES ({0}, {1}, {2}, 9)",
                    Guid.NewGuid(), diagnosisId, plan.ProposalId);
                break;
            case "evidence_removed":
                await SqlAsync("DELETE FROM attempt_verification_evidence WHERE AttemptId = {0}", diagnosisId);
                break;
            case "evidence_substituted":
                await SqlAsync("UPDATE attempt_verification_evidence SET VerificationExecutionId = {0} WHERE AttemptId = {1}", Guid.NewGuid(), diagnosisId);
                break;
            case "evidence_added":
                await SqlAsync(
                    "INSERT INTO attempt_verification_evidence (Id, AttemptId, VerificationCommandId, VerificationExecutionId, Sequence, SnapshotSha256) "
                    + "SELECT {0}, AttemptId, {3}, {1}, 9, SnapshotSha256 FROM attempt_verification_evidence WHERE AttemptId = {2}",
                    Guid.NewGuid(), Guid.NewGuid(), diagnosisId, Guid.NewGuid());
                break;
            case "wrong_fingerprint":
                await SqlAsync("UPDATE attempts SET AgentCheckpointFingerprintSha256 = {0} WHERE Id = {1}", new string('e', 64), diagnosisId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    // ---- approval source fingerprint --------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_approval_whose_review_source_fingerprint_differs_from_its_step_discloses_no_final_and_changes_nothing()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var ended = await scene.DriveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);
        var reviewId = ended.FinalReviewAttemptId!.Value;
        var outcomeBefore = (await scene.ReadAttemptAsync(reviewId)).AgentOutcome;

        await SqlAsync("UPDATE attempts SET AgentCheckpointFingerprintSha256 = {0} WHERE Id = {1}", new string('e', 64), reviewId);

        var status = await scene.ReadStatusAsync();
        Assert.Null(status.Sequence!.Final);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, status.State);
        Assert.Equal(ended.Revision, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(outcomeBefore, (await scene.ReadAttemptAsync(reviewId)).AgentOutcome);
    }

    [Fact]
    public async Task A_review_whose_source_fingerprint_differs_from_its_step_ends_in_attention_at_observation()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveUntilAsync(sequenceId, Step.CodeReview);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.CompleteClaimedAsync(ticket);
        await SqlAsync("UPDATE attempts SET AgentCheckpointFingerprintSha256 = {0} WHERE Id = {1}", new string('e', 64), ticket.ReservedOperationId);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.OperationOutcomeInvalid, sequence.ReasonCode);
        Assert.Null(sequence.FinalReviewAttemptId);
        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    // ---- provider diagnosis escalation ------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_intact_two_recipe_diagnosis_escalation_in_a_later_pass_still_proves_its_final()
    {
        var (scene, _, sequenceId) = await ArmedAsync(recipeCount: 2);
        var script = EscalatingScript(pass: 2).On(Step.CodeReview, 1, ReviewChangesRequested);

        var ended = await scene.DriveAsync(sequenceId, script);

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        var final = (await scene.ReadStatusAsync()).Sequence!.Final;
        Assert.NotNull(final);
        Assert.Equal(ended.FinalEscalationMessageId, final.EscalationMessageId);
    }

    [Theory]
    [MemberData(nameof(DiagnosisBreaks))]
    public async Task A_diagnosis_escalation_with_a_broken_input_or_pass_discloses_no_final_and_changes_nothing(string kind)
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var ended = await scene.DriveAsync(sequenceId, EscalatingScript());
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);
        var diagnosisId = (await scene.ReadStepsAsync()).Single(step => step.Kind == Step.VerificationDiagnosis).OperationId!.Value;
        var outcomeBefore = (await scene.ReadAttemptAsync(diagnosisId)).AgentOutcome;

        await BreakDiagnosisAsync(kind, plan, diagnosisId);

        var status = await scene.ReadStatusAsync();
        Assert.Null(status.Sequence!.Final);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, status.State);
        Assert.Equal(ended.Revision, (await scene.ReadSequenceAsync()).Revision);
        Assert.Equal(outcomeBefore, (await scene.ReadAttemptAsync(diagnosisId)).AgentOutcome);
    }

    [Theory]
    [MemberData(nameof(DiagnosisClassificationBreaks))]
    public async Task A_diagnosis_escalation_with_a_broken_input_or_pass_ends_in_attention_at_observation(string kind)
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var script = EscalatingScript();
        var ticket = await scene.ReserveUntilAsync(sequenceId, Step.VerificationDiagnosis, script);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.CompleteClaimedAsync(ticket, script);
        await BreakDiagnosisAsync(kind, plan, ticket.ReservedOperationId);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.OperationOutcomeInvalid, sequence.ReasonCode);
        Assert.Null(sequence.FinalEscalationMessageId);
        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    [Fact]
    public async Task A_two_recipe_diagnosis_whose_evidence_is_reordered_discloses_no_final()
    {
        var (scene, _, sequenceId) = await ArmedAsync(recipeCount: 2);
        var ended = await scene.DriveAsync(sequenceId, EscalatingScript());
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);
        var diagnosisId = (await scene.ReadStepsAsync()).Single(step => step.Kind == Step.VerificationDiagnosis).OperationId!.Value;

        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 100 WHERE AttemptId = {0} AND Sequence = 0", diagnosisId);
        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 0 WHERE AttemptId = {0} AND Sequence = 1", diagnosisId);
        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 1 WHERE AttemptId = {0} AND Sequence = 100", diagnosisId);

        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    [Fact]
    public async Task A_second_pass_diagnosis_whose_evidence_is_the_first_pass_discloses_no_final()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var script = EscalatingScript(pass: 2).On(Step.CodeReview, 1, ReviewChangesRequested);
        var ended = await scene.DriveAsync(sequenceId, script);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);
        var steps = await scene.ReadStepsAsync();
        var firstPass = steps.Single(step => step.Kind == Step.Verification && step.PassNumber == 1).OperationId!.Value;
        var diagnosisId = steps.Single(step => step.Kind == Step.VerificationDiagnosis).OperationId!.Value;

        await SqlAsync("UPDATE attempt_verification_evidence SET VerificationExecutionId = {0} WHERE AttemptId = {1}", firstPass, diagnosisId);

        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }
}
