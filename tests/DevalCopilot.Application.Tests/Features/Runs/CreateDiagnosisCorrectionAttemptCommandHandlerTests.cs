using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The diagnosis-origin correction claim (ADR-0018): a Completed DiagnosisFindingsRecorded diagnosis that structurally
/// resolves and still exactly applies claims the existing ReviewCorrection contract with the previous report then every
/// finding in timeline order, a manifest that names its source and carries no raw logs, and the shared run-wide budgets.
/// </summary>
public sealed class CreateDiagnosisCorrectionAttemptCommandHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneWithDiagnosisAsync(
        int findingCount = 2, int maximumAgentAttempts = 16, TimeSpan? maximumAgentInvocationTime = null, params Spec[] specs)
    {
        var scene = await CreateAsync(
            _fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed(stdout: "SENTINEL-RAW-STDOUT", stderr: "SENTINEL-RAW-STDERR")] : specs,
            maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: maximumAgentInvocationTime);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount)).Attempt;
        return (scene, diagnosis);
    }

    private async Task AssertRefusedAsync(
        DiagnosisTestScene scene, Guid diagnosisId, string code, DiagnosisArtifactStore? store = null, RepairEvidenceReader? reader = null)
    {
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosisId, store: store, reader: reader);

        Assert.True(result.IsFailure, result.IsSuccess ? "claimed" : null);
        Assert.Equal(code, Code(result));
        Assert.Equal(before, await scene.CountsAsync());
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
    }

    // ---- Happy path ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_completed_applicable_findings_diagnosis_claims_a_correction_with_the_report_then_every_finding_in_order()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(findingCount: 3);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.Id == created.AttemptId);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, attempt.AgentPermissionProfile);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(scene.Implementation.ReviewCheckpoint.Id, attempt.AgentGitCheckpointId);
        Assert.Equal(scene.Implementation.ReviewFingerprint, attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(TimeSpan.FromMinutes(20), attempt.AgentTimeout);
        Assert.Equal(created.AttemptNumber, attempt.AttemptNumber);
        Assert.Equal(attempt.AttemptNumber, attempt.AgentBudgetSlot);

        var findings = await verify.CollaborationMessages.AsNoTracking()
            .Where(m => m.AttemptId == diagnosis.Id && m.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(m => m.Sequence).Select(m => m.Id).ToListAsync();
        Assert.Equal(3, findings.Count);
        var inputs = await verify.AttemptInputMessages.Where(i => i.AttemptId == attempt.Id).OrderBy(i => i.Sequence).ToListAsync();
        Assert.Equal([0, 1, 2, 3], inputs.Select(i => i.Sequence));
        Assert.Equal([scene.ReportId, .. findings], inputs.Select(i => i.CollaborationMessageId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == attempt.Id));
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
    }

    [Fact]
    public async Task The_manifest_names_the_diagnosis_source_before_the_untrusted_evidence_and_carries_no_raw_logs()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var manifestText = scene.Store.ReadManifest(scene.Run.Id, created.AttemptId);
        using var manifest = JsonDocument.Parse(manifestText);
        var names = manifest.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            "These findings came from an explicit diagnosis of a failed local verification of this implementation, not from a " +
            "code review. Correct only what the findings require. The failing verification output is not included and you must " +
            "not run verification yourself. Do not change verification commands, tools, permissions, or the scope of the plan.",
            manifest.RootElement.GetProperty("sourceNotice").GetString());
        Assert.True(Array.IndexOf(names, "sourceNotice") > Array.IndexOf(names, "expectedOutputSchema"));
        Assert.True(Array.IndexOf(names, "sourceNotice") < Array.IndexOf(names, "untrustedEvidenceBoundary"));
        Assert.DoesNotContain("SENTINEL-RAW-STDOUT", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-RAW-STDERR", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("verificationEvidence", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(DiagnosisTestScene.ExecutableSentinel, manifestText, StringComparison.Ordinal);
        Assert.Contains("Diagnosis finding 1.", manifestText, StringComparison.Ordinal);
        InstructionContextTestSupport.AssertManifestCarriesDeliveredInstructions(manifest.RootElement);
        Assert.True(Array.IndexOf(names, "projectInstructionContext") > Array.IndexOf(names, "untrustedEvidenceBoundary"));
    }

    [Fact]
    public async Task An_ordinary_review_correction_manifest_has_no_source_notice()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;

        var result = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id), CancellationToken.None);

        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        using var manifest = JsonDocument.Parse(scene.Store.ReadManifest(scene.Run.Id, created.AttemptId));
        Assert.False(manifest.RootElement.TryGetProperty("sourceNotice", out _));
    }

    [Fact]
    public async Task A_corrected_report_can_itself_be_diagnosed_and_corrected_again()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed()], corrected: true);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 1)).Attempt;

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    // ---- Source refusals -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_escalation_outcome_diagnosis_is_not_a_correction_source()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated)).Attempt;

        await AssertRefusedAsync(scene, diagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.VerificationEvidenceChanged)]
    [InlineData(AgentOutcome.InputAlreadyDiagnosed)]
    public async Task A_failed_diagnosis_is_not_a_correction_source(AgentOutcome failed)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(failed, dispatched: failed is AgentOutcome.InvalidStructuredOutput or AgentOutcome.ProviderInvocationFailed)).Attempt;

        await AssertRefusedAsync(scene, diagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Fact]
    public async Task A_running_diagnosis_is_not_a_correction_source()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(outcome: null, dispatched: true)).Attempt;

        // The running diagnosis would also block the claim; both refusals are fail-closed.
        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task An_unknown_diagnosis_or_an_ordinary_review_attempt_id_is_not_a_correction_source()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;

        await AssertRefusedAsync(scene, Guid.NewGuid(), CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
        await AssertRefusedAsync(scene, review.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Fact]
    public async Task A_diagnosis_of_another_run_is_not_a_correction_source()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var foreign = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var foreignDiagnosis = (await foreign.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;

        await AssertRefusedAsync(scene, foreignDiagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Theory]
    [InlineData("newer-pass")]
    [InlineData("newer-failure")]
    [InlineData("newer-timed-out")]
    [InlineData("disabled-command")]
    [InlineData("new-enabled-command")]
    [InlineData("failed-output-removed")]
    [InlineData("report-re-pointed")]
    [InlineData("pinned-row-removed")]
    public async Task A_diagnosis_whose_verification_membership_no_longer_applies_is_refused_with_nothing_sealed(string change)
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        switch (change)
        {
            case "newer-pass":
                await scene.AddExecutionAsync(1, Spec.Passed());
                break;
            case "newer-failure":
                await scene.AddExecutionAsync(1, Spec.Failed("n", "n"));
                break;
            case "newer-timed-out":
                await scene.AddExecutionAsync(0, new Spec(Kind.TimedOut));
                break;
            case "disabled-command":
                await scene.SetEnabledAsync(0, enabled: false);
                break;
            case "new-enabled-command":
                await scene.AddEnabledCommandAsync();
                break;
            case "failed-output-removed":
                await scene.SqlAsync("DELETE FROM verification_output_artifacts WHERE VerificationExecutionId = {0}", scene.Executions[1].Id);
                break;
            case "report-re-pointed":
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                break;
            default:
                await scene.SqlAsync("DELETE FROM attempt_verification_evidence WHERE AttemptId = {0} AND Sequence = 0", diagnosis.Id);
                break;
        }

        await AssertRefusedAsync(scene, diagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
        Assert.Equal(0, scene.Store.SealCount);
    }

    [Fact]
    public async Task A_new_current_checkpoint_refuses_before_the_diagnosis_is_even_resolved()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        await scene.AddCheckpointAsync();

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.checkpoint_not_current");
    }

    [Theory]
    [InlineData("finding-foreign-provenance")]
    [InlineData("finding-reply-elsewhere")]
    [InlineData("extra-message")]
    [InlineData("no-findings")]
    [InlineData("foreign-actor")]
    public async Task A_diagnosis_whose_recorded_messages_are_not_its_exact_findings_is_not_a_correction_source(string tamper)
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(findingCount: 2);
        switch (tamper)
        {
            case "finding-foreign-provenance":
                await scene.SqlAsync("UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE AttemptId = {0} AND Summary = 'Diagnosis finding 2.'", diagnosis.Id);
                break;
            case "finding-reply-elsewhere":
                await scene.SqlAsync(
                    "UPDATE collaboration_messages SET InReplyToMessageId = {0} WHERE AttemptId = {1} AND Summary = 'Diagnosis finding 2.'",
                    scene.Implementation.ResolvedPlan.Id, diagnosis.Id);
                break;
            case "extra-message":
                scene.Db.CollaborationMessages.Add(CollaborationMessage.RecordAgent(
                    diagnosis, Guid.NewGuid(), ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, scene.ReportId,
                    "Extra escalation.",
                    "{\"unresolvedDecision\":\"a\",\"options\":\"b\",\"consequences\":\"c\",\"evidence\":\"d\",\"recommendedChoice\":\"e\"}", Now));
                await scene.SaveAsync();
                break;
            case "no-findings":
                await scene.SqlAsync("DELETE FROM collaboration_messages WHERE AttemptId = {0}", diagnosis.Id);
                break;
            default:
                await scene.SqlAsync("UPDATE collaboration_messages SET ActorAgentProvider = 'ClaudeCode' WHERE AttemptId = {0}", diagnosis.Id);
                break;
        }

        await AssertRefusedAsync(scene, diagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Fact]
    public async Task A_diagnosis_with_an_incoherent_tuple_is_not_a_correction_source()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        await scene.Scene.CorruptAsync(diagnosis.Id, "AgentAdapterContractVersion = 'codex-implementation-review-v1'");

        await AssertRefusedAsync(scene, diagnosis.Id, CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode);
    }

    [Fact]
    public async Task A_competing_successful_correction_of_the_exact_input_is_refused_before_anything_is_sealed()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        var number = await scene.NextAttemptNumberAsync();
        var findings = await scene.Db.CollaborationMessages.AsNoTracking()
            .Where(m => m.AttemptId == diagnosis.Id).OrderBy(m => m.Sequence).Select(m => m.Id).ToListAsync();
        var competing = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), scene.Run.Id, number, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id,
            scene.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
        competing.MarkAgentDispatched(Now);
        competing.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), Now, processEvidence: TestProcessEvidence.CleanExit);
        scene.Db.Attempts.Add(competing);
        scene.Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, scene.ReportId, 0));
        for (var index = 0; index < findings.Count; index++)
        {
            scene.Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, findings[index], index + 1));
        }

        await scene.SaveAsync();

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.already_corrected");
        Assert.Equal(0, scene.Store.SealCount);
    }

    // ---- Run-wide authority ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_not_found_and_a_stopped_run_or_a_running_attempt_blocks_the_claim()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();

        Assert.Equal("runs.not_found", Code(await scene.ClaimCorrectionAsync(diagnosis.Id, runId: Guid.NewGuid())));

        await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", scene.Run.Id);
        Assert.Equal("runs.not_running", Code(await scene.ClaimCorrectionAsync(diagnosis.Id)));
    }

    [Fact]
    public async Task A_running_attempt_blocks_the_correction_claim()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        await scene.AddCorrectionHistoryAsync(0);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", diagnosis.Id);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.Equal("attempts.run_has_active_attempt", Code(result));
    }

    [Fact]
    public async Task The_run_wide_agent_count_budget_is_shared_and_refuses_before_any_evidence_or_seal()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(maximumAgentAttempts: 4);
        var reader = scene.Reader();

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.budget_exhausted", reader: reader);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task The_run_wide_reserved_time_budget_is_shared_and_uses_the_twenty_minute_correction_timeout()
    {
        // Four seeded attempts reserve 40 minutes; the correction reserves 20 more against a 55-minute ceiling.
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(maximumAgentInvocationTime: TimeSpan.FromMinutes(55));

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.time_budget_exceeded");
    }

    [Fact]
    public async Task A_reserved_time_exactly_at_the_ceiling_still_claims()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync(maximumAgentInvocationTime: TimeSpan.FromMinutes(60));

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_configured_claude_token_stop_fails_closed_before_any_git_capture_and_a_codex_token_stop_does_not_apply()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        await TokenStopTestSupport.AddHistoryAsync(
            _fixture, scene.Run.Id, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, AgentProvider.Codex,
            TokenStopTestSupport.CodexUsage(5000, 5000));
        await TokenStopTestSupport.SetStopAsync(_fixture, scene.Run.Id, AgentProvider.Codex, 1);
        var codexStopped = await scene.ClaimCorrectionAsync(diagnosis.Id);
        Assert.True(codexStopped.IsSuccess, codexStopped.IsFailure ? Code(codexStopped) : null);

        var (otherScene, otherDiagnosis) = await SceneWithDiagnosisAsync();
        await TokenStopTestSupport.AddHistoryAsync(
            _fixture, otherScene.Run.Id, otherScene.Scene.Workspace.Id, otherScene.Implementation.ReviewCheckpoint.Id, AgentProvider.ClaudeCode,
            TokenStopTestSupport.ClaudeUsage(1000, 200, null, null));
        await TokenStopTestSupport.SetStopAsync(_fixture, otherScene.Run.Id, AgentProvider.ClaudeCode, 1200);
        var reader = otherScene.Reader();

        await AssertRefusedAsync(otherScene, otherDiagnosis.Id, AgentTokenStopGate.EvidenceIndeterminateCode, reader: reader);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task An_unobserved_claude_runtime_is_refused()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        await scene.SqlAsync("DELETE FROM host_capability_snapshots WHERE Capability = 'ClaudeCli'");

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.provider_not_observed");
    }

    [Fact]
    public async Task Drifted_git_evidence_is_refused_before_the_diagnosis_is_resolved()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();

        await AssertRefusedAsync(scene, diagnosis.Id, "agent_attempts.checkpoint_not_current", reader: RepairEvidenceReader.Matching(new string('9', 64)));
    }

    [Fact]
    public async Task A_manifest_seal_failure_leaves_nothing_claimed()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        scene.Store.SealShouldFail = true;
        var before = await scene.CountsAsync();

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.Equal("agent_attempts.context_manifest_seal_failed", Code(result));
        Assert.Equal(before, await scene.CountsAsync());
    }

    // ---- Populated tracker ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_diagnosis_made_inapplicable_between_the_two_resolutions_is_refused_and_the_sealed_manifest_is_deleted()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        // The manifest seal sits between the first resolution and the final untracked re-read.
        scene.Store.AfterSeal = () => scene.AddExecutionAsync(1, Spec.Passed());

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(result));
        var sealedAttemptId = Assert.Single(scene.Store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), scene.Store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
    }

    [Fact]
    public async Task A_report_re_pointed_between_the_two_resolutions_is_refused_despite_the_populated_tracker()
    {
        var (scene, diagnosis) = await SceneWithDiagnosisAsync();
        var unrelated = await scene.AddUnrelatedRootAsync();
        scene.Store.AfterSeal = () => scene.ReplyReportToAsync(unrelated.Id);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, scene.Db);

        Assert.True(result.IsFailure);
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Code(result));
        var sealedAttemptId = Assert.Single(scene.Store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), scene.Store.DeletedSealedFiles);
    }
}
