using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateImplementationAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_an_implementation_attempt_normally()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.ManualAgent);
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_mode_change_between_the_late_read_and_the_commit_persists_nothing()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.ManualAgent);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, runId, (int)RunExecutionMode.Simulated)));
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.Equal(CurrentRunExecutionMode.ChangedDuringClaimCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId && a.AgentRole == AgentRole.Implementer));
        Assert.Empty(verify.AttemptInputMessages.Where(message => verify.Attempts.Any(a => a.Id == message.AttemptId && a.AgentRole == AgentRole.Implementer)));
        Assert.Empty(verify.Artifacts.Where(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest));
    }
}
