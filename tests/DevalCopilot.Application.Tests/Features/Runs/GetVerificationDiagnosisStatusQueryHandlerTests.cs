using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The bounded diagnosis status the cockpit reads (ADR-0018): an explicit no-attempt result with a truthful hint about why the
/// current verification can or cannot be diagnosed, the latest diagnosis's facts, whether its correction still applies, the
/// correction's own facts, and the correction allowance shared with ordinary reviews. Never a path, prompt, or output.
/// </summary>
public sealed class GetVerificationDiagnosisStatusQueryHandlerTests : IAsyncLifetime
{
    private static readonly string CorrectedFingerprint = new('e', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<VerificationDiagnosisStatusQueryResult> StatusAsync(Guid runId)
    {
        await using var db = _fixture.CreateContext();
        var result = await new GetVerificationDiagnosisStatusQueryHandler(db).HandleAsync(
            new GetVerificationDiagnosisStatusQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        await using var db = _fixture.CreateContext();

        var result = await new GetVerificationDiagnosisStatusQueryHandler(db).HandleAsync(
            new GetVerificationDiagnosisStatusQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    // ---- No attempt: the diagnosable hint ------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_without_a_diagnosis_reports_no_attempt_and_the_diagnosable_report_when_the_verification_is_diagnosable()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);

        var status = await StatusAsync(scene.Run.Id);

        Assert.False(status.HasAttempt);
        Assert.Null(status.AttemptId);
        Assert.Null(status.Status);
        Assert.Null(status.Outcome);
        Assert.Empty(status.Artifacts);
        Assert.Empty(status.Verification);
        Assert.Equal(0, status.FindingCount);
        Assert.False(status.CorrectionApplicable);
        Assert.Equal(scene.ReportId, status.DiagnosableExecutionReportMessageId);
        Assert.Null(status.DiagnosisUnavailableCode);
        Assert.Null(status.ConfiguredCommandSandbox);
        Assert.Null(status.ConfiguredRolloutPersistence);
    }

    public static TheoryData<string, string> UnavailableHints => new()
    {
        { "no-commands", "verification_diagnosis.no_verification_commands_enabled" },
        { "all-passed", "verification_diagnosis.no_failed_verification" },
        { "running", "verification_diagnosis.evidence_running" },
        { "timed-out", "verification_diagnosis.evidence_not_diagnosable" },
        { "missing", "verification_diagnosis.evidence_missing" },
        { "output-missing", "verification_diagnosis.failed_output_unavailable" },
        { "workspace-not-ready", "verification_diagnosis.workspace_not_ready" },
        { "run-not-running", "verification_diagnosis.workspace_not_ready" },
        { "no-current-report", "verification_diagnosis.no_current_execution_report" },
    };

    [Theory]
    [MemberData(nameof(UnavailableHints))]
    public async Task The_no_attempt_result_names_why_the_current_verification_cannot_be_diagnosed(string situation, string expectedCode)
    {
        var specs = situation switch
        {
            "no-commands" => Array.Empty<Spec>(),
            "all-passed" => [Spec.Passed(), Spec.Passed()],
            "running" => [Spec.Failed(), new Spec(Kind.Running)],
            "timed-out" => [Spec.Failed(), new Spec(Kind.TimedOut)],
            "output-missing" => [new Spec(Kind.Failed, WithOutputs: false)],
            _ => [Spec.Passed(), Spec.Failed()],
        };
        var scene = await CreateAsync(_fixture, specs);
        switch (situation)
        {
            case "missing":
                await scene.AddEnabledCommandAsync();
                break;
            case "workspace-not-ready":
                await scene.SqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.Scene.Workspace.Id);
                break;
            case "run-not-running":
                await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {0}", scene.Run.Id);
                break;
            case "no-current-report":
                await scene.AddCheckpointAsync();
                break;
        }

        var status = await StatusAsync(scene.Run.Id);

        Assert.False(status.HasAttempt);
        Assert.Null(status.DiagnosableExecutionReportMessageId);
        Assert.Equal(expectedCode, status.DiagnosisUnavailableCode);
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public async Task A_successful_diagnosis_of_the_current_identity_makes_it_not_diagnosable_again(AgentOutcome outcome)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(outcome);

        var status = await StatusAsync(scene.Run.Id);

        Assert.True(status.HasAttempt);
        Assert.Null(status.DiagnosableExecutionReportMessageId);
        Assert.Equal("agent_attempts.already_diagnosed", status.DiagnosisUnavailableCode);
    }

    [Fact]
    public async Task A_failed_diagnosis_leaves_the_same_identity_diagnosable_again()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(AgentOutcome.InvalidStructuredOutput);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(scene.ReportId, status.DiagnosableExecutionReportMessageId);
        Assert.Null(status.DiagnosisUnavailableCode);
    }

    // ---- The latest diagnosis --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_running_diagnosis_reports_its_pinned_membership_without_any_path_or_hash_and_the_fixed_sandbox_facts()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed(), Spec.Passed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess);

        var status = await StatusAsync(scene.Run.Id);

        Assert.True(status.HasAttempt);
        Assert.Equal(claim.Value.AttemptId, status.AttemptId);
        Assert.Equal(claim.Value.AttemptNumber, status.AttemptNumber);
        Assert.Equal(scene.ReportId, status.ExecutionReportMessageId);
        Assert.Equal(AttemptStatus.Running, status.Status);
        Assert.Null(status.Outcome);
        Assert.Null(status.DispatchedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(10), status.Timeout);
        Assert.Equal("read-only", status.ConfiguredCommandSandbox);
        Assert.Equal("Disabled", status.ConfiguredRolloutPersistence);
        Assert.False(status.CorrectionApplicable);
        Assert.Equal(
            [(1, "Verification 1", 1, "Passed", 0), (2, "Verification 2", 2, "Failed", 1), (3, "Verification 3", 3, "Passed", 0)],
            status.Verification.Select(m => (m.Position, m.CommandName, m.ExecutionNumber, m.Status, m.ExitCode ?? -1)));
        var artifact = Assert.Single(status.Artifacts);
        Assert.Equal(ArtifactPurpose.AgentContextManifest, artifact.Purpose);
        var serialized = System.Text.Json.JsonSerializer.Serialize(status);
        Assert.DoesNotContain(DiagnosisTestScene.ExecutableSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(DiagnosisTestScene.ArgumentSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Findings_report_their_count_and_the_correction_applies_until_the_evidence_changes()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount: 3)).Attempt;

        var before = await StatusAsync(scene.Run.Id);
        await scene.AddExecutionAsync(1, Spec.Passed());
        var after = await StatusAsync(scene.Run.Id);

        Assert.Equal(diagnosis.Id, before.AttemptId);
        Assert.Equal(AttemptStatus.Completed, before.Status);
        Assert.Equal(AgentOutcome.DiagnosisFindingsRecorded, before.Outcome);
        Assert.Equal(3, before.FindingCount);
        Assert.Null(before.DiagnosisEscalationMessageId);
        Assert.True(before.CorrectionApplicable);
        Assert.False(after.CorrectionApplicable);
        Assert.Equal(3, after.FindingCount);
        Assert.Null(after.CorrectionAttemptId);
    }

    [Theory]
    [InlineData("new-checkpoint")]
    [InlineData("disabled-command")]
    [InlineData("new-enabled-command")]
    [InlineData("lease-released")]
    [InlineData("workspace-not-ready")]
    [InlineData("report-re-pointed")]
    public async Task Correction_applicability_is_false_after_any_change_to_the_workspace_or_the_pinned_evidence(string change)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount: 1);
        Assert.True((await StatusAsync(scene.Run.Id)).CorrectionApplicable);
        switch (change)
        {
            case "new-checkpoint":
                await scene.AddCheckpointAsync();
                break;
            case "disabled-command":
                await scene.SetEnabledAsync(0, enabled: false);
                break;
            case "new-enabled-command":
                await scene.AddEnabledCommandAsync();
                break;
            case "lease-released":
                await scene.ReleaseLeaseAsync();
                break;
            case "workspace-not-ready":
                await scene.SqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.Scene.Workspace.Id);
                break;
            default:
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                break;
        }

        var status = await StatusAsync(scene.Run.Id);

        Assert.False(status.CorrectionApplicable, change);
    }

    [Fact]
    public async Task An_escalation_diagnosis_reports_its_message_and_no_findings_and_no_correction()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var seeded = await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(AgentOutcome.DiagnosisEscalated, status.Outcome);
        Assert.Equal(0, status.FindingCount);
        Assert.Equal(Assert.Single(seeded.Messages).Id, status.DiagnosisEscalationMessageId);
        Assert.False(status.CorrectionApplicable);
        Assert.Null(status.CorrectionAttemptId);
        Assert.Null(status.CorrectionEscalationId);
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    public async Task A_failed_diagnosis_reports_its_outcome_and_never_a_correction(AgentOutcome failed)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(failed);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(AttemptStatus.Failed, status.Status);
        Assert.Equal(failed, status.Outcome);
        Assert.False(status.CorrectionApplicable);
    }

    [Fact]
    public async Task The_latest_diagnosis_by_attempt_number_is_reported_and_ordinary_reviews_are_ignored()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(AgentOutcome.InvalidStructuredOutput);
        var latest = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated)).Attempt;
        await scene.AddOrdinaryChangesRequestedReviewAsync();

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(latest.Id, status.AttemptId);
        Assert.Equal(AgentOutcome.DiagnosisEscalated, status.Outcome);
    }

    [Fact]
    public async Task A_diagnosis_with_an_incoherent_tuple_reports_no_sandbox_facts()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded)).Attempt;
        await scene.Scene.CorruptAsync(diagnosis.Id, "AgentAdapterContractVersion = 'codex-implementation-review-v1'");

        var status = await StatusAsync(scene.Run.Id);

        Assert.Null(status.ConfiguredCommandSandbox);
        Assert.Null(status.ConfiguredRolloutPersistence);
    }

    // ---- Correction facts and the shared allowance -------------------------------------------------------------------------

    [Fact]
    public async Task A_claimed_correction_is_reported_with_its_own_facts_and_the_shared_budget_counts()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id)).Value);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(claim.AttemptId, status.CorrectionAttemptId);
        Assert.Equal(claim.AttemptNumber, status.CorrectionAttemptNumber);
        Assert.Equal(AttemptStatus.Running, status.CorrectionStatus);
        Assert.Null(status.CorrectionOutcome);
        Assert.Null(status.ReviewableExecutionReportMessageId);
        Assert.Equal(2, status.MaximumReviewCorrectionAttempts);
        Assert.Equal(1, status.ReviewCorrectionAttemptsUsed);
        Assert.False(status.CorrectionBudgetExhausted);
    }

    [Fact]
    public async Task An_applied_correction_exposes_the_corrected_report_for_the_ordinary_review()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var seeded = await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2);
        var claim = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(
            (await scene.ClaimCorrectionAsync(seeded.Attempt.Id)).Value);
        await using (var db = _fixture.CreateContext())
        {
            Assert.True((await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
                new MarkAgentAttemptDispatchedCommand(scene.Run.Id, claim.AttemptId), CancellationToken.None)).IsSuccess);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            var correction = ValidatedReviewCorrection.Create(
                seeded.Messages.Select((finding, index) => new ValidatedRevisionResponse(finding.Id, "Applied", $"Fixed {index}.", $"Updated {index}.")).ToArray(),
                ValidatedImplementationReport.Create("Correction completed.", ["src/Foo.cs"], "Applied.", string.Empty, string.Empty, "Run the tests."));
            var recorded = await new RecordReviewCorrectionResultCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
                new RecordReviewCorrectionResultCommand(
                    scene.Run.Id, claim.AttemptId, true, scene.Implementation.ReviewCheckpoint.HeadCommitSha, CorrectedFingerprint,
                    [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], [], correction, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None);
            Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        }

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(AttemptStatus.Completed, status.CorrectionStatus);
        Assert.Equal(AgentOutcome.CorrectionApplied, status.CorrectionOutcome);
        await using var verify = _fixture.CreateContext();
        var correctedReport = await verify.CollaborationMessages.AsNoTracking()
            .SingleAsync(m => m.AttemptId == claim.AttemptId && m.Type == CollaborationMessageType.ExecutionReport);
        Assert.Equal(correctedReport.Id, status.ReviewableExecutionReportMessageId);
        // The diagnosis no longer applies to the new checkpoint.
        Assert.False(status.CorrectionApplicable);
        Assert.Equal(1, status.ReviewCorrectionAttemptsUsed);
    }

    [Fact]
    public async Task The_budget_counts_ordinary_and_diagnosis_origin_corrections_together_and_reports_exhaustion_with_the_escalation()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(2);
        var escalated = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(
            (await scene.ClaimCorrectionAsync(diagnosis.Id)).Value);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(2, status.ReviewCorrectionAttemptsUsed);
        Assert.True(status.CorrectionBudgetExhausted);
        Assert.Equal(escalated.EscalationId, status.CorrectionEscalationId);
        Assert.Equal(escalated.EscalationMessageId, status.CorrectionEscalationMessageId);
        Assert.Null(status.CorrectionAttemptId);
    }

    [Fact]
    public async Task The_no_attempt_result_still_carries_the_shared_budget_counts()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], corrected: true);

        var status = await StatusAsync(scene.Run.Id);

        Assert.False(status.HasAttempt);
        Assert.Equal(1, status.ReviewCorrectionAttemptsUsed);
        Assert.Equal(2, status.MaximumReviewCorrectionAttempts);
        Assert.False(status.CorrectionBudgetExhausted);
    }

    [Fact]
    public async Task Process_evidence_and_token_usage_of_a_concluded_diagnosis_are_projected()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisEscalated);

        var status = await StatusAsync(scene.Run.Id);

        Assert.Equal(TestProcessEvidence.CleanExit, status.ProcessExecution);
        Assert.NotNull(status.DispatchedAtUtc);
        Assert.NotNull(status.CompletedAtUtc);
    }
}
