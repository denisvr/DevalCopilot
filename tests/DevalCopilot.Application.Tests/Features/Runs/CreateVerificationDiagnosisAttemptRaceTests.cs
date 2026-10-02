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
/// The claim's populated-tracker seam (ADR-0018): every piece of authority is read UNTRACKED inside the short claim
/// transaction, after all external work. The handler runs on the very context that seeded (so it tracks the run,
/// workspace, checkpoints, report chain, commands, and executions) and an independent context commits a change exactly
/// before the transaction begins. A tracked or skipped re-read would still see the old world; the claim must refuse, persist
/// nothing, and delete the sealed manifest.
/// </summary>
public sealed class CreateVerificationDiagnosisAttemptRaceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<DiagnosisTestScene> SceneAsync(params Spec[] specs) =>
        await CreateAsync(_fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed()] : specs);

    /// <summary>Claims on the scene's own populated (tracking) context with <paramref name="race"/> committed from an
    /// independent context at the transaction seam, and asserts the claim was refused with <paramref name="expectedCode"/>
    /// and left nothing durable behind.</summary>
    private async Task AssertRefusedAtTheSeamAsync(
        DiagnosisTestScene scene, Func<Task> race, string expectedCode, DiagnosisArtifactStore? store = null)
    {
        store ??= scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db) { BeforeBeginTransaction = _ => race() };

        var result = await scene.ClaimAsync(faulting, store: store);

        Assert.True(result.IsFailure, "The claim must be refused at the seam.");
        Assert.Equal(expectedCode, Code(result));
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(e => e.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
    }

    [Fact]
    public async Task A_newer_pass_of_the_only_failure_committed_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.AddExecutionAsync(1, Spec.Passed()), VerificationDiagnosisEvidence.NoFailedVerificationCode);
    }

    [Fact]
    public async Task A_newer_failed_execution_committed_at_the_seam_changes_the_pinned_membership_and_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Failed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.AddExecutionAsync(0, Spec.Failed("newer", "newer")), VerificationDiagnosisEvidence.NotDiagnosableCode);
    }

    [Fact]
    public async Task A_newer_timed_out_execution_committed_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.AddExecutionAsync(1, new Spec(Kind.TimedOut)), VerificationDiagnosisEvidence.NotDiagnosableCode);
    }

    [Fact]
    public async Task A_newer_running_execution_committed_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.AddExecutionAsync(0, new Spec(Kind.Running)), VerificationDiagnosisEvidence.EvidenceRunningCode);
    }

    [Fact]
    public async Task Disabling_a_passed_command_at_the_seam_changes_the_enabled_set_and_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.SetEnabledAsync(0, enabled: false), VerificationDiagnosisEvidence.NotDiagnosableCode);
    }

    [Fact]
    public async Task Disabling_the_only_failed_command_at_the_seam_leaves_nothing_to_diagnose()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.SetEnabledAsync(1, enabled: false), VerificationDiagnosisEvidence.NoFailedVerificationCode);
    }

    [Fact]
    public async Task A_new_enabled_command_without_evidence_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.AddEnabledCommandAsync(), VerificationDiagnosisEvidence.EvidenceMissingCode);
    }

    [Fact]
    public async Task A_failed_output_row_removed_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => scene.SqlAsync("DELETE FROM verification_output_artifacts WHERE VerificationExecutionId = {0} AND Purpose = 'StandardError'", scene.Executions[1].Id),
            VerificationDiagnosisEvidence.OutputUnavailableCode);
    }

    [Fact]
    public async Task A_failed_output_row_replaced_at_the_seam_refuses_the_claim_because_the_pinned_identity_changed()
    {
        var scene = await SceneAsync(Spec.Passed(), Spec.Failed());

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => scene.SqlAsync(
                "UPDATE verification_output_artifacts SET Id = {0} WHERE VerificationExecutionId = {1} AND Purpose = 'StandardOutput'",
                Guid.NewGuid(), scene.Executions[1].Id),
            VerificationDiagnosisEvidence.NotDiagnosableCode);
    }

    [Fact]
    public async Task A_new_current_checkpoint_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(scene, () => scene.AddCheckpointAsync(), "agent_attempts.checkpoint_not_current");
    }

    [Fact]
    public async Task A_lease_released_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(scene, scene.ReleaseLeaseAsync, "agent_attempts.checkpoint_not_current");
    }

    [Fact]
    public async Task A_workspace_no_longer_ready_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => scene.SqlAsync("UPDATE git_workspaces SET Status = 'AlteredExternally' WHERE Id = {0}", scene.Scene.Workspace.Id),
            "agent_attempts.checkpoint_not_current");
    }

    [Fact]
    public async Task A_run_that_stopped_running_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => scene.SqlAsync("UPDATE runs SET Lifecycle = 'Interrupted' WHERE Id = {0}", scene.Run.Id),
            "agent_attempts.checkpoint_not_current");
    }

    [Theory]
    [InlineData("report-reply-to-root")]
    [InlineData("report-first-input-removed")]
    public async Task A_report_chain_changed_at_the_seam_is_refused_despite_the_populated_tracker(string change)
    {
        var scene = await SceneAsync();
        var ownerId = scene.Implementation.ExecutionReport.AttemptId!.Value;
        var unrelated = await scene.AddUnrelatedRootAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => change == "report-reply-to-root"
                ? scene.ReplyReportToAsync(unrelated.Id)
                : scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 0", ownerId),
            "agent_attempts.implementer_attempt_not_valid");
    }

    [Fact]
    public async Task A_report_re_pointed_to_the_planner_root_of_a_revised_plan_is_refused_at_the_seam()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], PlanForm.FirstRevision);
        Assert.NotEqual(scene.Implementation.OriginalPlan.Id, scene.Implementation.ResolvedPlan.Id);

        await AssertRefusedAtTheSeamAsync(
            scene, () => scene.ReplyReportToAsync(scene.Implementation.OriginalPlan.Id), "agent_attempts.implementer_attempt_not_valid");
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public async Task A_competing_successful_diagnosis_committed_at_the_seam_refuses_the_claim(AgentOutcome outcome)
    {
        var scene = await SceneAsync();
        var pairs = await scene.CurrentPairsAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            async () =>
            {
                // A competing claim that already completed: it needs its own attempt number and budget slot.
                await using var other = _fixture.CreateContext();
                var number = await other.Attempts.CountAsync(a => a.RunId == scene.Run.Id) + 1;
                var attempt = Attempt.ClaimAgentVerificationDiagnosis(
                    Guid.NewGuid(), scene.Run.Id, number, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id,
                    scene.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number);
                attempt.MarkAgentDispatched(Now);
                attempt.CompleteAgent(outcome, scene.Implementation.ReviewFingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
                other.Attempts.Add(attempt);
                other.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, scene.ReportId, 0));
                for (var index = 0; index < pairs.Count; index++)
                {
                    other.AttemptVerificationEvidence.Add(
                        AttemptVerificationEvidence.Record(Guid.NewGuid(), attempt.Id, pairs[index].CommandId, pairs[index].ExecutionId, index));
                }

                await other.SaveChangesAsync();
            },
            "agent_attempts.already_diagnosed");
    }

    [Fact]
    public async Task A_running_attempt_committed_at_the_seam_refuses_the_claim()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            async () =>
            {
                await using var other = _fixture.CreateContext();
                var number = await other.Attempts.CountAsync(a => a.RunId == scene.Run.Id) + 1;
                other.Attempts.Add(TokenStopTestSupport.RunningHistory(
                    scene.Run.Id, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, number, AgentProvider.Codex));
                await other.SaveChangesAsync();
            },
            "attempts.run_has_active_attempt");
    }

    // ---- Budget slot races -----------------------------------------------------------------------------------------------

    private static Func<Task> CompetingCompletedClaim(DiagnosisTestScene scene, int slot) => async () =>
    {
        await using var other = new SqliteDatabaseFixtureAccess(scene).Context();
        other.Attempts.Add(TokenStopTestSupport.ConcludedHistory(
            scene.Run.Id, scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id, slot, AgentProvider.Codex, null));
        await other.SaveChangesAsync();
    };

    [Fact]
    public async Task A_concurrent_claim_of_the_same_budget_slot_below_the_maximum_is_a_retryable_conflict()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(scene, CompetingCompletedClaim(scene, slot: 4), "agent_attempts.budget_slot_conflict");
    }

    [Fact]
    public async Task A_concurrent_claim_of_the_last_budget_slot_is_truthfully_exhaustion()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentAttempts: 4);

        await AssertRefusedAtTheSeamAsync(scene, CompetingCompletedClaim(scene, slot: 4), "agent_attempts.budget_exhausted");
    }

    private sealed class SqliteDatabaseFixtureAccess(DiagnosisTestScene scene)
    {
        public DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext Context() => scene.Fixture.CreateContext();
    }

    // ---- Guards written inside the transaction ---------------------------------------------------------------------------

    [Fact]
    public async Task A_preference_change_at_the_seam_is_excluded_by_the_confirmation_guard()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            async () =>
            {
                await using var other = _fixture.CreateContext();
                var run = await other.Runs.SingleAsync(r => r.Id == scene.Run.Id);
                run.SetRequestedCodexAssignment("gpt-6-sol", "high");
                await other.SaveChangesAsync();
            },
            "agent_attempts.assignment_preference_changed");
    }

    [Fact]
    public async Task A_token_stop_policy_change_at_the_seam_is_excluded_by_the_confirmation_guard()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => TokenStopTestSupport.SetStopAsync(_fixture, scene.Run.Id, AgentProvider.Codex, 5),
            CurrentTokenStopPolicy.PolicyChangedDuringClaimCode);
    }

    [Fact]
    public async Task A_mode_change_at_the_seam_is_excluded_by_the_confirmation_guard()
    {
        var scene = await SceneAsync();

        await AssertRefusedAtTheSeamAsync(
            scene,
            () => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, scene.Run.Id, (int)RunExecutionMode.Simulated),
            CurrentRunExecutionMode.NotAdmittedCode);
    }

    [Fact]
    public async Task The_unraced_claim_on_the_populated_context_succeeds_so_each_refusal_above_is_the_race_itself()
    {
        var scene = await SceneAsync();
        var faulting = new FaultInjectingDbContext(scene.Db) { BeforeBeginTransaction = _ => Task.CompletedTask };

        var result = await scene.ClaimAsync(faulting);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        Assert.Empty(scene.Store.DeletedSealedFiles);
    }
}
