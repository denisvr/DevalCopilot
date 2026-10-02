using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The authoritative last gate before a verification diagnosis (and a diagnosis-origin correction) reaches its provider
/// (ADR-0018): the exact tuple, the dedupe of a competing successful diagnosis, and the fresh untracked applicability of the
/// workspace, lease, checkpoint, report chain, and the complete ordered verification selection the claim pinned.
/// </summary>
public sealed class MarkAgentAttemptDispatchedVerificationDiagnosisTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    private async Task<(DiagnosisTestScene Scene, Attempt Attempt)> ClaimedAsync(params Spec[] specs)
    {
        var scene = await CreateAsync(_fixture, specs.Length == 0 ? [Spec.Passed(), Spec.Failed()] : specs);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        await using var read = _fixture.CreateContext();
        return (scene, await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId));
    }

    private async Task<Devalente.Shared.Results.Result<DateTimeOffset>> DispatchAsync(DiagnosisTestScene scene, Attempt attempt)
    {
        await using var db = _fixture.CreateContext();
        var result = await new MarkAgentAttemptDispatchedCommandHandler(db, new FixedTimeProvider(Now)).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(scene.Run.Id, attempt.Id), CancellationToken.None);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync();
        }

        return result;
    }

    private async Task AssertDispatchStateAsync(Attempt attempt, bool dispatched)
    {
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(dispatched, stored.AgentDispatchedAtUtc.HasValue);
        Assert.Equal(AttemptStatus.Running, stored.Status);
    }

    // ---- Diagnosis -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_claimed_unchanged_diagnosis_dispatches()
    {
        var (scene, attempt) = await ClaimedAsync();

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertDispatchStateAsync(attempt, dispatched: true);
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public async Task A_competing_successful_diagnosis_of_the_exact_identity_is_deduplicated_before_the_provider(AgentOutcome outcome)
    {
        var (scene, attempt) = await ClaimedAsync();
        await CompleteCompetitorAsync(scene, attempt, outcome);

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyDiagnosedCode, Code(result));
        Assert.Equal("agent_attempts.input_already_diagnosed", Code(result));
        await AssertDispatchStateAsync(attempt, dispatched: false);
    }

    /// <summary>A completed competitor with the same report and ordered selection. The waiting claim holds the single
    /// Running slot, so the competitor is added while the claim is briefly outside it.</summary>
    private async Task CompleteCompetitorAsync(DiagnosisTestScene scene, Attempt waiting, AgentOutcome outcome)
    {
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", waiting.Id);
        await scene.AddDiagnosisAsync(outcome);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", waiting.Id);
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    public async Task A_failed_competing_diagnosis_does_not_block_dispatch(AgentOutcome failed)
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        await scene.AddDiagnosisAsync(failed);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attempt.Id);

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Fact]
    public async Task A_successful_diagnosis_of_a_different_ordered_selection_does_not_dedupe()
    {
        var (scene, attempt) = await ClaimedAsync(Spec.Passed(), Spec.Failed());
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        var pairs = await scene.CurrentPairsAsync();
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, pairs: [pairs[1], pairs[0]]);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attempt.Id);

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    [Theory]
    [InlineData("lease-released")]
    [InlineData("new-checkpoint")]
    [InlineData("workspace-not-ready")]
    public async Task Workspace_lease_or_checkpoint_drift_is_workspace_no_longer_eligible(string drift)
    {
        var (scene, attempt) = await ClaimedAsync();
        switch (drift)
        {
            case "lease-released":
                await scene.ReleaseLeaseAsync();
                break;
            case "new-checkpoint":
                await scene.AddCheckpointAsync();
                break;
            default:
                await scene.SqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.Scene.Workspace.Id);
                break;
        }

        var result = await DispatchAsync(scene, attempt);

        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Code(result));
        await AssertDispatchStateAsync(attempt, dispatched: false);
    }

    public static TheoryData<string> VerificationDrifts => new()
    {
        "new-enabled-command",
        "disabled-passed-command",
        "disabled-failed-command",
        "newer-pass-of-failure",
        "newer-failure",
        "newer-timed-out",
        "newer-running",
        "failed-output-row-removed",
        "report-re-pointed",
        "report-first-input-removed",
        "pinned-row-removed",
    };

    [Theory]
    [MemberData(nameof(VerificationDrifts))]
    public async Task Verification_or_report_drift_after_the_claim_is_verification_evidence_changed_before_any_provider(string drift)
    {
        var (scene, attempt) = await ClaimedAsync(Spec.Passed(), Spec.Failed());
        var ownerId = scene.Implementation.ExecutionReport.AttemptId!.Value;
        switch (drift)
        {
            case "new-enabled-command":
                await scene.AddEnabledCommandAsync();
                break;
            case "disabled-passed-command":
                await scene.SetEnabledAsync(0, enabled: false);
                break;
            case "disabled-failed-command":
                await scene.SetEnabledAsync(1, enabled: false);
                break;
            case "newer-pass-of-failure":
                await scene.AddExecutionAsync(1, Spec.Passed());
                break;
            case "newer-failure":
                await scene.AddExecutionAsync(1, Spec.Failed("n", "n"));
                break;
            case "newer-timed-out":
                await scene.AddExecutionAsync(0, new Spec(Kind.TimedOut));
                break;
            case "newer-running":
                await scene.AddExecutionAsync(0, new Spec(Kind.Running));
                break;
            case "failed-output-row-removed":
                await scene.SqlAsync("DELETE FROM verification_output_artifacts WHERE VerificationExecutionId = {0} AND Purpose = 'StandardOutput'", scene.Executions[1].Id);
                break;
            case "report-re-pointed":
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                await scene.SaveAsync();
                break;
            case "report-first-input-removed":
                await scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 0", ownerId);
                break;
            default:
                await scene.SqlAsync("DELETE FROM attempt_verification_evidence WHERE AttemptId = {0} AND Sequence = 0", attempt.Id);
                break;
        }

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsFailure, drift);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.VerificationEvidenceChangedCode, Code(result));
        Assert.Equal("agent_attempts.verification_evidence_changed", Code(result));
        await AssertDispatchStateAsync(attempt, dispatched: false);
    }

    [Theory]
    [InlineData("adapter-contract")]
    [InlineData("permission-profile")]
    [InlineData("expected-message-type")]
    public async Task An_incoherent_diagnosis_tuple_is_never_dispatched(string corruption)
    {
        var (scene, attempt) = await ClaimedAsync();
        var assignment = corruption switch
        {
            "adapter-contract" => "AgentAdapterContractVersion = 'codex-implementation-review-v1'",
            "permission-profile" => "AgentPermissionProfile = 'WorkspaceEditOnly'",
            _ => "AgentExpectedMessageType = 'ReviewApproval'",
        };
        await scene.Scene.CorruptAsync(attempt.Id, assignment);

        var result = await DispatchAsync(scene, attempt);

        Assert.True(result.IsFailure, corruption);
        Assert.Equal("agent_attempts.invalid_agent_contract", Code(result));
        await AssertDispatchStateAsync(attempt, dispatched: false);
    }

    [Fact]
    public async Task A_diagnosis_without_its_single_report_input_or_without_pinned_verification_is_never_dispatched()
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0}", attempt.Id);
        Assert.Equal("agent_attempts.invalid_agent_contract", Code(await DispatchAsync(scene, attempt)));

        var (otherScene, otherAttempt) = await ClaimedAsync();
        await otherScene.SqlAsync("DELETE FROM attempt_verification_evidence WHERE AttemptId = {0}", otherAttempt.Id);
        Assert.Equal("agent_attempts.invalid_agent_contract", Code(await DispatchAsync(otherScene, otherAttempt)));
    }

    [Fact]
    public async Task A_second_dispatch_of_the_same_diagnosis_is_refused()
    {
        var (scene, attempt) = await ClaimedAsync();
        Assert.True((await DispatchAsync(scene, attempt)).IsSuccess);

        var again = await DispatchAsync(scene, attempt);

        Assert.Equal("attempts.already_dispatched", Code(again));
    }

    // ---- Ordinary reviews are unaffected ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_ordinary_code_review_still_dispatches_and_dedupes_by_its_own_identity_only()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        var claim = await new Application.Features.Runs.Commands.CreateCodeReviewAttempt.CreateCodeReviewAttemptCommandHandler(
                _fixture.CreateContext(), scene.Reader(), scene.Store, new FixedTimeProvider(Now), new DevalCopilot.Infrastructure.Persistence.AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new Application.Features.Runs.Commands.CreateCodeReviewAttempt.CreateCodeReviewAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        await using var read = _fixture.CreateContext();
        var review = await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId);

        var result = await DispatchAsync(scene, review);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertDispatchStateAsync(review, dispatched: true);
    }

    [Fact]
    public async Task A_successful_diagnosis_of_the_same_report_does_not_dedupe_an_ordinary_review_dispatch()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        var claim = await new Application.Features.Runs.Commands.CreateCodeReviewAttempt.CreateCodeReviewAttemptCommandHandler(
                _fixture.CreateContext(), scene.Reader(), scene.Store, new FixedTimeProvider(Now), new DevalCopilot.Infrastructure.Persistence.AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new Application.Features.Runs.Commands.CreateCodeReviewAttempt.CreateCodeReviewAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", claim.Value.AttemptId);
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", claim.Value.AttemptId);
        await using var read = _fixture.CreateContext();
        var review = await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId);

        var result = await DispatchAsync(scene, review);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }

    // ---- Diagnosis-origin correction -------------------------------------------------------------------------------------

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis, Attempt Correction)> ClaimedCorrectionAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount: 2)).Attempt;
        var claim = await scene.ClaimCorrectionAsync(diagnosis.Id);
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        await using var read = _fixture.CreateContext();
        return (scene, diagnosis, await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId));
    }

    [Fact]
    public async Task An_unchanged_diagnosis_origin_correction_dispatches()
    {
        var (scene, _, correction) = await ClaimedCorrectionAsync();

        var result = await DispatchAsync(scene, correction);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertDispatchStateAsync(correction, dispatched: true);
    }

    [Theory]
    [InlineData("newer-execution")]
    [InlineData("disabled-command")]
    [InlineData("new-enabled-command")]
    [InlineData("new-checkpoint")]
    public async Task A_diagnosis_origin_correction_is_refused_when_its_diagnosis_no_longer_applies(string change)
    {
        var (scene, _, correction) = await ClaimedCorrectionAsync();
        switch (change)
        {
            case "newer-execution":
                await scene.AddExecutionAsync(1, Spec.Passed());
                break;
            case "disabled-command":
                await scene.SetEnabledAsync(0, enabled: false);
                break;
            case "new-enabled-command":
                await scene.AddEnabledCommandAsync();
                break;
            default:
                await scene.AddCheckpointAsync();
                break;
        }

        var result = await DispatchAsync(scene, correction);

        Assert.True(result.IsFailure, change);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Code(result));
        await AssertDispatchStateAsync(correction, dispatched: false);
    }

    [Fact]
    public async Task A_diagnosis_origin_correction_is_refused_when_its_findings_input_no_longer_equals_the_diagnosis_findings()
    {
        var (scene, _, correction) = await ClaimedCorrectionAsync();
        await scene.SqlAsync("DELETE FROM attempt_input_messages WHERE AttemptId = {0} AND Sequence = 2", correction.Id);

        var result = await DispatchAsync(scene, correction);

        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Code(result));
        await AssertDispatchStateAsync(correction, dispatched: false);
    }

    [Fact]
    public async Task An_ordinary_review_origin_correction_dispatch_is_unchanged_by_later_verification_executions()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var review = (await scene.AddOrdinaryChangesRequestedReviewAsync()).Attempt;
        var claim = await scene.OrdinaryCorrectionHandler(_fixture.CreateContext()).HandleAsync(
            new CreateReviewCorrectionAttemptCommand(scene.Run.Id, review.Id), CancellationToken.None);
        Assert.True(claim.IsSuccess, claim.IsFailure ? Code(claim) : null);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        await scene.AddExecutionAsync(1, Spec.Passed());
        await using var read = _fixture.CreateContext();
        var correction = await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId);

        var result = await DispatchAsync(scene, correction);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
    }
}
