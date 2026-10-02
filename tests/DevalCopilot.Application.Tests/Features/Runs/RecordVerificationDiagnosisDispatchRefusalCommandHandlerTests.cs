using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisDispatchRefusal;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Records a dispatch-gate loss of a still-undispatched verification diagnosis (ADR-0018) only after independently re-deriving
/// it from the database, with no process evidence; a claimed loss that is not actually lost is refused without mutation.
/// </summary>
public sealed class RecordVerificationDiagnosisDispatchRefusalCommandHandlerTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code(Devalente.Shared.Results.Result result) => Assert.Single(result.Errors).Code;

    private async Task<(DiagnosisTestScene Scene, Attempt Attempt)> ClaimedAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess);
        await using var read = _fixture.CreateContext();
        return (scene, await read.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId));
    }

    private async Task<Devalente.Shared.Results.Result> RefuseAsync(
        DiagnosisTestScene scene, Guid attemptId, VerificationDiagnosisDispatchRefusal refusal, Guid? runId = null)
    {
        await using var db = _fixture.CreateContext();
        return await new RecordVerificationDiagnosisDispatchRefusalCommandHandler(db, new FixedTimeProvider(Now.AddMinutes(1))).HandleAsync(
            new RecordVerificationDiagnosisDispatchRefusalCommand(runId ?? scene.Run.Id, attemptId, refusal), CancellationToken.None);
    }

    private async Task AssertUntouchedAsync(Attempt attempt, (int Attempts, int Artifacts, int Inputs, int Evidence, int Messages, int Events) before, DiagnosisTestScene scene)
    {
        Assert.Equal(before, await scene.CountsAsync());
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Running, stored.Status);
        Assert.Null(stored.AgentOutcome);
    }

    private async Task AssertRecordedAsync(Attempt attempt, AgentOutcome outcome)
    {
        await using var verify = _fixture.CreateContext();
        var stored = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Failed, stored.Status);
        Assert.Equal(outcome, stored.AgentOutcome);
        Assert.Null(stored.AgentDispatchedAtUtc);
        Assert.Null(stored.AgentProcessOutcome);
        Assert.Null(stored.GetAgentProcessExecutionEvidence());
        var recorded = Assert.Single(await verify.Events.Where(e => e.AttemptId == attempt.Id).ToListAsync());
        Assert.Equal(RunEventType.AgentAttemptCompleted, recorded.EventType);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), recorded.Actor);
        Assert.Contains(outcome.ToString(), recorded.PayloadJson, StringComparison.Ordinal);
        Assert.Empty(verify.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    [Theory]
    [InlineData(AgentOutcome.DiagnosisFindingsRecorded)]
    [InlineData(AgentOutcome.DiagnosisEscalated)]
    public async Task A_real_competing_successful_diagnosis_is_recorded_as_input_already_diagnosed_without_process_evidence(AgentOutcome competitor)
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        await scene.AddDiagnosisAsync(competitor);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attempt.Id);

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertRecordedAsync(attempt, AgentOutcome.InputAlreadyDiagnosed);
    }

    [Fact]
    public async Task An_input_already_diagnosed_claim_is_refused_when_no_competing_successful_diagnosis_exists()
    {
        var (scene, attempt) = await ClaimedAsync();
        var before = await scene.CountsAsync();

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_eligible", Code(result));
        await AssertUntouchedAsync(attempt, before, scene);
    }

    [Theory]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.VerificationEvidenceChanged)]
    public async Task A_failed_competing_diagnosis_never_justifies_input_already_diagnosed(AgentOutcome failed)
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        await scene.AddDiagnosisAsync(failed, dispatched: failed != AgentOutcome.VerificationEvidenceChanged);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attempt.Id);
        var before = await scene.CountsAsync();

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed);

        Assert.Equal("attempts.not_eligible", Code(result));
        await AssertUntouchedAsync(attempt, before, scene);
    }

    [Fact]
    public async Task A_successful_diagnosis_of_another_ordered_identity_never_justifies_input_already_diagnosed()
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        var pairs = await scene.CurrentPairsAsync();
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, pairs: [pairs[0]]);
        await scene.SqlAsync("UPDATE attempts SET Status = 'Running' WHERE Id = {0}", attempt.Id);

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed);

        Assert.Equal("attempts.not_eligible", Code(result));
    }

    [Theory]
    [InlineData("newer-execution")]
    [InlineData("disabled-command")]
    [InlineData("new-enabled-command")]
    [InlineData("report-re-pointed")]
    public async Task Changed_verification_evidence_is_recorded_as_verification_evidence_changed_without_process_evidence(string change)
    {
        var (scene, attempt) = await ClaimedAsync();
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
                await scene.ReplyReportToAsync((await scene.AddUnrelatedRootAsync()).Id);
                break;
        }

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged);

        Assert.True(result.IsSuccess, result.IsFailure ? Code(result) : null);
        await AssertRecordedAsync(attempt, AgentOutcome.VerificationEvidenceChanged);
    }

    [Fact]
    public async Task A_verification_evidence_changed_claim_is_refused_when_nothing_actually_changed()
    {
        var (scene, attempt) = await ClaimedAsync();
        var before = await scene.CountsAsync();

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged);

        Assert.Equal("attempts.not_eligible", Code(result));
        await AssertUntouchedAsync(attempt, before, scene);
    }

    [Theory]
    [InlineData("lease-released")]
    [InlineData("new-checkpoint")]
    public async Task Workspace_drift_is_not_verification_evidence_changed_and_is_refused_here(string change)
    {
        var (scene, attempt) = await ClaimedAsync();
        if (change == "lease-released")
        {
            await scene.ReleaseLeaseAsync();
        }
        else
        {
            await scene.AddCheckpointAsync();
        }

        var before = await scene.CountsAsync();

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged);

        Assert.Equal("attempts.not_eligible", Code(result));
        await AssertUntouchedAsync(attempt, before, scene);
    }

    [Fact]
    public async Task A_dispatched_attempt_cannot_record_a_dispatch_refusal()
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET AgentDispatchedAtUtc = {0} WHERE Id = {1}", "2026-09-30 09:00:00+00:00", attempt.Id);
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged);

        Assert.Equal("attempts.not_eligible", Code(result));
    }

    [Fact]
    public async Task A_concluded_attempt_cannot_record_a_dispatch_refusal()
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attempt.Id);
        await scene.AddExecutionAsync(1, Spec.Passed());

        var result = await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged);

        Assert.Equal("attempts.not_eligible", Code(result));
    }

    [Fact]
    public async Task An_ordinary_code_review_attempt_is_not_a_verification_diagnosis()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), scene.Run.Id, await scene.NextAttemptNumberAsync(), scene.Scene.Workspace.Id, scene.Implementation.ReviewCheckpoint.Id,
            scene.Implementation.ReviewFingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, scene.Scene.Lineage.NextAttemptNumber - 1);
        scene.Db.Attempts.Add(review);
        await scene.SaveAsync();

        var result = await RefuseAsync(scene, review.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed);

        Assert.Equal("attempts.not_verification_diagnosis", Code(result));
    }

    [Fact]
    public async Task An_unknown_attempt_or_an_attempt_of_another_run_is_not_found_and_an_undefined_refusal_is_invalid()
    {
        var (scene, attempt) = await ClaimedAsync();

        Assert.Equal("attempts.not_found", Code(await RefuseAsync(scene, Guid.NewGuid(), VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed)));
        Assert.Equal("attempts.not_found", Code(await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.InputAlreadyDiagnosed, runId: Guid.NewGuid())));
        Assert.Equal("agent_attempts.invalid_outcome", Code(await RefuseAsync(scene, attempt.Id, (VerificationDiagnosisDispatchRefusal)99)));
    }

    [Fact]
    public async Task A_recorded_refusal_frees_the_run_so_the_same_identity_may_be_claimed_again_only_when_no_success_exists()
    {
        var (scene, attempt) = await ClaimedAsync();
        await scene.AddExecutionAsync(1, Spec.Failed("again", "again"));
        Assert.True((await RefuseAsync(scene, attempt.Id, VerificationDiagnosisDispatchRefusal.VerificationEvidenceChanged)).IsSuccess);

        var next = await scene.ClaimAsync();

        Assert.True(next.IsSuccess, next.IsFailure ? Code(next) : null);
        Assert.NotEqual(attempt.Id, next.Value.AttemptId);
    }
}
