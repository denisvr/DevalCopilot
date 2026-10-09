using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the Claude implementation claim, which commits
/// through one Run UPDATE whose Lifecycle concurrency token loses to any committed transition.</summary>
public sealed partial class CreateImplementationAttemptCommandHandlerTests
{
    [Fact]
    public async Task An_abandonment_that_wins_before_the_save_persists_no_attempt_and_removes_the_manifest()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, AbandonmentRaceSupport.ManualAgentMode);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            async () => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, runId)).IsSuccess)));
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.Implementer));
        Assert.Empty(verify.AttemptInputMessages.Where(message => verify.Attempts.Any(a => a.Id == message.AttemptId && a.AgentRole == AgentRole.Implementer)));
        Assert.Empty(verify.Artifacts.Where(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest
            && verify.Attempts.Any(attempt => attempt.Id == a.AttemptId && attempt.AgentRole == AgentRole.Implementer)));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == runId).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned));
    }

    [Fact]
    public async Task A_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, AbandonmentRaceSupport.ManualAgentMode);

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, runId, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateImplementationAttemptCommandHandler(
                    claimContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now))
                .HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, runId, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.Implementer && a.Status == AttemptStatus.Running));
    }
}
