using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Feed isolation between the two CodeReviewer read-only stages (ADR-0018): the diagnosis supervisor's eligibility feed
/// returns only coherent diagnosis attempts, the ordinary code-review feed returns only ordinary review attempts, and the
/// ordinary status query never reports a diagnosis. Two runs share one database so each feed sees both kinds.
/// </summary>
public sealed class VerificationDiagnosisFeedIsolationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(DiagnosisTestScene Scene, Guid AttemptId)> ClaimedDiagnosisAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()]);
        var claim = await scene.ClaimAsync();
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (scene, claim.Value.AttemptId);
    }

    private async Task<(DiagnosisTestScene Scene, Guid AttemptId)> ClaimedOrdinaryReviewAsync()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        await using var db = _fixture.CreateContext();
        var claim = await new CreateCodeReviewAttemptCommandHandler(
                db, scene.Reader(), scene.Store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new CreateCodeReviewAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (scene, claim.Value.AttemptId);
    }

    private async Task<IReadOnlyList<EligibleVerificationDiagnosisAttempt>> DiagnosisFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return await new GetEligibleVerificationDiagnosisAttemptsQueryHandler(db).HandleAsync(
            new GetEligibleVerificationDiagnosisAttemptsQuery(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<EligibleCodeReviewAttempt>> ReviewFeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return await new GetEligibleCodeReviewAttemptsQueryHandler(db).HandleAsync(new GetEligibleCodeReviewAttemptsQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task Each_feed_returns_only_its_own_attempt_kind()
    {
        var (_, diagnosisId) = await ClaimedDiagnosisAsync();
        var (_, reviewId) = await ClaimedOrdinaryReviewAsync();

        var diagnoses = await DiagnosisFeedAsync();
        var reviews = await ReviewFeedAsync();

        Assert.Equal(diagnosisId, Assert.Single(diagnoses).AttemptId);
        Assert.Equal(reviewId, Assert.Single(reviews).AttemptId);
    }

    [Fact]
    public async Task The_diagnosis_feed_projects_the_bounded_supervisor_facts_of_the_claim()
    {
        var (scene, attemptId) = await ClaimedDiagnosisAsync();
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        var manifest = await verify.Artifacts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);

        var candidate = Assert.Single(await DiagnosisFeedAsync());

        Assert.Equal(scene.Run.Id, candidate.RunId);
        Assert.Equal(scene.Scene.Workspace.Id, candidate.GitWorkspaceId);
        Assert.Equal(scene.Scene.Workspace.WorkspacePath, candidate.WorkspacePath);
        Assert.Equal(scene.Implementation.ReviewCheckpoint.Id, candidate.GitCheckpointId);
        Assert.Equal(scene.Implementation.ReviewFingerprint, candidate.CheckpointFingerprintSha256);
        Assert.Equal(manifest.RelativeStoragePath, candidate.ContextManifestRelativeStoragePath);
        Assert.Equal(manifest.ByteLength, candidate.ContextManifestByteLength);
        Assert.Equal(manifest.ContentHash, candidate.ContextManifestContentHash);
        Assert.Equal(attempt.AgentTimeout, candidate.Timeout);
        Assert.Equal(attempt.AgentMaxBytesPerStream, candidate.MaxBytesPerStream);
        Assert.Equal(attempt.AgentMaxTotalCapturedBytes, candidate.MaxTotalCapturedBytes);
        Assert.Equal(attempt.AgentRequestedModel, candidate.RequestedModel);
    }

    [Fact]
    public async Task An_ordinary_review_attempt_is_never_in_the_diagnosis_feed_and_a_diagnosis_is_never_in_the_review_feed()
    {
        var (_, _) = await ClaimedDiagnosisAsync();

        Assert.Empty(await ReviewFeedAsync());

        var (_, reviewId) = await ClaimedOrdinaryReviewAsync();
        Assert.DoesNotContain(await DiagnosisFeedAsync(), candidate => candidate.AttemptId == reviewId);
    }

    [Theory]
    [InlineData("dispatched")]
    [InlineData("completed")]
    [InlineData("lease-released")]
    [InlineData("new-checkpoint")]
    [InlineData("workspace-not-ready")]
    [InlineData("run-not-running")]
    [InlineData("simulated-mode")]
    [InlineData("adapter-contract")]
    [InlineData("permission-profile")]
    [InlineData("provider")]
    [InlineData("review-contract")]
    public async Task A_claimed_diagnosis_leaves_the_feed_when_it_is_no_longer_dispatchable_or_coherent(string change)
    {
        var (scene, attemptId) = await ClaimedDiagnosisAsync();
        Assert.Single(await DiagnosisFeedAsync());
        switch (change)
        {
            case "dispatched":
                await scene.SqlAsync("UPDATE attempts SET AgentDispatchedAtUtc = {0} WHERE Id = {1}", "2026-09-30 09:00:00+00:00", attemptId);
                break;
            case "completed":
                await scene.SqlAsync("UPDATE attempts SET Status = 'Failed' WHERE Id = {0}", attemptId);
                break;
            case "lease-released":
                await scene.ReleaseLeaseAsync();
                break;
            case "new-checkpoint":
                await scene.AddCheckpointAsync();
                break;
            case "workspace-not-ready":
                await scene.SqlAsync("UPDATE git_workspaces SET Status = 'NeedsAttention' WHERE Id = {0}", scene.Scene.Workspace.Id);
                break;
            case "run-not-running":
                await scene.SqlAsync("UPDATE runs SET Lifecycle = 'Interrupted' WHERE Id = {0}", scene.Run.Id);
                break;
            case "simulated-mode":
                await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, scene.Run.Id, (int)RunExecutionMode.Simulated);
                break;
            case "adapter-contract":
                await scene.Scene.CorruptAsync(attemptId, "AgentAdapterContractVersion = 'codex-implementation-review-v1'");
                break;
            case "permission-profile":
                await scene.Scene.CorruptAsync(attemptId, "AgentPermissionProfile = 'WorkspaceEditOnly'");
                break;
            case "provider":
                await scene.Scene.CorruptAsync(attemptId, "AgentProvider = 'ClaudeCode'");
                break;
            default:
                await scene.Scene.CorruptAsync(attemptId, "AgentResponseContract = 'ImplementationReview'");
                break;
        }

        Assert.Empty(await DiagnosisFeedAsync());
    }

    [Fact]
    public async Task A_manual_agent_run_diagnosis_is_in_the_feed()
    {
        var (scene, attemptId) = await ClaimedDiagnosisAsync();
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, scene.Run.Id, (int)RunExecutionMode.ManualAgent);

        Assert.Equal(attemptId, Assert.Single(await DiagnosisFeedAsync()).AttemptId);
    }

    [Fact]
    public async Task The_review_feed_omits_an_ordinary_review_that_was_retargeted_to_the_diagnosis_contract()
    {
        var (scene, reviewId) = await ClaimedOrdinaryReviewAsync();
        await scene.Scene.CorruptAsync(reviewId, "AgentResponseContract = 'VerificationDiagnosis'");

        Assert.Empty(await ReviewFeedAsync());
    }

    // ---- Ordinary status ignores diagnoses -------------------------------------------------------------------------------

    private async Task<Application.Features.Runs.Queries.GetCodeReviewAttemptStatus.CodeReviewAttemptStatusQueryResult> ReviewStatusAsync(Guid runId)
    {
        await using var db = _fixture.CreateContext();
        var result = await new GetCodeReviewAttemptStatusQueryHandler(db).HandleAsync(new GetCodeReviewAttemptStatusQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public async Task The_ordinary_code_review_status_ignores_a_run_that_only_has_a_diagnosis()
    {
        var (scene, _) = await ClaimedDiagnosisAsync();

        var status = await ReviewStatusAsync(scene.Run.Id);

        Assert.False(status.HasAttempt);
        Assert.Null(status.AttemptId);
    }

    [Fact]
    public async Task The_ordinary_code_review_status_reports_the_review_even_when_a_later_diagnosis_exists()
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Passed()]);
        await using (var db = _fixture.CreateContext())
        {
            var claim = await new CreateCodeReviewAttemptCommandHandler(
                    db, scene.Reader(), scene.Store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options))
                .HandleAsync(new CreateCodeReviewAttemptCommand(scene.Run.Id, scene.ReportId), CancellationToken.None);
            Assert.True(claim.IsSuccess);
            await scene.SqlAsync("UPDATE attempts SET Status = 'Failed', AgentOutcome = 'InvalidStructuredOutput' WHERE Id = {0}", claim.Value.AttemptId);
        }

        var review = await ReviewStatusAsync(scene.Run.Id);
        await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, findingCount: 1);
        var afterDiagnosis = await ReviewStatusAsync(scene.Run.Id);

        Assert.True(review.HasAttempt);
        Assert.Equal(review.AttemptId, afterDiagnosis.AttemptId);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, afterDiagnosis.Outcome);
    }
}
