using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0058: the Claude review-correction claim is one of at most two recorded potentially mutating owners across the database.
/// A full database refuses it before any Git capture or sealing, an owner that takes the last place before its write lock refuses it at
/// the commit seam, and a failed or cancelled final capacity read removes only its own manifest; nothing is consumed in any case.</summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private const string CapacityLimitReachedCode = "execution_capacity.limit_reached";

    [Fact]
    public async Task A_database_at_capacity_refuses_the_claim_before_any_external_work_and_consumes_nothing()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification);
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
        Assert.Equal(CapacityLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit)]
    public async Task The_last_place_taken_before_the_commit_seam_refuses_the_claim_and_removes_its_manifest(OwnerFamily late)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ExecutionCapacitySeed.AddAsync(_fixture, late)));

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CapacityLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_capacity_read_removes_the_prepared_manifest_and_keeps_the_original_failure(FenceReadFault fault)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CapacityReadFaultInterceptor(fault, cancellation);
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

    [Fact]
    public async Task A_correction_is_admitted_beside_one_other_owner_and_a_finished_owner_frees_a_full_database()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        var owners = await ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Agent, OwnerFamily.Verification);

        await using (var full = _fixture.CreateContext())
        {
            var refused = await new CreateReviewCorrectionAttemptCommandHandler(
                full, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
                .HandleAsync(Command(seed), CancellationToken.None);
            Assert.Equal(CapacityLimitReachedCode, Assert.Single(refused.Errors).Code);
        }

        await ExecutionCapacitySeed.FinishAsync(_fixture, owners[0]);

        await using var freed = _fixture.CreateContext();
        var admitted = await new CreateReviewCorrectionAttemptCommandHandler(
            freed, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);
        Assert.True(admitted.IsSuccess);
    }

    /// <summary>ADR-0059: at limit one a single other recorded owner refuses the correction before any external work, the one place taken
    /// before the commit seam refuses it with its manifest removed, and a lone correction is admitted.</summary>
    [Theory]
    [InlineData(OwnerFamily.Agent)]
    [InlineData(OwnerFamily.Verification)]
    [InlineData(OwnerFamily.LocalCommit)]
    public async Task At_limit_one_a_single_other_owner_refuses_the_claim_before_any_external_work_and_consumes_nothing(OwnerFamily other)
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ExecutionCapacitySeed.SetLimitAsync(_fixture, 1);
        await ExecutionCapacitySeed.AddAsync(_fixture, other);
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
        Assert.Equal(CapacityLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Fact]
    public async Task At_limit_one_the_place_taken_before_the_commit_seam_refuses_the_claim_and_removes_its_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ExecutionCapacitySeed.SetLimitAsync(_fixture, 1);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, seed.Run.Id);
        var store = new TestArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => ExecutionCapacitySeed.AddAsync(_fixture, OwnerFamily.Verification)));

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now)).HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CapacityLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, seed.Run.Id, before);
    }

    [Fact]
    public async Task At_limit_one_a_lone_correction_is_admitted()
    {
        await using var seedContext = _fixture.CreateContext();
        var seed = await SeedAsync(seedContext);
        await ExecutionCapacitySeed.SetLimitAsync(_fixture, 1);
        await using var handlerContext = _fixture.CreateContext();

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
            handlerContext, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
