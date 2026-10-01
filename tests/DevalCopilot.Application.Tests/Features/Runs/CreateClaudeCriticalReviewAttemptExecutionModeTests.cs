using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateClaudeCriticalReviewAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_a_critical_review_attempt_normally()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.ManualAgent);
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // The mode changes after the claim last read and strictly before its single save: the guarded UPDATE matches no
    // row, so the Attempt, its input message, and its manifest row all roll back together.
    [Fact]
    public async Task A_mode_change_between_the_late_read_and_the_commit_persists_nothing()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.ManualAgent);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.Simulated)));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.ChangedDuringClaimCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer));
        Assert.Empty(verify.Artifacts.Where(a => a.RunId == runId));
        Assert.Equal(RunLifecycle.Running, verify.Runs.AsNoTracking().Single(r => r.Id == runId).Lifecycle);
    }

    // The mode changes during the claim external evidence capture: the late fresh read refuses it.
    [Fact]
    public async Task A_mode_change_during_external_work_is_refused_by_the_late_read_and_cleans_the_manifest()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.ManualAgent);
        await using var handlerContext = _fixture.CreateContext();
        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
            _ => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.Simulated));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(handlerContext, evidenceReader, artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.NotAdmittedCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer));
    }
}
