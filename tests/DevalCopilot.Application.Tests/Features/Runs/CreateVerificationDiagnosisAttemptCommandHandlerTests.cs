using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The verification-diagnosis claim (ADR-0018): the happy path, the true implemented plan for every plan form, a corrected
/// report, every eligibility refusal (each leaving nothing claimed and no sealed manifest), and the duplicate rule. Real
/// file-backed SQLite and the real claim handler.
/// </summary>
public sealed class CreateVerificationDiagnosisAttemptCommandHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<DiagnosisTestScene> SceneAsync(params Spec[] specs) =>
        await CreateAsync(_fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed()] : specs);

    [Fact]
    public async Task The_claim_pins_the_report_the_ordered_selection_and_a_sealed_manifest_on_the_exact_diagnosis_tuple()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed(), Spec.Passed());

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(4, attempt.AttemptNumber);
        Assert.Equal(4, result.Value.AttemptNumber);
        Assert.Equal(4, attempt.AgentBudgetSlot);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AgentProvider.Codex, attempt.AgentProvider);
        Assert.Equal(AgentRole.CodeReviewer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.VerificationDiagnosis, attempt.AgentResponseContract);
        Assert.Equal(CollaborationMessageType.ReviewFinding, attempt.AgentExpectedMessageType);
        Assert.Equal(AgentPermissionProfile.ReadOnly, attempt.AgentPermissionProfile);
        Assert.Equal(VerificationDiagnosisPolicy.AdapterContractVersion, attempt.AgentAdapterContractVersion);
        Assert.True(VerificationDiagnosisPolicy.HasExactTuple(attempt));
        Assert.Equal(scene.Scene.Workspace.Id, attempt.AgentGitWorkspaceId);
        Assert.Equal(scene.Implementation.ReviewCheckpoint.Id, attempt.AgentGitCheckpointId);
        Assert.Equal(scene.Implementation.ReviewFingerprint, attempt.AgentCheckpointFingerprintSha256);
        Assert.Equal(TimeSpan.FromMinutes(10), attempt.AgentTimeout);
        Assert.Equal(256 * 1024, attempt.AgentMaxBytesPerStream);
        Assert.Equal(512 * 1024, attempt.AgentMaxTotalCapturedBytes);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentRepairSourceAttemptId);

        var input = Assert.Single(await verify.AttemptInputMessages.Where(i => i.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(0, input.Sequence);
        Assert.Equal(scene.ReportId, input.CollaborationMessageId);

        var rows = await verify.AttemptVerificationEvidence.Where(e => e.AttemptId == attempt.Id).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal([0, 1, 2], rows.Select(r => r.Sequence));
        Assert.Equal(scene.Commands.Select(c => c.Id), rows.Select(r => r.VerificationCommandId));
        Assert.Equal(scene.Executions.Select(e => e.Id), rows.Select(r => r.VerificationExecutionId));

        var artifact = Assert.Single(await verify.Artifacts.Where(a => a.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(attempt.AgentContextManifestArtifactId, artifact.Id);
        Assert.Equal(ArtifactPurpose.AgentContextManifest, artifact.Purpose);
        Assert.Equal(ArtifactSensitivity.HostConstructedContent, artifact.Sensitivity);
        Assert.Equal(1, scene.Store.SealCount);
        Assert.Empty(scene.Store.DeletedSealedFiles);
        using var manifest = JsonDocument.Parse(scene.Store.ReadManifest(scene.Run.Id, attempt.Id));
        Assert.Equal("VerificationDiagnosis", manifest.RootElement.GetProperty("expectedResponseContract").GetString());
        Assert.Equal(3, manifest.RootElement.GetProperty("verificationEvidence").GetArrayLength());
        InstructionContextTestSupport.AssertManifestCarriesDeliveredInstructions(manifest.RootElement);
    }

    [Fact]
    public async Task The_claim_pins_the_runs_current_codex_model_and_effort_preference()
    {
        var scene = await SceneAsync();
        await using (var other = _fixture.CreateContext())
        {
            var run = await other.Runs.SingleAsync(r => r.Id == scene.Run.Id);
            run.SetRequestedCodexAssignment("gpt-6-sol", "high");
            await other.SaveChangesAsync();
        }

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal("gpt-6-sol", attempt.AgentRequestedModel);
        Assert.Equal("high", attempt.AgentRequestedEffort);
    }

    [Fact]
    public async Task An_all_failed_selection_is_claimable_and_pins_every_execution_in_command_order()
    {
        var scene = await SceneAsync(Spec.Failed(), Spec.Failed());

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var rows = await verify.AttemptVerificationEvidence.Where(e => e.AttemptId == result.Value.AttemptId).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal(scene.Executions.Select(e => e.Id), rows.Select(r => r.VerificationExecutionId));
    }

    [Fact]
    public async Task The_claim_pins_the_latest_execution_per_command_not_an_older_failure()
    {
        var scene = await SceneAsync(Spec.Failed(), Spec.Failed());
        var newerPass = await scene.AddExecutionAsync(0, Spec.Passed());

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var rows = await verify.AttemptVerificationEvidence.Where(e => e.AttemptId == result.Value.AttemptId).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Equal([newerPass.Id, scene.Executions[1].Id], rows.Select(r => r.VerificationExecutionId));
    }

    // ---- The true implemented plan ---------------------------------------------------------------------------------------

    public static TheoryData<string, bool> Forms => new()
    {
        { "AcceptedRoot", false },
        { "FirstRevision", false },
        { "AcceptedFirstRevision", false },
        { "AcceptedRoot", true },
        { "FirstRevision", true },
        { "AcceptedFirstRevision", true },
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task The_manifest_seals_the_true_implemented_plan_for_every_plan_form_initial_or_corrected(string formName, bool corrected)
    {
        var form = Enum.Parse<PlanForm>(formName);
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], form, corrected);

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        var manifestText = scene.Store.ReadManifest(scene.Run.Id, result.Value.AttemptId);
        using var manifest = JsonDocument.Parse(manifestText);
        var plan = manifest.RootElement.GetProperty("implementedPlan");
        var implemented = scene.Implementation.ResolvedPlan;
        Assert.Equal(implemented.Id, plan.GetProperty("messageId").GetGuid());
        Assert.Equal(implemented.Summary, plan.GetProperty("summary").GetString());
        Assert.Equal(
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(implemented.StructuredContentJson)),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(plan.GetProperty("structuredContent").GetRawText())));
        Assert.Equal(scene.ReportId, manifest.RootElement.GetProperty("executionReport").GetProperty("messageId").GetGuid());
        if (form != PlanForm.AcceptedRoot)
        {
            Assert.NotEqual(scene.Implementation.OriginalPlan.Id, plan.GetProperty("messageId").GetGuid());
            Assert.Contains("Revised steps unique to scope", plan.GetProperty("structuredContent").GetProperty("implementationSteps").GetString());
            Assert.DoesNotContain("Add the table then the query", manifestText, StringComparison.Ordinal);
            Assert.DoesNotContain("Add the ledger table and its query.", manifestText, StringComparison.Ordinal);
            Assert.DoesNotContain(scene.Implementation.OriginalPlan.Id.ToString(), manifestText, StringComparison.OrdinalIgnoreCase);
        }

        await using var verify = _fixture.CreateContext();
        var input = Assert.Single(await verify.AttemptInputMessages.Where(i => i.AttemptId == result.Value.AttemptId).ToListAsync());
        Assert.Equal(scene.ReportId, input.CollaborationMessageId);
    }

    [Fact]
    public async Task A_corrected_report_diagnosis_uses_the_corrected_checkpoint_and_its_own_failed_verification()
    {
        var scene = await CreateAsync(_fixture, [Spec.Failed(stdout: "corrected out", stderr: "corrected err")], corrected: true);

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.SingleAsync(a => a.Id == result.Value.AttemptId);
        Assert.Equal(scene.Implementation.ReviewCheckpoint.Id, attempt.AgentGitCheckpointId);
        Assert.Equal(CorrectedFingerprint, attempt.AgentCheckpointFingerprintSha256);
        Assert.Contains("corrected out", scene.Store.ReadManifest(scene.Run.Id, attempt.Id), StringComparison.Ordinal);
    }

    // ---- Refusals before anything is sealed ------------------------------------------------------------------------------

    private async Task AssertRefusedBeforeSealAsync(DiagnosisTestScene scene, string expectedCode, Func<Task<Devalente.Shared.Results.Result<Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt.CreateVerificationDiagnosisAttemptCommandResult>>>? claim = null)
    {
        var before = await scene.CountsAsync();

        var result = await (claim?.Invoke() ?? scene.ClaimAsync());

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, Code(result));
        Assert.Equal(before, await scene.CountsAsync());
        Assert.Equal(0, scene.Store.SealCount);
        Assert.Empty(scene.Store.DeletedSealedFiles);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        var scene = await SceneAsync();

        await AssertRefusedBeforeSealAsync(scene, "runs.not_found", () => scene.ClaimAsync(runId: Guid.NewGuid()));
    }

    [Fact]
    public async Task A_run_that_is_not_running_cannot_start_a_diagnosis()
    {
        var scene = await SceneAsync();
        await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", scene.Run.Id);

        await AssertRefusedBeforeSealAsync(scene, "runs.not_running");
    }

    [Fact]
    public async Task A_run_whose_mode_does_not_admit_agents_is_refused()
    {
        var scene = await SceneAsync();
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, scene.Run.Id, (int)RunExecutionMode.Simulated);

        await AssertRefusedBeforeSealAsync(scene, CurrentRunExecutionMode.NotAdmittedCode);
    }

    [Fact]
    public async Task A_run_with_an_active_attempt_is_refused()
    {
        var scene = await SceneAsync();
        await scene.AddDiagnosisAsync(outcome: null, dispatched: true);

        await AssertRefusedBeforeSealAsync(scene, "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task An_undispatched_running_attempt_also_blocks_a_new_claim()
    {
        var scene = await SceneAsync();
        await scene.AddDiagnosisAsync(outcome: null, dispatched: false);

        await AssertRefusedBeforeSealAsync(scene, "attempts.run_has_active_attempt");
    }

    [Fact]
    public async Task The_run_wide_agent_claim_budget_is_enforced_before_any_evidence_capture_or_seal()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentAttempts: 3);
        var reader = scene.Reader();

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.budget_exhausted", () => scene.ClaimAsync(reader: reader));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task The_run_wide_reserved_invocation_time_budget_is_enforced_with_the_code_review_timeout()
    {
        // Three seeded attempts reserve 30 minutes; the diagnosis reserves 10 more against a 35-minute ceiling.
        var scene = await CreateAsync(
            _fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromMinutes(35));

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.time_budget_exceeded");
    }

    [Fact]
    public async Task A_reserved_time_exactly_at_the_ceiling_is_still_claimable()
    {
        var scene = await CreateAsync(
            _fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromMinutes(40));

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_reached_codex_token_stop_refuses_before_any_git_capture_or_seal()
    {
        var scene = await SceneAsync();
        await TokenStopTestSupport.AddHistoryAsync(
            _fixture, scene.Run.Id, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, AgentProvider.Codex,
            TokenStopTestSupport.CodexUsage(1000, 200));
        await TokenStopTestSupport.SetStopAsync(_fixture, scene.Run.Id, AgentProvider.Codex, 1200);
        var reader = scene.Reader();

        await using var db = _fixture.CreateContext();
        var before = await scene.CountsAsync();
        var result = await scene.ClaimAsync(reader: reader);

        Assert.True(result.IsFailure);
        Assert.Equal(AgentTokenStopGate.ReachedCode, Code(result));
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, scene.Store.SealCount);
        Assert.Equal(before, await scene.CountsAsync());
    }

    [Fact]
    public async Task A_claude_token_stop_does_not_block_a_codex_diagnosis()
    {
        var scene = await SceneAsync();
        await TokenStopTestSupport.AddHistoryAsync(
            _fixture, scene.Run.Id, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, AgentProvider.ClaudeCode,
            TokenStopTestSupport.ClaudeUsage(5000, 5000, null, null));
        await TokenStopTestSupport.SetStopAsync(_fixture, scene.Run.Id, AgentProvider.ClaudeCode, 1);

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_workspace_that_is_not_ready_is_refused()
    {
        var scene = await SceneAsync();
        await scene.SqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.Scene.Workspace.Id);

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.workspace_not_ready");
    }

    [Fact]
    public async Task A_released_lease_is_refused()
    {
        var scene = await SceneAsync();
        await scene.ReleaseLeaseAsync();

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.lease_not_active");
    }

    [Fact]
    public async Task An_unobserved_codex_runtime_is_refused()
    {
        var scene = await SceneAsync();
        await scene.SqlAsync("DELETE FROM host_capability_snapshots WHERE Capability = 'CodexCli'");

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.provider_not_observed");
    }

    [Fact]
    public async Task Git_evidence_that_no_longer_matches_the_checkpoint_fingerprint_is_refused()
    {
        var scene = await SceneAsync();
        var drifted = RepairEvidenceReader.Matching(new string('9', 64));

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.checkpoint_not_current", () => scene.ClaimAsync(reader: drifted));
    }

    [Fact]
    public async Task A_failed_git_evidence_capture_is_refused()
    {
        var scene = await SceneAsync();
        var failed = new RepairEvidenceReader(new DevalCopilot.Application.Features.Projects.Ports.GitWorkspaceEvidenceResult(
            DevalCopilot.Application.Features.Projects.Ports.GitWorkspaceEvidenceOutcome.GitInvocationFailed,
            null, null, [], null));

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.checkpoint_not_current", () => scene.ClaimAsync(reader: failed));
    }

    [Fact]
    public async Task A_newer_checkpoint_makes_the_report_stale_and_the_claim_is_refused()
    {
        var scene = await SceneAsync();
        var newer = await scene.AddCheckpointAsync();

        // The evidence matches the new current checkpoint, but the report's result is the previous one.
        var reader = RepairEvidenceReader.Matching(newer.FingerprintSha256);

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.result_checkpoint_mismatch", () => scene.ClaimAsync(reader: reader));
    }

    // ---- The report ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_report_is_not_found()
    {
        var scene = await SceneAsync();

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.execution_report_not_found", () => scene.ClaimAsync(reportId: Guid.NewGuid()));
    }

    [Fact]
    public async Task A_report_of_another_run_is_not_found()
    {
        var scene = await SceneAsync();
        await using var otherFixture = new OtherRun(_fixture);
        var foreignReportId = await otherFixture.SeedForeignReportAsync();

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.execution_report_not_found", () => scene.ClaimAsync(reportId: foreignReportId));
    }

    [Fact]
    public async Task A_message_that_is_not_an_execution_report_is_refused()
    {
        var scene = await SceneAsync();

        await AssertRefusedBeforeSealAsync(
            scene, "agent_attempts.not_provider_observed_execution_report",
            () => scene.ClaimAsync(reportId: scene.Implementation.ResolvedPlan.Id));
    }

    [Fact]
    public async Task A_report_whose_owning_implementer_result_is_not_the_current_checkpoint_is_refused()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], corrected: true);
        // The initial implementation's report: its result checkpoint (number 2) is no longer the current one (number 3).
        await using var other = _fixture.CreateContext();
        var initialReportId = await other.CollaborationMessages.AsNoTracking()
            .Where(m => m.RunId == scene.Run.Id && m.Type == CollaborationMessageType.ExecutionReport && m.Id != scene.ReportId)
            .Select(m => m.Id)
            .SingleAsync();

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.result_checkpoint_mismatch", () => scene.ClaimAsync(reportId: initialReportId));
    }

    [Theory]
    [InlineData("not-provider-observed")]
    [InlineData("foreign-actor")]
    [InlineData("reply-to-unrelated-plan")]
    [InlineData("missing-first-input")]
    [InlineData("owner-not-completed")]
    [InlineData("owner-not-implemented")]
    public async Task A_stale_or_tampered_report_chain_is_refused_without_a_reservation_or_manifest(string tamper)
    {
        var scene = await SceneAsync();
        var ownerId = scene.Implementation.ExecutionReport.AttemptId!.Value;
        switch (tamper)
        {
            case "not-provider-observed":
                await scene.SqlAsync("UPDATE collaboration_messages SET Provenance = 'HostConstructed' WHERE Id = {0}", scene.ReportId);
                break;
            case "foreign-actor":
                await scene.SqlAsync("UPDATE collaboration_messages SET ActorAgentProvider = 'Codex' WHERE Id = {0}", scene.ReportId);
                break;
            case "reply-to-unrelated-plan":
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                await scene.SaveAsync();
                break;
            case "missing-first-input":
                await scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 0", ownerId);
                break;
            case "owner-not-completed":
                await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", ownerId);
                break;
            default:
                await scene.SqlAsync("UPDATE attempts SET AgentOutcome = 'NoChangesProduced' WHERE Id = {0}", ownerId);
                break;
        }

        var before = await scene.CountsAsync();
        var result = await scene.ClaimAsync();

        Assert.True(result.IsFailure, tamper);
        Assert.Equal(0, scene.Store.SealCount);
        Assert.Equal(before.Attempts, (await scene.CountsAsync()).Attempts);
        Assert.Equal(before.Artifacts, (await scene.CountsAsync()).Artifacts);
        Assert.Equal(before.Evidence, (await scene.CountsAsync()).Evidence);
    }

    // ---- Verification evidence refusals ----------------------------------------------------------------------------------

    [Fact]
    public async Task No_enabled_command_refuses_the_claim()
    {
        var scene = await CreateAsync(_fixture, []);

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.NoCommandsEnabledCode);
    }

    [Fact]
    public async Task A_missing_execution_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Failed());
        await scene.AddEnabledCommandAsync();

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.EvidenceMissingCode);
    }

    [Fact]
    public async Task A_running_execution_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Failed(), new Spec(Kind.Running));

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.EvidenceRunningCode);
    }

    [Theory]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    [InlineData("Interrupted")]
    [InlineData("SourceChanged")]
    [InlineData("StartFailure")]
    public async Task A_non_diagnosable_terminal_execution_refuses_the_claim(string kind)
    {
        var scene = await SceneAsync(Spec.Failed(), new Spec(Enum.Parse<Kind>(kind)));

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.NotDiagnosableCode);
    }

    [Fact]
    public async Task All_passed_verification_has_no_failure_to_diagnose()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Passed());

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.NoFailedVerificationCode);
    }

    [Fact]
    public async Task A_failed_execution_without_sealed_output_rows_refuses_the_claim()
    {
        var scene = await SceneAsync(new Spec(Kind.Failed, WithOutputs: false));

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.OutputUnavailableCode);
    }

    [Fact]
    public async Task An_unverifiable_sealed_stream_refuses_the_claim_before_any_manifest_is_written()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());
        await using (var other = _fixture.CreateContext())
        {
            var row = await other.VerificationOutputArtifacts.AsNoTracking()
                .FirstAsync(o => o.VerificationExecutionId == scene.Executions[1].Id && o.Purpose == VerificationOutputPurpose.StandardError);
            scene.Store.Remove(row.RelativeStoragePath);
        }

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.OutputUnavailableCode);
    }

    [Fact]
    public async Task A_tampered_sealed_stream_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Failed());
        await using (var other = _fixture.CreateContext())
        {
            var row = await other.VerificationOutputArtifacts.AsNoTracking()
                .FirstAsync(o => o.VerificationExecutionId == scene.Executions[0].Id && o.Purpose == VerificationOutputPurpose.StandardOutput);
            scene.Store.Overwrite(row.RelativeStoragePath, [1, 2, 3]);
        }

        await AssertRefusedBeforeSealAsync(scene, VerificationDiagnosisEvidence.OutputUnavailableCode);
    }

    [Fact]
    public async Task A_manifest_seal_failure_leaves_nothing_claimed()
    {
        var scene = await SceneAsync();
        scene.Store.SealShouldFail = true;
        var before = await scene.CountsAsync();

        var result = await scene.ClaimAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.context_manifest_seal_failed", Code(result));
        Assert.Equal(before, await scene.CountsAsync());
    }

    // ---- Duplicates ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public async Task A_second_claim_after_a_successful_diagnosis_of_the_same_identity_is_refused(AgentOutcome outcome)
    {
        var scene = await SceneAsync();
        await scene.AddDiagnosisAsync(outcome);

        await AssertRefusedBeforeSealAsync(scene, "agent_attempts.already_diagnosed");
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.VerificationEvidenceChanged)]
    [InlineData(AgentOutcome.InputAlreadyDiagnosed)]
    public async Task A_failed_diagnosis_does_not_count_and_the_same_identity_may_be_claimed_again(AgentOutcome failed)
    {
        var scene = await SceneAsync();
        await scene.AddDiagnosisAsync(failed, dispatched: failed is not (AgentOutcome.InputAlreadyDiagnosed or AgentOutcome.VerificationEvidenceChanged));

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_successful_diagnosis_of_a_different_ordered_selection_does_not_block_a_new_identity()
    {
        var scene = await SceneAsync(Spec.Failed(), Spec.Passed());
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded);
        // A newer execution of command 2 changes the ordered execution identity.
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_successful_diagnosis_of_a_different_report_does_not_block_this_report()
    {
        var scene = await SceneAsync();
        var other = await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated);
        await scene.SqlAsync("UPDATE attempt_input_messages SET CollaborationMessageId = {0} WHERE AttemptId = {1}", scene.Implementation.ResolvedPlan.Id, other.Attempt.Id);

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_completed_ordinary_code_review_of_the_same_identity_never_blocks_a_diagnosis()
    {
        var scene = await SceneAsync();
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), scene.Run.Id, scene.Scene.Lineage.ReserveAttemptNumber(), scene.Scene.Workspace.Id,
            scene.Implementation.ReviewCheckpoint.Id, scene.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
            262144, 524288, Now, scene.Scene.Lineage.NextAttemptNumber - 1);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, scene.Implementation.ReviewFingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        scene.Db.Attempts.Add(review);
        scene.Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, scene.ReportId, 0));
        foreach (var (pair, index) in (await scene.CurrentPairsAsync()).Select((pair, index) => (pair, index)))
        {
            scene.Db.AttemptVerificationEvidence.Add(
                AttemptVerificationEvidence.Record(Guid.NewGuid(), review.Id, pair.CommandId, pair.ExecutionId, index));
        }

        await scene.SaveAsync();

        var result = await scene.ClaimAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    private sealed class OtherRun(SqliteDatabaseFixture fixture) : IAsyncDisposable
    {
        public async Task<Guid> SeedForeignReportAsync()
        {
            var foreign = await RepairTestScene.CreateAsync(fixture);
            var implementation = foreign.AddInitialImplementation();
            await foreign.SaveAsync();
            return implementation.ExecutionReport.Id;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
