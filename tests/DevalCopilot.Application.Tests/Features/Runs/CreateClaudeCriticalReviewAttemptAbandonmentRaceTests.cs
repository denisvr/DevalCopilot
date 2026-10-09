using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the Claude critical-review claim. This Claude claim
/// commits through one Run UPDATE whose Lifecycle concurrency token loses to any committed transition.</summary>
public sealed partial class CreateClaudeCriticalReviewAttemptCommandHandlerTests
{
    // The abandonment commits strictly between the claim's last read and its single save.
    [Fact]
    public async Task An_abandonment_that_wins_before_the_save_persists_no_attempt_and_removes_the_manifest()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, AbandonmentRaceSupport.ManualAgentMode);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            async () => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, runId)).IsSuccess)));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer));
        Assert.Empty(verify.Artifacts.Where(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == runId).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned));
    }

    // The abandonment commits during the claim's external evidence capture: its late fresh read still sees a Running run, but the
    // lifecycle-token guarded save loses.
    [Fact]
    public async Task An_abandonment_that_wins_during_external_work_persists_no_attempt()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, AbandonmentRaceSupport.ManualAgentMode);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var evidenceReader = new RaceInjectingEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
            async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, runId)).IsSuccess));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, evidenceReader, artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == runId).Lifecycle);
    }

    [Fact]
    public async Task A_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, AbandonmentRaceSupport.ManualAgentMode);

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, runId, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                    claimContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(),
                    new FixedTimeProvider(Now), DurabilityProbe)
                .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, runId, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.CriticalReviewer && a.Status == AttemptStatus.Running));
    }
}
