using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the Claude review-correction claim, which commits
/// through one Run UPDATE whose Lifecycle concurrency token loses to any committed transition.</summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    [Fact]
    public async Task An_abandonment_that_wins_before_the_save_persists_no_attempt_and_removes_the_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seedContext, seed.Run.Id, AbandonmentRaceSupport.ManualAgentMode);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            async () => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, seed.Run.Id)).IsSuccess)));

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(await verify.Attempts.Where(item => item.RunId == seed.Run.Id && item.AgentResponseContract == AgentResponseContract.ReviewCorrection).ToListAsync());
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == seed.Run.Id).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == seed.Run.Id && e.EventType == RunEventType.RunAbandoned));
    }

    [Fact]
    public async Task A_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seedContext, seed.Run.Id, AbandonmentRaceSupport.ManualAgentMode);

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, seed.Run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateReviewCorrectionAttemptCommandHandler(
                    claimContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
                .HandleAsync(Command(seed), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, seed.Run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Attempts.Where(item => item.RunId == seed.Run.Id
            && item.AgentResponseContract == AgentResponseContract.ReviewCorrection && item.Status == AttemptStatus.Running).ToListAsync());
    }
}
