using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0057 at the three manual format-repair claims that run through their own short write-locked claim transaction (the Claude critical
/// review, Codex challenge resolution and Codex code review repairs; the Codex planning repair shares the planning claim's transaction and
/// is covered with it). A repair is a new Agent attempt on the workspace, so it is admitted only into an Idle workspace: a competing owner
/// refuses it before any Git capture or sealing and, appearing in the window before the transaction, refuses it at its commit seam with no
/// repair attempt, input, artifact or consumed repair slot, and the sealed manifest removed.
/// </summary>
public sealed class FormatRepairWorkspaceExecutionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task AssertRefusedBeforeAnyExternalWorkAsync(
        Devalente.Shared.Results.Error error, string expectedCode, RepairTestScene scene, WorkspaceExecutionRaceSupport.Footprint before,
        RepairEvidenceReader reader, RepairArtifactStore store)
    {
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, store.SealCount);
        Assert.Empty(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
        Assert.DoesNotContain(await scene.AttemptsAsync(), attempt => attempt.AgentRepairSourceAttemptId is not null);
    }

    private async Task AssertRefusedAtTheSeamAsync(
        Devalente.Shared.Results.Error error, string expectedCode, RepairTestScene scene, WorkspaceExecutionRaceSupport.Footprint before,
        RepairArtifactStore store)
    {
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(1, store.SealCount);
        Assert.Single(store.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, scene.Run.Id, before);
        Assert.DoesNotContain(await scene.AttemptsAsync(), attempt => attempt.AgentRepairSourceAttemptId is not null);
    }

    // ---- Claude critical review repair --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_critical_review_repair_is_refused_by_a_competing_owner_before_any_external_work(CompetingOwner owner)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                scene.Db, reader, store, new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertRefusedBeforeAnyExternalWorkAsync(
            Assert.Single(result.Errors), WorkspaceExecutionRaceSupport.CodeOf(owner), scene, before, reader, store);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_critical_review_repair_is_refused_at_its_commit_seam_by_an_owner_that_wins_before_the_transaction(CompetingOwner owner)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _) = scene.AddCriticalReviewSource();
        await scene.SaveAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner),
        };

        var result = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store, new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateClaudeCriticalReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertRefusedAtTheSeamAsync(Assert.Single(result.Errors), WorkspaceExecutionRaceSupport.CodeOf(owner), scene, before, store);
    }

    // ---- Codex challenge-resolution repair ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_resolution_repair_is_refused_by_a_competing_owner_before_any_external_work(CompetingOwner owner)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var reader = RepairEvidenceReader.Matching(RepairTestScene.Fingerprint);
        var store = new RepairArtifactStore();

        var result = await new CreateChallengeResolutionAttemptCommandHandler(
                scene.Db, reader, store, new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertRefusedBeforeAnyExternalWorkAsync(
            Assert.Single(result.Errors), WorkspaceExecutionRaceSupport.CodeOf(owner), scene, before, reader, store);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_resolution_repair_is_refused_at_its_commit_seam_by_an_owner_that_wins_before_the_transaction(CompetingOwner owner)
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var (source, _, _) = scene.AddResolverSource();
        await scene.SaveAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner),
        };

        var result = await new CreateChallengeResolutionAttemptCommandHandler(
                faulting, RepairEvidenceReader.Matching(RepairTestScene.Fingerprint), store, new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateChallengeResolutionAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertRefusedAtTheSeamAsync(Assert.Single(result.Errors), WorkspaceExecutionRaceSupport.CodeOf(owner), scene, before, store);
    }

    // ---- Codex code review repair -------------------------------------------------------------------------------------------

    private async Task<(RepairTestScene Scene, Attempt Source, RepairTestScene.ImplementationScene Implementation)> CodeReviewSourceAsync()
    {
        var scene = await RepairTestScene.CreateAsync(_fixture);
        var implementation = scene.AddInitialImplementation();
        var verification = await scene.AddPassedVerificationAsync(implementation, 2);
        var source = scene.AddInvalidCodeReview(implementation, verification);
        await scene.SaveAsync();
        return (scene, source, implementation);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_code_review_repair_is_refused_by_a_competing_owner_before_any_external_work(CompetingOwner owner)
    {
        var (scene, source, implementation) = await CodeReviewSourceAsync();
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var reader = RepairEvidenceReader.Matching(implementation.ReviewFingerprint);
        var store = new RepairArtifactStore();

        var result = await new CreateCodeReviewAttemptCommandHandler(
                scene.Db, reader, store, new FixedTimeProvider(RepairTestScene.Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        // The repair's own pure-database eligibility (the claimed verification set must have passed) classifies a Running verification
        // first, before any external work, exactly as it did before; an Agent attempt is refused by the exclusion.
        await AssertRefusedBeforeAnyExternalWorkAsync(
            Assert.Single(result.Errors),
            owner == CompetingOwner.Verification ? "agent_attempts.verification_evidence_running" : WorkspaceExecutionRaceSupport.CodeOf(owner),
            scene,
            before,
            reader,
            store);
    }

    [Fact]
    public async Task A_code_review_repair_is_refused_at_its_commit_seam_by_an_Agent_attempt_that_wins_before_the_transaction()
    {
        var (scene, source, implementation) = await CodeReviewSourceAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, scene.Run.Id);
        var store = new RepairArtifactStore();
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, scene.Run.Id, CompetingOwner.AgentOfAnotherRun),
        };

        var result = await new CreateCodeReviewAttemptCommandHandler(
                faulting, RepairEvidenceReader.Matching(implementation.ReviewFingerprint), store, new FixedTimeProvider(RepairTestScene.Now),
                new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(CreateCodeReviewAttemptCommand.ForRepair(scene.Run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await AssertRefusedAtTheSeamAsync(
            Assert.Single(result.Errors), WorkspaceExecutionRaceSupport.CodeOf(CompetingOwner.AgentOfAnotherRun), scene, before, store);
    }
}
