using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The classifier and progression of a driven cycle (ADR-0060), through the production claims, dispatch markers and result recorders
/// over real SQLite: zero-correction approval, both correction origins, mixed origins sharing the cap of two, exhaustion with its one
/// fixed host escalation, the preserved provider diagnosis escalation, every NeedsAttention branch and the step, pass and Agent claim
/// bounds. Nothing is skipped, retried or selected by "latest".
/// </summary>
public sealed class LocalExecutionSequenceCycleTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan)> NewSceneAsync(
        int recipeCount = 2, int maximumReviewCorrectionAttempts = 2)
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(
            fixture, recipeCount, maximumReviewCorrectionAttempts: maximumReviewCorrectionAttempts);
        return (scene, await scene.AddAcceptedRootPlanAsync());
    }

    private static async Task<Guid> ReportOfAsync(LocalExecutionSequenceTestScene scene, Guid attemptId) =>
        (await scene.ReadMessagesAsync(CollaborationMessageType.ExecutionReport)).Single(message => message.AttemptId == attemptId).Id;

    private static async Task<List<LocalExecutionSequenceStep>> StepsOfAsync(LocalExecutionSequenceTestScene scene, Step kind) =>
        (await scene.ReadStepsAsync()).Where(step => step.Kind == kind).ToList();

    // ---- zero-correction approval ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_zero_correction_cycle_ends_ready_for_human_approval_with_the_exact_final_references()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.ReviewApproved, sequence.ReasonCode);
        Assert.Equal(
            [(Step.Implementation, 0, (int?)null), (Step.Verification, 1, 0), (Step.Verification, 1, 1), (Step.CodeReview, 1, null)],
            await scene.TraceAsync());

        var steps = await scene.ReadStepsAsync();
        var implementation = steps[0].OperationId!.Value;
        var review = steps[3].OperationId!.Value;
        var report = await ReportOfAsync(scene, implementation);
        Assert.Equal(report, sequence.FinalReportMessageId);
        Assert.Equal(review, sequence.FinalReviewAttemptId);
        Assert.Null(sequence.FinalEscalationMessageId);
        Assert.Equal(steps[3].ReportMessageId, report);
        Assert.All(steps.Take(3), step => Assert.Equal(LocalExecutionSequenceStepState.Concluded, step.State));

        // The review consumed exactly the report and this pass's complete ordered set, never a latest one.
        Assert.Equal([report], await scene.ReadInputsAsync(review));
        var executions = await scene.ReadExecutionsAsync();
        Assert.Equal(steps.Where(step => step.Kind == Step.Verification).Select(step => step.OperationId!.Value), executions.Select(e => e.Id));
        var resultCheckpoint = (await scene.ReadAttemptAsync(implementation)).AgentResultGitCheckpointId;
        Assert.All(executions, execution => Assert.Equal(resultCheckpoint, execution.GitCheckpointId));
        Assert.Equal(2, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract is AgentResponseContract.ImplementationReport
            or AgentResponseContract.ImplementationReview));

        // The sequence is evidence awaiting Human approval: not a completed run, no commit, no extra correction authority.
        Assert.Equal(RunLifecycle.Running, (await scene.ReadRunAsync()).Lifecycle);
        Assert.Equal(1, await scene.CountEventsAsync(RunEventType.LocalExecutionSequenceEnded));
        Assert.Empty(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
    }

    [Fact]
    public async Task The_status_of_an_approved_cycle_proves_its_final_references_from_the_linked_rows()
    {
        var (scene, plan) = await NewSceneAsync();
        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, status.State);
        Assert.Equal(sequence.FinalReportMessageId, status.Sequence!.Final!.ReportMessageId);
        Assert.Equal(sequence.FinalReviewAttemptId, status.Sequence.Final.ReviewAttemptId);
        Assert.Null(status.Sequence.Final.EscalationMessageId);
        Assert.Equal("review_and_decide_on_human_approval_separately", status.Sequence.NextActionCode);
    }

    // ---- review-origin correction ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_review_origin_correction_consumes_the_exact_report_and_every_finding_then_reverifies_the_new_checkpoint()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var script = new LocalExecutionStepScript { Findings = 3 }.On(Step.CodeReview, 1, ReviewChangesRequested);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(
            [
                (Step.Implementation, 0, (int?)null), (Step.Verification, 1, 0), (Step.Verification, 1, 1), (Step.CodeReview, 1, null),
                (Step.ReviewCorrection, 1, null), (Step.Verification, 2, 0), (Step.Verification, 2, 1), (Step.CodeReview, 2, null),
            ],
            await scene.TraceAsync());
        var steps = await scene.ReadStepsAsync();
        var firstReport = await ReportOfAsync(scene, steps[0].OperationId!.Value);
        var firstReview = steps[3].OperationId!.Value;
        var correction = steps[4];
        var findings = (await scene.ReadMessagesAsync(CollaborationMessageType.ReviewFinding)).Where(m => m.AttemptId == firstReview).ToList();
        Assert.Equal(3, findings.Count);

        // The correction consumed the preceding exact report and the WHOLE finding set, in protocol order.
        Assert.Equal(firstReview, correction.SubjectAttemptId);
        Assert.Equal(firstReport, correction.ReportMessageId);
        Assert.Equal([firstReport, .. findings.Select(finding => finding.Id)], await scene.ReadInputsAsync(correction.OperationId!.Value));

        // Pass two runs against the exact new result checkpoint of the correction, and the approval answers the correction's report.
        var correctionAttempt = await scene.ReadAttemptAsync(correction.OperationId!.Value);
        Assert.Equal(AgentOutcome.CorrectionApplied, correctionAttempt.AgentOutcome);
        Assert.Equal(correctionAttempt.AgentResultGitCheckpointId, steps[5].SourceCheckpointId);
        Assert.Equal(correctionAttempt.AgentResultGitCheckpointId, steps[7].SourceCheckpointId);
        var secondReport = await ReportOfAsync(scene, correction.OperationId!.Value);
        Assert.Equal(secondReport, steps[7].ReportMessageId);
        Assert.Equal(secondReport, sequence.FinalReportMessageId);
        Assert.Equal(steps[7].OperationId, sequence.FinalReviewAttemptId);
        Assert.Equal(1, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
    }

    [Fact]
    public async Task The_pass_membership_is_the_exact_recorded_set_and_a_stale_execution_of_an_older_pass_never_substitutes()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, 1, ReviewChangesRequested);

        await scene.ArmAndDriveAsync(plan.ProposalId, script);

        var steps = await scene.ReadStepsAsync();
        var pass1 = steps.Where(step => step.Kind == Step.Verification && step.PassNumber == 1).Select(step => step.OperationId!.Value).ToList();
        var pass2 = steps.Where(step => step.Kind == Step.Verification && step.PassNumber == 2).Select(step => step.OperationId!.Value).ToList();
        Assert.Empty(pass1.Intersect(pass2));
        await using var context = fixture.CreateContext();
        foreach (var (reviewStep, expected) in new[] { (steps.First(s => s.Kind == Step.CodeReview && s.PassNumber == 1), pass1), (steps.First(s => s.Kind == Step.CodeReview && s.PassNumber == 2), pass2) })
        {
            var evidence = await context.AttemptVerificationEvidence.AsNoTracking()
                .Where(row => row.AttemptId == reviewStep.OperationId).OrderBy(row => row.Sequence).ToListAsync();
            Assert.Equal(expected, evidence.Select(row => row.VerificationExecutionId));
        }
    }

    // ---- diagnosis-origin correction -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_recipe_finishes_the_whole_pass_then_a_diagnosis_of_that_exact_pass_selects_a_diagnosis_correction()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 3);
        var script = new LocalExecutionStepScript { Findings = 2 }.Verify(1, 0, VerificationFailed);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(
            [
                (Step.Implementation, 0, (int?)null), (Step.Verification, 1, 0), (Step.Verification, 1, 1), (Step.Verification, 1, 2),
                (Step.VerificationDiagnosis, 1, null), (Step.DiagnosisCorrection, 1, null),
                (Step.Verification, 2, 0), (Step.Verification, 2, 1), (Step.Verification, 2, 2), (Step.CodeReview, 2, null),
            ],
            await scene.TraceAsync());
        var steps = await scene.ReadStepsAsync();
        var diagnosis = steps.Single(step => step.Kind == Step.VerificationDiagnosis);
        var pass1 = steps.Where(step => step.Kind == Step.Verification && step.PassNumber == 1).Select(step => step.OperationId!.Value).ToList();

        // The diagnosis claimed exactly the complete, ordered pass (including the recipes after the failed one).
        await using var context = fixture.CreateContext();
        var snapshot = await context.AttemptVerificationEvidence.AsNoTracking()
            .Where(row => row.AttemptId == diagnosis.OperationId).OrderBy(row => row.Sequence).ToListAsync();
        Assert.Equal(pass1, snapshot.Select(row => row.VerificationExecutionId));
        Assert.Equal((await scene.ReadAttemptAsync(diagnosis.OperationId!.Value)).AgentOutcome, AgentOutcome.DiagnosisFindingsRecorded);

        var correction = steps.Single(step => step.Kind == Step.DiagnosisCorrection);
        Assert.Equal(diagnosis.OperationId, correction.SubjectAttemptId);
        var findings = (await scene.ReadMessagesAsync(CollaborationMessageType.ReviewFinding)).Where(m => m.AttemptId == diagnosis.OperationId).ToList();
        Assert.Equal(2, findings.Count);
        Assert.Equal(
            [steps[0].OperationId is { } first ? await ReportOfAsync(scene, first) : Guid.Empty, .. findings.Select(f => f.Id)],
            await scene.ReadInputsAsync(correction.OperationId!.Value));
        Assert.Equal(await ReportOfAsync(scene, correction.OperationId!.Value), sequence.FinalReportMessageId);
    }

    [Fact]
    public async Task A_provider_diagnosis_escalation_is_preserved_as_the_end_without_a_host_message_or_a_correction()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var script = new LocalExecutionStepScript().Verify(1, 1, VerificationFailed).On(Step.VerificationDiagnosis, DiagnosisEscalated);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.DiagnosisEscalated, sequence.ReasonCode);
        var escalation = Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
        Assert.Equal(escalation.Id, sequence.FinalEscalationMessageId);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, escalation.Provenance);
        var steps = await scene.ReadStepsAsync();
        Assert.Equal(steps.Single(step => step.Kind == Step.VerificationDiagnosis).OperationId, escalation.AttemptId);
        Assert.Equal(sequence.FinalReportMessageId, escalation.InReplyToMessageId);
        Assert.Null(sequence.FinalReviewAttemptId);
        Assert.DoesNotContain(steps, step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind));
    }

    // ---- mixed origins sharing the cap -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Mixed_origins_share_the_cap_of_two_and_the_cycle_reaches_its_exact_step_pass_and_claim_bounds()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript()
            .Verify(1, 0, VerificationFailed)
            .On(Step.CodeReview, 2, ReviewChangesRequested);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
        Assert.Equal(
            [
                (Step.Implementation, 0, (int?)null), (Step.Verification, 1, 0), (Step.VerificationDiagnosis, 1, null),
                (Step.DiagnosisCorrection, 1, null), (Step.Verification, 2, 0), (Step.CodeReview, 2, null),
                (Step.ReviewCorrection, 2, null), (Step.Verification, 3, 0), (Step.CodeReview, 3, null),
            ],
            await scene.TraceAsync());
        var steps = await scene.ReadStepsAsync();
        // 3 * recipeCount + 6 steps, three passes and six Agent claims: the largest cycle there can be.
        Assert.Equal(LocalExecutionSequencePolicy.MaximumSteps(1), steps.Count);
        Assert.Equal(LocalExecutionSequencePolicy.MaximumPasses, steps.Max(step => step.PassNumber));
        Assert.Equal(LocalExecutionSequencePolicy.MaximumAgentClaims, steps.Count(step => step.IsAgent));
        // The ordinary shared allowance counted both origins' ReviewCorrection-contract attempts.
        Assert.Equal(2, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        var status = await scene.ReadStatusAsync();
        Assert.Equal(2, status.Sequence!.CorrectionsClaimed);
        Assert.Equal(3, status.Sequence.PassesStarted);
        Assert.Equal(6, status.Sequence.AgentClaims);
    }

    // ---- exhaustion ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Exhaustion_with_unresolved_findings_records_exactly_one_fixed_host_escalation_replying_to_the_current_report()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AllowanceReached, sequence.ReasonCode);
        var steps = await scene.ReadStepsAsync();
        Assert.Equal(2, steps.Count(step => step.Kind == Step.ReviewCorrection));
        Assert.Equal(Step.CodeReview, steps[^1].Kind);
        Assert.Equal(3, steps[^1].PassNumber);

        var escalation = Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
        Assert.Equal(escalation.Id, sequence.FinalEscalationMessageId);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, escalation.Provenance);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), escalation.Actor);
        Assert.Equal(ParticipantIdentity.ForHuman(), escalation.Recipient);
        Assert.Equal("Local execution cycle requires a human decision.", escalation.Summary);
        var lastCorrection = steps.Last(step => step.Kind == Step.ReviewCorrection);
        var currentReport = await ReportOfAsync(scene, lastCorrection.OperationId!.Value);
        Assert.Equal(currentReport, escalation.InReplyToMessageId);
        Assert.Equal(currentReport, sequence.FinalReportMessageId);
        Assert.Null(escalation.AttemptId);
        using var content = JsonDocument.Parse(escalation.StructuredContentJson);
        Assert.Equal(
            [
                ("unresolvedDecision", "The authorized local execution cycle has unresolved findings."),
                ("options", "Inspect the recorded evidence and decide on an explicit manual next action. This cycle cannot continue."),
                ("consequences", "No further operation is authorized by this sequence; the run remains awaiting human action."),
                ("evidence", "The authorized automatic correction allowance was reached."),
                ("recommendedChoice", "Inspect the evidence before requesting any manual continuation."),
            ],
            content.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.GetString()!)).ToArray());

        // Nothing past the cap was claimed or consumed, and no ordinary extra-correction grant exists or was read.
        Assert.Equal(2, (await scene.ReadAttemptsAsync()).Count(attempt => attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection));
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.ReviewCorrectionEscalations.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ReviewCorrectionAuthorizations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_second_observation_of_the_ended_exhausted_sequence_records_no_second_escalation()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested);
        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        var again = await scene.ObserveAsync(sequence.Id);

        Assert.False(again.Value.Changed);
        Assert.Null(again.Value.ReservedStep);
        Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
    }

    [Fact]
    public async Task A_smaller_remaining_allowance_exhausts_after_one_shared_correction()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1, maximumReviewCorrectionAttempts: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceReason.AllowanceReached, sequence.ReasonCode);
        Assert.Equal(1, (await scene.ReadStepsAsync()).Count(step => step.Kind == Step.ReviewCorrection));
        Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
    }

    [Fact]
    public async Task Exhaustion_at_a_failing_diagnosis_also_records_the_one_fixed_host_escalation()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1, maximumReviewCorrectionAttempts: 1);
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(2, 0, VerificationFailed);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AllowanceReached, sequence.ReasonCode);
        Assert.Equal(
            [Step.Implementation, Step.Verification, Step.VerificationDiagnosis, Step.DiagnosisCorrection, Step.Verification, Step.VerificationDiagnosis],
            (await scene.ReadStepsAsync()).Select(step => step.Kind));
        var escalation = Assert.Single(await scene.ReadMessagesAsync(CollaborationMessageType.Escalation));
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, escalation.Provenance);
    }

    // ---- NeedsAttention branches -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ProviderFailedNoExit, LocalExecutionSequenceReason.ExitEvidenceMissing)]
    [InlineData(ProviderFailedWithExit, LocalExecutionSequenceReason.OperationFailed)]
    [InlineData(InvalidOutput, LocalExecutionSequenceReason.OperationFailed)]
    public async Task A_failed_implementation_ends_in_attention_without_a_retry_or_a_repair(ScriptedResultKind result, string reason)
    {
        var (scene, plan) = await NewSceneAsync();
        var script = new LocalExecutionStepScript().On(Step.Implementation, result);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        Assert.Single(await scene.ReadStepsAsync());
        Assert.Single(await scene.ReadAttemptsAsync(), attempt => attempt.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Empty(await scene.ReadExecutionsAsync());
    }

    [Fact]
    public async Task A_timed_out_or_exitless_operation_stays_an_owner_and_its_fence_is_not_released()
    {
        var (scene, plan) = await NewSceneAsync();
        var script = new LocalExecutionStepScript().On(Step.Implementation, ProviderFailedNoExit);
        await scene.ArmAndDriveAsync(plan.ProposalId, script);

        var status = await scene.ReadStatusAsync();

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, status.State);
        Assert.Equal(Application.Features.Runs.Queries.GetLocalExecutionSequence.LocalExecutionSequenceManualFence.Blocked, status.ManualFence);
        Assert.True(status.Sequence!.AwaitingReconciliation);
        Assert.True(Assert.Single(status.Sequence.Steps).Quarantined);
        Assert.Equal("no_supported_recovery_action_in_this_version", status.Sequence.NextActionCode);
    }

    [Fact]
    public async Task A_timed_out_verification_ends_in_attention_not_in_diagnosis()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).Verify(1, 1, VerificationTimedOut);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.ExitEvidenceMissing, sequence.ReasonCode);
        Assert.DoesNotContain(await scene.ReadStepsAsync(), step => step.Kind is Step.VerificationDiagnosis or Step.CodeReview);
    }

    [Fact]
    public async Task A_source_changed_verification_is_not_diagnosable_and_ends_in_attention()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var script = new LocalExecutionStepScript().Verify(1, 1, VerificationSourceChanged);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.OperationOutcomeInvalid, sequence.ReasonCode);
        Assert.DoesNotContain(await scene.ReadStepsAsync(), step => step.Kind is Step.VerificationDiagnosis or Step.CodeReview);
    }

    [Theory]
    [InlineData(Step.CodeReview, ProviderFailedWithExit, LocalExecutionSequenceReason.OperationFailed)]
    [InlineData(Step.CodeReview, ProviderFailedNoExit, LocalExecutionSequenceReason.ExitEvidenceMissing)]
    [InlineData(Step.CodeReview, InvalidOutput, LocalExecutionSequenceReason.OperationFailed)]
    public async Task A_failed_review_ends_in_attention_without_a_correction(Step kind, ScriptedResultKind result, string reason)
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(kind, result);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        Assert.DoesNotContain(await scene.ReadStepsAsync(), step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind));
    }

    [Theory]
    [InlineData(ProviderFailedWithExit, LocalExecutionSequenceReason.OperationFailed)]
    [InlineData(ProviderFailedNoExit, LocalExecutionSequenceReason.ExitEvidenceMissing)]
    [InlineData(InvalidOutput, LocalExecutionSequenceReason.OperationFailed)]
    public async Task A_failed_diagnosis_ends_in_attention_without_a_correction(ScriptedResultKind result, string reason)
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.VerificationDiagnosis, result);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        Assert.DoesNotContain(await scene.ReadStepsAsync(), step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind));
    }

    [Theory]
    [InlineData(ProviderFailedWithExit, LocalExecutionSequenceReason.OperationFailed)]
    [InlineData(ProviderFailedNoExit, LocalExecutionSequenceReason.ExitEvidenceMissing)]
    [InlineData(InvalidOutput, LocalExecutionSequenceReason.OperationFailed)]
    public async Task A_failed_correction_ends_in_attention_without_a_second_correction_or_reverification(ScriptedResultKind result, string reason)
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, 1, ReviewChangesRequested).On(Step.ReviewCorrection, result);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        var steps = await scene.ReadStepsAsync();
        Assert.Equal(Step.ReviewCorrection, steps[^1].Kind);
        Assert.Single(steps, step => step.Kind == Step.ReviewCorrection);
    }

    [Fact]
    public async Task A_correction_that_produced_no_change_ends_in_attention()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 1);
        var script = new LocalExecutionStepScript().On(Step.CodeReview, 1, ReviewChangesRequested).On(Step.ReviewCorrection, InvalidOutput);

        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, script);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(1, (await scene.ReadStepsAsync()).Count(step => step.Kind == Step.ReviewCorrection));
    }

    [Fact]
    public async Task A_new_unrelated_checkpoint_between_steps_ends_the_cycle_instead_of_becoming_its_source()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var armed = await scene.ArmAsync(plan.ProposalId);
        // Implementation, then pass one's first recipe.
        await scene.StepAsync(armed.SequenceId);
        await scene.StepAsync(armed.SequenceId);
        // A manual capture is withheld by the database guard while live; with the guard lifted the Application read must still refuse it.
        await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_checkpoints");
        await using (var context = fixture.CreateContext())
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.WorkspaceId);
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), LocalExecutionSequenceTestScene.Start,
                LocalExecutionSequenceTestScene.Head, new string('9', 64), []));
            await context.SaveChangesAsync();
        }

        var driven = await scene.StepAsync(armed.SequenceId);

        Assert.Null(driven.Ticket);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AuthorityChanged, sequence.ReasonCode);
        Assert.Equal(2, (await scene.ReadStepsAsync()).Count);
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("setting")]
    public async Task A_recipe_or_setting_change_between_steps_ends_the_cycle_with_authority_changed(string drift)
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var armed = await scene.ArmAsync(plan.ProposalId);
        await scene.StepAsync(armed.SequenceId);
        await scene.StepAsync(armed.SequenceId);
        if (drift == "recipe")
        {
            await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_verification_commands_update");
            await scene.EditRecipeAsync((await scene.ReadRecipesAsync())[1].Id, timeoutSeconds: 31);
        }
        else
        {
            await scene.ExecuteSqlAsync("DROP TRIGGER trg_local_execution_guard_run_settings");
            await using var context = fixture.CreateContext();
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
            run.SetRequestedClaudeMaxTurns(9);
            await context.SaveChangesAsync();
        }

        var driven = await scene.StepAsync(armed.SequenceId);

        Assert.Null(driven.Ticket);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(LocalExecutionSequenceReason.AuthorityChanged, sequence.ReasonCode);
    }

    [Fact]
    public async Task An_unrecorded_verification_execution_never_substitutes_for_a_missing_member_of_the_pass()
    {
        var (scene, plan) = await NewSceneAsync(recipeCount: 2);
        var armed = await scene.ArmAsync(plan.ProposalId);
        await scene.StepAsync(armed.SequenceId);
        var first = await scene.StepAsync(armed.SequenceId);
        Assert.Equal(Step.Verification, first.Ticket!.Kind);

        // The recorded pass has a single member; its step is Concluded by the next observation, which only reserves the second recipe.
        var steps = await scene.ReadStepsAsync();
        Assert.Equal(2, steps.Count);
        var pass = await ReadPassAsync(scene, 1);
        Assert.Equal([(0, steps[1].OperationId!.Value)], pass);
    }

    private async Task<IReadOnlyList<(int RecipeOrdinal, Guid ExecutionId)>> ReadPassAsync(LocalExecutionSequenceTestScene scene, int pass)
    {
        await using var context = fixture.CreateContext();
        var sequence = await scene.ReadSequenceAsync();
        return await LocalExecutionStepAuthority.PassExecutionsAsync(context, sequence.Id, pass, CancellationToken.None);
    }
}
