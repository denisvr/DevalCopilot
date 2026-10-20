using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the Claude critical-review claim (the ordinary path, which commits through one short explicit transaction opened
/// after every late read) is admitted only into an Idle workspace. A competing owner refuses it before any Git capture or sealing, and
/// an owner that wins in the window between the early check and the write lock refuses it at the commit seam with nothing consumed.</summary>
public sealed partial class CreateClaudeCriticalReviewAttemptCommandHandlerTests
{
    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner)
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var captures = 0;
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext,
            new RaceInjectingEvidenceReader(
                new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
                _ =>
                {
                    captures++;
                    return Task.CompletedTask;
                }),
            artifactStore,
            new FixedTimeProvider(Now),
            DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_wins_before_the_commit_seam_refuses_the_claim_and_removes_its_manifest(CompetingOwner owner)
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner)));
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_workspace_read_removes_the_prepared_manifest_and_keeps_the_original_failure(FenceReadFault fault)
    {
        var (runId, proposalMessageId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new FenceReadFaultInterceptor(fault, cancellation);
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var handler = new CreateClaudeCriticalReviewAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);
        var command = new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessageId);

        if (fault == FenceReadFault.Cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(command, cancellation.Token));
        }
        else
        {
            var result = await handler.HandleAsync(command, CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        }

        Assert.True(interceptor.Fired);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }
}
