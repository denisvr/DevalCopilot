using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateCodeReviewAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_a_code_review_attempt_normally()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, executionReportId) = await SeedReviewableRunAsync(dbContext);
        await RunExecutionModeTestSupport.SetStoredModeAsync(dbContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var handler = new CreateCodeReviewAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64)), new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReportId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_mode_change_before_the_claim_transaction_is_refused_without_persisting_or_leaving_a_manifest()
    {
        await using var innerContext = _fixture.CreateContext();
        var (run, executionReportId) = await SeedReviewableRunAsync(innerContext);
        await RunExecutionModeTestSupport.SetStoredModeAsync(innerContext, run.Id, (int)RunExecutionMode.ManualAgent);
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = _ => RunExecutionModeTestSupport.SetStoredModeAsync(
                _fixture, run.Id, (int)RunExecutionMode.Simulated),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateCodeReviewAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64)), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReportId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == run.Id && attempt.AgentRole == AgentRole.CodeReviewer));
        Assert.Empty(verify.AttemptVerificationEvidence.Where(evidence => verify.Attempts.Any(attempt => attempt.Id == evidence.AttemptId && attempt.AgentRole == AgentRole.CodeReviewer)));
    }
}
