using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the Claude review-correction claim (the ordinary path, which commits through one short explicit transaction opened
/// after every late read) is admitted only into an Idle workspace. A competing owner refuses it before any Git capture or sealing, and
/// an owner that wins in the window between the early check and the write lock refuses it at the commit seam with nothing consumed.</summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, seed.Run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var captures = 0;
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext();

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext,
            new RecordingEvidenceReader(
                seed.Evidence,
                _ =>
                {
                    captures++;
                    return Task.CompletedTask;
                }),
            store,
            new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_wins_before_the_commit_seam_refuses_the_claim_and_removes_its_manifest(CompetingOwner owner)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => WorkspaceExecutionRaceSupport.AddAsync(_fixture, seed.Run.Id, owner)));

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_workspace_read_removes_the_prepared_manifest_and_keeps_the_original_failure(FenceReadFault fault)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new FenceReadFaultInterceptor(fault, cancellation);
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var handler = new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now));

        if (fault == FenceReadFault.Cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(Command(seed), cancellation.Token));
        }
        else
        {
            var result = await handler.HandleAsync(Command(seed), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        }

        Assert.True(interceptor.Fired);
        Assert.Single(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }
}
