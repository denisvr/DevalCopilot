using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the three manual format-repair claims that run in
/// their own short claim transaction (critical review, challenge resolution and code review; the planning repair is covered beside
/// the planning claim). A repair is another claim form of the same handler, so a repair that decided before an abandonment must not
/// commit an attempt, and a repair that committed first must make the abandonment refuse.</summary>
public sealed class RepairClaimsAbandonmentRaceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static RepairEvidenceReader Reader() => RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);

    /// <summary>The scene seeds a Legacy run; the abandonment needs the stored manual mode, written as another request would, with the
    /// tracker cleared so the handler reads the run afresh exactly as production does.</summary>
    private async Task MakeManualAsync(RepairTestScene scene)
    {
        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, scene.Run.Id, AbandonmentRaceSupport.ManualAgentMode);
        scene.Detach();
    }

    private async Task AssertAbandonedWithNoRepairAsync(RepairTestScene scene, int expectedAttempts, RepairArtifactStore store)
    {
        var deleted = Assert.Single(store.DeletedSealedFiles);
        Assert.Equal(ArtifactPurpose.AgentContextManifest, deleted.Purpose);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(expectedAttempts, await verify.Attempts.CountAsync(a => a.RunId == scene.Run.Id));
        Assert.DoesNotContain(verify.Attempts.Where(a => a.RunId == scene.Run.Id), a => a.AgentRepairSourceAttemptId != null);
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == deleted.AttemptId));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(r => r.Id == scene.Run.Id).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == scene.Run.Id && e.EventType == RunEventType.RunAbandoned));
    }

    private async Task AssertRepairWonAsync(RepairTestScene scene, Attempt source, Devalente.Shared.Results.IResult abandonment)
    {
        Assert.True(abandonment.IsFailure);
        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(abandonment.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, scene.Run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == scene.Run.Id && a.AgentRepairSourceAttemptId == source.Id && a.Status == AttemptStatus.Running));
    }

    // ---- the critical-review repair ---------------------------------------------------------------------------------------

    private async Task<(RepairTestScene Scene, Attempt Source)> CriticalReviewSceneAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await MakeManualAsync(scene);
        return (scene, source);
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_critical_review_repair_claim_commits_persists_no_repair()
    {
        var (scene, source) = await CriticalReviewSceneAsync();
        var store = new RepairArtifactStore();
        var attemptsBefore = await scene.AttemptCountAsync();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, scene.Run.Id)).IsSuccess),
        };

        var result = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                faulting, Reader(), store, new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertAbandonedWithNoRepairAsync(scene, attemptsBefore, store);
    }

    [Fact]
    public async Task A_critical_review_repair_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (scene, source) = await CriticalReviewSceneAsync();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, scene.Run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                    claimContext, Reader(), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                    new AttemptDurabilityProbe(_fixture.Options))
                .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        await AssertRepairWonAsync(scene, source, result);
    }

    // ---- the challenge-resolution repair ----------------------------------------------------------------------------------

    private async Task<(RepairTestScene Scene, Attempt Source)> ResolverSceneAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource(challengeCount: 2);
        await scene.SaveAsync();
        await MakeManualAsync(scene);
        return (scene, source);
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_challenge_resolution_repair_claim_commits_persists_no_repair()
    {
        var (scene, source) = await ResolverSceneAsync();
        var store = new RepairArtifactStore();
        var attemptsBefore = await scene.AttemptCountAsync();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, scene.Run.Id)).IsSuccess),
        };

        var result = await new CreateChallengeResolutionAttemptCommandHandler(
                faulting, Reader(), store, new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentRunLifecycle.NotRunningCode, Assert.Single(result.Errors).Code);
        await AssertAbandonedWithNoRepairAsync(scene, attemptsBefore, store);
    }

    [Fact]
    public async Task A_challenge_resolution_repair_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (scene, source) = await ResolverSceneAsync();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, scene.Run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateChallengeResolutionAttemptCommandHandler(
                    claimContext, Reader(), new RepairArtifactStore(), new FixedTimeProvider(RepairTestScene.Now),
                    new AttemptDurabilityProbe(_fixture.Options))
                .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, source.Id), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        await AssertRepairWonAsync(scene, source, result);
    }

    // ---- the code-review repair -------------------------------------------------------------------------------------------

    private async Task<(RepairTestScene Scene, Attempt Source, string Fingerprint)> CodeReviewSceneAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation, 2);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        await MakeManualAsync(scene);
        return (scene, source, implementation.ReviewFingerprint);
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_code_review_repair_claim_commits_persists_no_repair()
    {
        var (scene, source, fingerprint) = await CodeReviewSceneAsync();
        var store = new RepairArtifactStore();
        var attemptsBefore = await scene.AttemptCountAsync();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, scene.Run.Id)).IsSuccess),
        };

        var result = await new CreateCodeReviewAttemptCommandHandler(
                faulting, RepairEvidenceReader.Matching(fingerprint), store, new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrentRunLifecycle.NotRunningCode, Assert.Single(result.Errors).Code);
        await AssertAbandonedWithNoRepairAsync(scene, attemptsBefore, store);
    }

    [Fact]
    public async Task A_code_review_repair_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (scene, source, fingerprint) = await CodeReviewSceneAsync();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, scene.Run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateCodeReviewAttemptCommandHandler(
                    claimContext, RepairEvidenceReader.Matching(fingerprint), new RepairArtifactStore(),
                    new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
                .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        await AssertRepairWonAsync(scene, source, result);
    }
}
