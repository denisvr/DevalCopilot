using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The final references a sequence advertises are proved from the recorded evidence itself (ADR-0060), never from a step row or an
/// attempt outcome alone: the Approved review, the provider diagnosis Escalation and the host exhaustion Escalation each fail closed (a null
/// Final, state and outcomes unchanged) when any part of their evidence is broken after completion, and the coordinator refuses to classify
/// a missing or malformed approval at observation time. The positive cases still prove all three finals.
/// </summary>
public sealed class LocalExecutionFinalEvidenceTests : IAsyncLifetime
{
    public static IEnumerable<object[]> ApprovalBreaks() => new[]
    {
        "approval_removed", "second_approval", "wrong_run", "wrong_actor", "host_provenance", "wrong_reply", "input_removed",
        "input_added", "not_clean_exit", "wrong_workspace", "wrong_checkpoint", "evidence_removed",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> ClassificationBreaks() => new[]
    {
        "approval_removed", "wrong_actor", "host_provenance", "wrong_reply", "evidence_removed", "input_removed",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> ProviderEscalationBreaks() => new[]
    {
        "removed", "wrong_reply", "host_provenance", "extra_message", "wrong_run",
    }.Select(name => new object[] { name });

    public static IEnumerable<object[]> HostEscalationBreaks() => new[]
    {
        "removed", "summary_altered", "wrong_actor", "provider_provenance", "wrong_reply", "report_not_a_step_report",
    }.Select(name => new object[] { name });

    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan, Guid SequenceId)> ArmedAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        return (scene, plan, (await scene.ArmAsync(plan.ProposalId)).SequenceId);
    }

    /// <summary>Runs one statement on a connection with foreign keys off, so a deliberately broken row can be stored.</summary>
    private async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private async Task BreakApprovalAsync(
        string kind, LocalExecutionSequenceTestScene scene, LocalExecutionSequenceTestScene.SeededPlan plan, Guid reviewId, LocalExecutionSequenceTestScene other)
    {
        var approval = (await scene.ReadMessagesAsync(CollaborationMessageType.ReviewApproval)).Single(message => message.AttemptId == reviewId);
        switch (kind)
        {
            case "approval_removed":
                await SqlAsync("DELETE FROM collaboration_messages WHERE Id = {0}", approval.Id);
                break;
            case "second_approval":
                await SqlAsync("UPDATE collaboration_messages SET Type = 'ReviewApproval', AttemptId = {0} WHERE Id = {1}", reviewId, plan.AcceptanceId!.Value);
                break;
            case "wrong_run":
                await SqlAsync("UPDATE collaboration_messages SET RunId = {0} WHERE Id = {1}", other.RunId, approval.Id);
                break;
            case "wrong_actor":
                await SqlAsync("UPDATE collaboration_messages SET ActorKind = 'Human', ActorAgentRole = NULL, ActorAgentProvider = NULL WHERE Id = {0}", approval.Id);
                break;
            case "host_provenance":
                await SqlAsync("UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {0}", approval.Id);
                break;
            case "wrong_reply":
                await SqlAsync("UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", plan.ProposalId, approval.Id);
                break;
            case "input_removed":
                await SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0}", reviewId);
                break;
            case "input_added":
                await SqlAsync(
                    "INSERT INTO attempt_input_messages (Id, AttemptId, CollaborationMessageId, Sequence) VALUES ({0}, {1}, {2}, 9)",
                    Guid.NewGuid(), reviewId, plan.ProposalId);
                break;
            case "not_clean_exit":
                await SqlAsync("UPDATE attempts SET AgentProcessExitCode = 3 WHERE Id = {0}", reviewId);
                break;
            case "wrong_workspace":
                await SqlAsync("UPDATE attempts SET AgentGitWorkspaceId = {0} WHERE Id = {1}", other.WorkspaceId, reviewId);
                break;
            case "wrong_checkpoint":
                await SqlAsync("UPDATE attempts SET AgentGitCheckpointId = {0} WHERE Id = {1}", other.CheckpointId, reviewId);
                break;
            case "evidence_removed":
                await SqlAsync("DELETE FROM attempt_verification_evidence WHERE AttemptId = {0}", reviewId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    // ---- approved review ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_intact_approval_proves_the_final_references()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var ended = await scene.DriveAsync(sequenceId);

        var final = (await scene.ReadStatusAsync()).Sequence!.Final;

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        Assert.NotNull(final);
        Assert.Equal(ended.FinalReviewAttemptId, final.ReviewAttemptId);
        Assert.Equal(ended.FinalReportMessageId, final.ReportMessageId);
    }

    [Theory]
    [MemberData(nameof(ApprovalBreaks))]
    public async Task A_broken_approval_evidence_discloses_no_final_and_changes_nothing(string kind)
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var ended = await scene.DriveAsync(sequenceId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        var reviewId = ended.FinalReviewAttemptId!.Value;
        var outcomeBefore = (await scene.ReadAttemptAsync(reviewId)).AgentOutcome;

        await BreakApprovalAsync(kind, scene, plan, reviewId, other);

        var status = await scene.ReadStatusAsync();
        Assert.Null(status.Sequence!.Final);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, status.State);
        var after = await scene.ReadSequenceAsync();
        Assert.Equal(ended.Revision, after.Revision);
        Assert.Equal(ended.FinalReviewAttemptId, after.FinalReviewAttemptId);
        Assert.Equal(outcomeBefore, (await scene.ReadAttemptAsync(reviewId)).AgentOutcome);
    }

    [Fact]
    public async Task An_approval_whose_evidence_is_another_passs_execution_discloses_no_final()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var script = new LocalExecutionStepScript().On(Step.CodeReview, 1, ReviewChangesRequested);
        var ended = await scene.DriveAsync(sequenceId, script);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, ended.State);
        var firstPass = (await scene.ReadStepsAsync()).First(step => step.Kind == Step.Verification && step.PassNumber == 1).OperationId!.Value;
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);

        await SqlAsync(
            "UPDATE attempt_verification_evidence SET VerificationExecutionId = {0} WHERE AttemptId = {1}", firstPass, ended.FinalReviewAttemptId!.Value);

        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    [Fact]
    public async Task A_second_pass_with_two_recipes_that_reorders_its_evidence_discloses_no_final()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var ended = await scene.ArmAndDriveAsync(plan.ProposalId);
        var reviewId = ended.FinalReviewAttemptId!.Value;
        Assert.NotNull((await scene.ReadStatusAsync()).Sequence!.Final);

        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 100 WHERE AttemptId = {0} AND Sequence = 0", reviewId);
        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 0 WHERE AttemptId = {0} AND Sequence = 1", reviewId);
        await SqlAsync("UPDATE attempt_verification_evidence SET Sequence = 1 WHERE AttemptId = {0} AND Sequence = 100", reviewId);

        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    [Fact]
    public async Task A_complete_approval_is_classified_ready_for_human_approval_at_observation()
    {
        var (scene, _, sequenceId) = await ArmedAsync();
        var ticket = await scene.ReserveUntilAsync(sequenceId, Step.CodeReview);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.CompleteClaimedAsync(ticket);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, observed.Value.State);
    }

    [Theory]
    [MemberData(nameof(ClassificationBreaks))]
    public async Task A_missing_or_malformed_approval_at_observation_ends_in_attention_not_approval(string kind)
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var ticket = await scene.ReserveUntilAsync(sequenceId, Step.CodeReview);
        Assert.True((await scene.ClaimAsync(ticket)).IsSuccess);
        await scene.CompleteClaimedAsync(ticket);
        await BreakApprovalAsync(kind, scene, plan, ticket.ReservedOperationId, other);

        var observed = await scene.ObserveAsync(sequenceId);

        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, observed.Value.State);
        var sequence = await scene.ReadSequenceAsync();
        Assert.Equal(LocalExecutionSequenceReason.OperationOutcomeInvalid, sequence.ReasonCode);
        Assert.Null(sequence.FinalReviewAttemptId);
        Assert.Null((await scene.ReadStatusAsync()).Sequence!.Final);
    }

    // ---- provider diagnosis escalation ------------------------------------------------------------------------------------------

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan, LocalExecutionSequence Ended)> EscalatedByDiagnosisAsync()
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var script = new LocalExecutionStepScript().Verify(1, 0, VerificationFailed).On(Step.VerificationDiagnosis, DiagnosisEscalated);
        var ended = await scene.DriveAsync(sequenceId, script);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        return (scene, plan, ended);
    }

    [Fact]
    public async Task An_intact_provider_escalation_proves_its_final_references()
    {
        var (scene, _, ended) = await EscalatedByDiagnosisAsync();

        var final = (await scene.ReadStatusAsync()).Sequence!.Final;

        Assert.NotNull(final);
        Assert.Equal(ended.FinalEscalationMessageId, final.EscalationMessageId);
    }

    [Theory]
    [MemberData(nameof(ProviderEscalationBreaks))]
    public async Task A_broken_provider_escalation_discloses_no_final_and_changes_nothing(string kind)
    {
        var (scene, plan, ended) = await EscalatedByDiagnosisAsync();
        var other = await LocalExecutionSequenceTestScene.CreateAsync(fixture, recipeCount: 1);
        var escalationId = ended.FinalEscalationMessageId!.Value;
        var diagnosisId = (await scene.ReadStepsAsync()).Single(step => step.Kind == Step.VerificationDiagnosis).OperationId!.Value;
        switch (kind)
        {
            case "removed":
                await SqlAsync("DELETE FROM collaboration_messages WHERE Id = {0}", escalationId);
                break;
            case "wrong_reply":
                await SqlAsync("UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", plan.ProposalId, escalationId);
                break;
            case "host_provenance":
                await SqlAsync("UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {0}", escalationId);
                break;
            case "extra_message":
                await SqlAsync("UPDATE collaboration_messages SET Type = 'Escalation', AttemptId = {0} WHERE Id = {1}", diagnosisId, plan.AcceptanceId!.Value);
                break;
            default:
                await SqlAsync("UPDATE collaboration_messages SET RunId = {0} WHERE Id = {1}", other.RunId, escalationId);
                break;
        }

        var status = await scene.ReadStatusAsync();

        Assert.Null(status.Sequence!.Final);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, status.State);
        Assert.Equal(ended.Revision, (await scene.ReadSequenceAsync()).Revision);
    }

    // ---- host exhaustion escalation ---------------------------------------------------------------------------------------------

    private async Task<(LocalExecutionSequenceTestScene Scene, LocalExecutionSequenceTestScene.SeededPlan Plan, LocalExecutionSequence Ended)> ExhaustedAsync()
    {
        var (scene, plan, sequenceId) = await ArmedAsync();
        var ended = await scene.DriveAsync(sequenceId, new LocalExecutionStepScript().On(Step.CodeReview, ReviewChangesRequested));
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, ended.State);
        return (scene, plan, ended);
    }

    [Fact]
    public async Task An_intact_host_exhaustion_escalation_proves_its_final_references()
    {
        var (scene, _, ended) = await ExhaustedAsync();

        var final = (await scene.ReadStatusAsync()).Sequence!.Final;

        Assert.NotNull(final);
        Assert.Equal(ended.FinalEscalationMessageId, final.EscalationMessageId);
        Assert.Equal(ended.FinalReportMessageId, final.ReportMessageId);
    }

    [Theory]
    [MemberData(nameof(HostEscalationBreaks))]
    public async Task A_broken_host_escalation_discloses_no_final_and_changes_nothing(string kind)
    {
        var (scene, plan, ended) = await ExhaustedAsync();
        var escalationId = ended.FinalEscalationMessageId!.Value;
        switch (kind)
        {
            case "removed":
                await SqlAsync("DELETE FROM collaboration_messages WHERE Id = {0}", escalationId);
                break;
            case "summary_altered":
                await SqlAsync("UPDATE collaboration_messages SET Summary = 'Altered summary.' WHERE Id = {0}", escalationId);
                break;
            case "wrong_actor":
                await SqlAsync("UPDATE collaboration_messages SET ActorKind = 'Human' WHERE Id = {0}", escalationId);
                break;
            case "provider_provenance":
                await SqlAsync("UPDATE collaboration_messages SET Provenance = 'ProviderObserved' WHERE Id = {0}", escalationId);
                break;
            case "wrong_reply":
                await SqlAsync("UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", plan.ProposalId, escalationId);
                break;
            default:
                // Both the message and the sequence agree on a report that is not any step's report.
                await SqlAsync("DROP TRIGGER trg_local_execution_sequence_immutable");
                await SqlAsync("UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", plan.ProposalId, escalationId);
                await SqlAsync("UPDATE local_execution_sequences SET FinalReportMessageId = {0} WHERE Id = {1}", plan.ProposalId, ended.Id);
                break;
        }

        var status = await scene.ReadStatusAsync();

        Assert.Null(status.Sequence!.Final);
        Assert.Equal(LocalExecutionSequenceState.HumanEscalation, status.State);
    }
}
