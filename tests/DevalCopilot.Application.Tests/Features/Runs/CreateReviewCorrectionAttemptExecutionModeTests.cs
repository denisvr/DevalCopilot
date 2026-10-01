using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    [Fact]
    public async Task A_manual_agent_run_claims_a_review_correction_attempt_normally()
    {
        await using var context = _fixture.CreateContext();
        var seed = await SeedAsync(context);
        await RunExecutionModeTestSupport.SetStoredModeAsync(context, seed.Run.Id, (int)RunExecutionMode.ManualAgent);

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            context, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
    }

    [Fact]
    public async Task A_mode_change_between_the_late_read_and_the_commit_persists_nothing()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seedContext, seed.Run.Id, (int)RunExecutionMode.ManualAgent);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, seed.Run.Id, (int)RunExecutionMode.Simulated)));

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        AssertCode(result, CurrentRunExecutionMode.ChangedDuringClaimCode);
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(await verify.Attempts.Where(item => item.RunId == seed.Run.Id && item.AgentResponseContract == AgentResponseContract.ReviewCorrection).ToListAsync());
        Assert.Empty(await verify.Artifacts.Where(item => item.RunId == seed.Run.Id).ToListAsync());
    }
}
