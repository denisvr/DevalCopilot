using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// A diagnosis-origin correction as a link of the Implementer report chain (ADR-0018 narrowly extends ADR-0010):
/// <c>IsValidFindingSource</c> accepts a diagnosis that recorded findings next to a changes-requested review and nothing
/// else; the record handler writes the revision responses for diagnosis-origin findings; and the ORDINARY code review of the
/// corrected report still judges the true implemented plan and still requires every enabled command's latest execution for the
/// NEW checkpoint to have Passed.
/// </summary>
public sealed class DiagnosisCorrectionChainTests : IAsyncLifetime
{
    private static readonly string CorrectedFingerprint = new('e', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private sealed record Corrected(
        DiagnosisTestScene Scene, Attempt Diagnosis, IReadOnlyList<CollaborationMessage> Findings, Guid CorrectionAttemptId);

    private async Task<Corrected> ClaimedAndDispatchedCorrectionAsync(
        PlanForm form = PlanForm.AcceptedRoot, int findingCount = 2, params Spec[] specs)
    {
        var scene = await CreateAsync(_fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed()] : specs, form);
        var seeded = await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount);
        var claim = await scene.ClaimCorrectionAsync(seeded.Attempt.Id);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        await using var db = _fixture.CreateContext();
        var dispatch = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, created.AttemptId), CancellationToken.None);
        Assert.True(dispatch.IsSuccess, dispatch.IsFailure ? Code(dispatch) : null);
        await db.SaveChangesAsync();
        return new Corrected(scene, seeded.Attempt, seeded.Messages, created.AttemptId);
    }

    private static ValidatedReviewCorrection CorrectionFor(IReadOnlyList<CollaborationMessage> findings) => ValidatedReviewCorrection.Create(
        findings.Select((finding, index) => new ValidatedRevisionResponse(finding.Id, "Applied", $"Fixed {index}.", $"Updated src/Foo{index}.cs.")).ToArray(),
        ValidatedImplementationReport.Create(
            "Correction completed.", ["src/Foo.cs"], "Applied the diagnosed fixes.", string.Empty, string.Empty, "Run the focused tests."));

    private Task<Devalente.Shared.Results.Result<RecordReviewCorrectionResultCommandResult>> RecordAsync(
        Corrected corrected, ValidatedReviewCorrection? correction = null, Guid? attemptId = null)
    {
        var db = _fixture.CreateContext();
        return new RecordReviewCorrectionResultCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordReviewCorrectionResultCommand(
                corrected.Scene.Run.Id, attemptId ?? corrected.CorrectionAttemptId, true, corrected.Scene.Implementation.ReviewCheckpoint.HeadCommitSha,
                CorrectedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [],
                correction ?? CorrectionFor(corrected.Findings), null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);
    }

    private async Task<(GitCheckpoint Checkpoint, CollaborationMessage Report)> AppliedAsync(Corrected corrected)
    {
        var recorded = await RecordAsync(corrected);
        Assert.True(recorded.IsSuccess, recorded.IsFailure ? Code(recorded) : null);
        Assert.Equal(AgentOutcome.CorrectionApplied, recorded.Value.Outcome);
        await using var read = _fixture.CreateContext();
        var checkpoint = await read.GitCheckpoints.AsNoTracking()
            .Where(c => c.WorkspaceId == corrected.Scene.Scene.Workspace.Id).OrderByDescending(c => c.CheckpointNumber).FirstAsync();
        var report = await read.CollaborationMessages.AsNoTracking()
            .SingleAsync(m => m.AttemptId == corrected.CorrectionAttemptId && m.Type == CollaborationMessageType.ExecutionReport);
        return (checkpoint, report);
    }

    private async Task<ImplementerExecutionReportEligibility.Result?> ResolveAsync(Corrected corrected, GitCheckpoint checkpoint, CollaborationMessage report)
    {
        await using var read = _fixture.CreateContext();
        return await ImplementerExecutionReportEligibility.ResolveAsync(
            read, report, corrected.Scene.Run.Id, corrected.Scene.Scene.Workspace.Id, checkpoint.Id, CancellationToken.None);
    }

    // ---- Record handler --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_record_handler_writes_one_revision_response_per_diagnosis_finding_in_order_and_the_corrected_report()
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync(findingCount: 3);

        var (checkpoint, report) = await AppliedAsync(corrected);

        await using var verify = _fixture.CreateContext();
        var responses = await verify.CollaborationMessages.Where(m => m.AttemptId == corrected.CorrectionAttemptId && m.Type == CollaborationMessageType.RevisionResponse)
            .OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal(corrected.Findings.Select(f => f.Id), responses.Select(r => r.InReplyToMessageId!.Value));
        Assert.All(responses, r => Assert.True(r.Sequence > corrected.Findings.Max(f => f.Sequence)));
        Assert.Equal(corrected.Scene.Implementation.OriginalPlan.Id, report.InReplyToMessageId);
        Assert.Equal(CorrectedFingerprint, checkpoint.FingerprintSha256);
        Assert.Equal(AgentOutcome.CorrectionApplied, (await verify.Attempts.SingleAsync(a => a.Id == corrected.CorrectionAttemptId)).AgentOutcome);
    }

    [Fact]
    public async Task Revision_response_cardinality_and_order_are_unchanged_for_diagnosis_origin_findings()
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync(findingCount: 2);
        var missing = CorrectionFor(corrected.Findings.Take(1).ToArray());
        var reordered = CorrectionFor(corrected.Findings.Reverse().ToArray());
        var extra = CorrectionFor([.. corrected.Findings, corrected.Findings[0]]);

        Assert.Equal("agent_attempts.invalid_correction_result", Code(await RecordAsync(corrected, missing)));
        Assert.Equal("agent_attempts.invalid_correction_result", Code(await RecordAsync(corrected, reordered)));
        Assert.Equal("agent_attempts.invalid_correction_result", Code(await RecordAsync(corrected, extra)));

        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.SingleAsync(a => a.Id == corrected.CorrectionAttemptId);
        Assert.Equal(AttemptStatus.Running, stored.Status);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == corrected.CorrectionAttemptId));
    }

    [Theory]
    [InlineData("diagnosis-escalated")]
    [InlineData("diagnosis-failed")]
    [InlineData("diagnosis-not-findings-contract")]
    [InlineData("diagnosis-other-checkpoint")]
    public async Task The_record_handler_rejects_findings_whose_owner_is_not_a_valid_finding_source(string tamper)
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync();
        switch (tamper)
        {
            case "diagnosis-escalated":
                await corrected.Scene.SqlAsync("UPDATE attempts SET AgentOutcome = 'DiagnosisEscalated' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            case "diagnosis-failed":
                await corrected.Scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            case "diagnosis-not-findings-contract":
                await corrected.Scene.SqlAsync(
                    "UPDATE attempts SET AgentResponseContract = 'ImplementationReview', AgentOutcome = 'ReviewApproved' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            default:
                await corrected.Scene.SqlAsync("UPDATE attempts SET AgentGitCheckpointId = {0} WHERE Id = {1}", corrected.Scene.Scene.Checkpoint.Id, corrected.Diagnosis.Id);
                break;
        }

        var result = await RecordAsync(corrected);

        Assert.True(result.IsFailure, tamper);
        Assert.Equal("agent_attempts.correction_input_invalid", Code(result));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(AttemptStatus.Running, (await verify.Attempts.SingleAsync(a => a.Id == corrected.CorrectionAttemptId)).Status);
    }

    [Fact]
    public async Task The_record_handler_still_accepts_findings_of_a_changes_requested_ordinary_review()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var review = await scene.AddOrdinaryChangesRequestedReviewAsync(findingCount: 2);
        var claim = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Attempt.Id), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        await using (var db = _fixture.CreateContext())
        {
            Assert.True((await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
                new MarkAgentAttemptDispatchedCommand(scene.Run.Id, created.AttemptId), CancellationToken.None)).IsSuccess);
            await db.SaveChangesAsync();
        }

        var corrected = new Corrected(scene, review.Attempt, review.Messages, created.AttemptId);
        var recorded = await RecordAsync(corrected);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? Code(recorded) : null);
        Assert.Equal(AgentOutcome.CorrectionApplied, recorded.Value.Outcome);
    }

    // ---- Chain resolution ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("AcceptedRoot")]
    [InlineData("FirstRevision")]
    [InlineData("AcceptedFirstRevision")]
    public async Task A_corrected_report_whose_findings_came_from_a_diagnosis_resolves_with_the_true_implemented_plan(string formName)
    {
        var form = Enum.Parse<PlanForm>(formName);
        var corrected = await ClaimedAndDispatchedCorrectionAsync(form);
        var (checkpoint, report) = await AppliedAsync(corrected);

        var chain = await ResolveAsync(corrected, checkpoint, report);

        Assert.NotNull(chain);
        Assert.Equal(corrected.Scene.Implementation.OriginalPlan.Id, chain.OriginalProposal.Id);
        Assert.Equal(corrected.Scene.Implementation.ResolvedPlan.Id, chain.ImplementedPlan.Id);
        Assert.Equal(corrected.Scene.ReportId, chain.PreviousExecutionReport!.Id);
        Assert.Equal(corrected.Findings.Select(f => f.Id), chain.OrderedFindings.Select(f => f.Id));
        Assert.Equal(corrected.Findings.Count, chain.OrderedRevisionResponses.Count);
    }

    [Theory]
    [InlineData("diagnosis-escalated")]
    [InlineData("diagnosis-failed")]
    [InlineData("diagnosis-running")]
    [InlineData("mixed-owners")]
    [InlineData("revision-response-missing")]
    [InlineData("finding-replies-elsewhere")]
    [InlineData("diagnosis-other-workspace-checkpoint")]
    public async Task A_corrected_report_over_an_invalid_finding_source_does_not_resolve(string tamper)
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync(findingCount: 2);
        var review = await corrected.Scene.AddOrdinaryChangesRequestedReviewAsync(findingCount: 1);
        var (checkpoint, report) = await AppliedAsync(corrected);
        Assert.NotNull(await ResolveAsync(corrected, checkpoint, report));
        switch (tamper)
        {
            case "diagnosis-escalated":
                await corrected.Scene.SqlAsync("UPDATE attempts SET AgentOutcome = 'DiagnosisEscalated' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            case "diagnosis-failed":
                await corrected.Scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            case "diagnosis-running":
                await corrected.Scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", corrected.Diagnosis.Id);
                break;
            case "mixed-owners":
                await corrected.Scene.SqlAsync(
                    "UPDATE attempt_input_messages SET CollaborationMessageId = {0} WHERE AttemptId = {1} AND Sequence = 2",
                    review.Messages[0].Id, corrected.CorrectionAttemptId);
                break;
            case "revision-response-missing":
                await corrected.Scene.SqlAsync("DELETE FROM collaboration_messages WHERE AttemptId = {0} AND Type = 'RevisionResponse'", corrected.CorrectionAttemptId);
                break;
            case "finding-replies-elsewhere":
                await corrected.Scene.SqlAsync(
                    "UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE Id = {1}", corrected.Scene.Implementation.ResolvedPlan.Id, corrected.Findings[0].Id);
                break;
            default:
                await corrected.Scene.SqlAsync(
                    "UPDATE attempts SET AgentGitCheckpointId = {0} WHERE Id = {1}", corrected.Scene.Scene.Checkpoint.Id, corrected.Diagnosis.Id);
                break;
        }

        Assert.Null(await ResolveAsync(corrected, checkpoint, report));
    }

    [Fact]
    public async Task IsValidFindingSource_accepts_exactly_the_two_completed_finding_sources()
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync();
        await using var read = _fixture.CreateContext();
        var diagnosis = await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == corrected.Diagnosis.Id);
        Assert.True(ImplementerExecutionReportEligibility.IsValidFindingSource(diagnosis));

        var cases = new (AgentResponseContract Contract, AgentOutcome Outcome, bool Valid)[]
        {
            (AgentResponseContract.VerificationDiagnosis, AgentOutcome.DiagnosisFindingsRecorded, true),
            (AgentResponseContract.ImplementationReview, AgentOutcome.ReviewChangesRequested, true),
            (AgentResponseContract.VerificationDiagnosis, AgentOutcome.DiagnosisEscalated, false),
            (AgentResponseContract.VerificationDiagnosis, AgentOutcome.ReviewChangesRequested, false),
            (AgentResponseContract.VerificationDiagnosis, AgentOutcome.ReviewApproved, false),
            (AgentResponseContract.VerificationDiagnosis, AgentOutcome.VerificationEvidenceChanged, false),
            (AgentResponseContract.ImplementationReview, AgentOutcome.DiagnosisFindingsRecorded, false),
            (AgentResponseContract.ImplementationReview, AgentOutcome.ReviewApproved, false),
            (AgentResponseContract.ReviewCorrection, AgentOutcome.DiagnosisFindingsRecorded, false),
        };
        foreach (var (contract, outcome, valid) in cases)
        {
            var attempt = Attempt.ClaimAgentVerificationDiagnosis(
                Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "f", Guid.NewGuid(), TimeSpan.FromMinutes(1), 1, 1, Now, null, null, 1);
            typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))!.SetValue(attempt, contract);
            typeof(Attempt).GetProperty(nameof(Attempt.AgentOutcome))!.SetValue(attempt, outcome);
            Assert.Equal(valid, ImplementerExecutionReportEligibility.IsValidFindingSource(attempt));
        }
    }

    // ---- The ordinary review of the corrected report keeps its Passed gate ----------------------------------------------

    private CreateCodeReviewAttemptCommandHandler ReviewHandler(DevalCopilotDbContext db, DiagnosisTestScene scene, DiagnosisArtifactStore? store = null) =>
        new(db, RepairEvidenceReader.Matching(CorrectedFingerprint), store ?? scene.Store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options));

    [Fact]
    public async Task The_review_of_a_corrected_report_is_refused_while_the_new_checkpoint_has_no_verification()
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync();
        var (_, report) = await AppliedAsync(corrected);
        await using var db = _fixture.CreateContext();
        var store = new DiagnosisArtifactStore();

        var result = await ReviewHandler(db, corrected.Scene, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(corrected.Scene.Run.Id, report.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.verification_evidence_missing", Code(result));
        Assert.Equal(0, store.SealCount);
    }

    [Theory]
    [InlineData("TimedOut", "agent_attempts.verification_evidence_not_passed")]
    [InlineData("Failed", "agent_attempts.verification_evidence_not_passed")]
    [InlineData("Running", "agent_attempts.verification_evidence_running")]
    public async Task The_review_of_a_corrected_report_requires_every_enabled_command_to_have_passed_for_the_new_checkpoint(
        string newKind, string expectedCode)
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync();
        var (checkpoint, report) = await AppliedAsync(corrected);
        await corrected.Scene.AddExecutionAsync(0, Spec.Passed(), checkpoint);
        await corrected.Scene.AddExecutionAsync(1, new Spec(Enum.Parse<Kind>(newKind), Stdout: [1], Stderr: [2]), checkpoint);
        await using var db = _fixture.CreateContext();
        var store = new DiagnosisArtifactStore();

        var result = await ReviewHandler(db, corrected.Scene, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(corrected.Scene.Run.Id, report.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(0, store.SealCount);
        Assert.Empty(db.Attempts.Where(a => a.AgentResponseContract == AgentResponseContract.ImplementationReview));
    }

    [Fact]
    public async Task The_old_checkpoints_failed_verification_never_satisfies_or_blocks_the_new_checkpoints_gate()
    {
        var corrected = await ClaimedAndDispatchedCorrectionAsync();
        var (checkpoint, report) = await AppliedAsync(corrected);
        // Only the new checkpoint matters: the old failed execution is ignored, the new pass is what counts.
        await corrected.Scene.AddExecutionAsync(0, Spec.Passed(), checkpoint);
        await corrected.Scene.AddExecutionAsync(1, Spec.Passed(), checkpoint);
        await using var db = _fixture.CreateContext();
        var store = new DiagnosisArtifactStore();

        var result = await ReviewHandler(db, corrected.Scene, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(corrected.Scene.Run.Id, report.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Theory]
    [InlineData("AcceptedRoot")]
    [InlineData("FirstRevision")]
    public async Task The_review_of_a_corrected_report_seals_the_true_implemented_plan_and_the_correction_evidence(string formName)
    {
        var form = Enum.Parse<PlanForm>(formName);
        var corrected = await ClaimedAndDispatchedCorrectionAsync(form);
        var (checkpoint, report) = await AppliedAsync(corrected);
        await corrected.Scene.AddExecutionAsync(0, Spec.Passed(), checkpoint);
        await corrected.Scene.AddExecutionAsync(1, Spec.Passed(), checkpoint);
        await using var db = _fixture.CreateContext();
        var store = new DiagnosisArtifactStore();

        var result = await ReviewHandler(db, corrected.Scene, store).HandleAsync(
            new CreateCodeReviewAttemptCommand(corrected.Scene.Run.Id, report.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        using var manifest = JsonDocument.Parse(store.ReadManifest(corrected.Scene.Run.Id, result.Value.AttemptId));
        Assert.Equal(corrected.Scene.Implementation.ResolvedPlan.Id, manifest.RootElement.GetProperty("resolvedPlan").GetProperty("messageId").GetGuid());
        Assert.True(manifest.RootElement.TryGetProperty("correctionEvidence", out _));
        await using var verify = _fixture.CreateContext();
        var review = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(AgentResponseContract.ImplementationReview, review.AgentResponseContract);
        Assert.Equal(checkpoint.Id, review.AgentGitCheckpointId);
        Assert.Equal(report.Id, (await verify.AttemptInputMessages.SingleAsync(i => i.AttemptId == review.Id)).CollaborationMessageId);
        Assert.Equal(2, verify.AttemptVerificationEvidence.Count(e => e.AttemptId == review.Id));
    }
}
