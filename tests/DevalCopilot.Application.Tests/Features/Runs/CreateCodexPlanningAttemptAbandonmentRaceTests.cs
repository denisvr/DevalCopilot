using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0031: both orders of the race between an explicit abandonment and the Codex planning claim (an ordinary request, which
/// may claim a Created or a Running run). The planning claim guards its commit with in-transaction reads and a lifecycle-token Run
/// update only when it claims a Created run, so a Running run needs its own in-transaction lifecycle read.</summary>
public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    public static TheoryData<bool> InactiveRunStates() => [false, true];

    [Theory]
    [MemberData(nameof(InactiveRunStates))]
    public async Task An_abandonment_that_wins_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest(bool running)
    {
        Run run;
        await using (var seed = _fixture.CreateContext())
        {
            (_, run, _, _) = await SeedEligibleRunAsync(seed, claimRun: running);
            await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
        }

        await using var innerContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, run.Id)).IsSuccess),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateCodexPlanningAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_active", Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await AbandonmentRaceSupport.AssertAbandonedWithoutAttemptsAsync(_fixture, run.Id);
    }

    [Theory]
    [MemberData(nameof(InactiveRunStates))]
    public async Task A_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse(bool running)
    {
        await using var seed = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(seed, claimRun: running);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateCodexPlanningAttemptCommandHandler(
                    claimContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(),
                    new FixedTimeProvider(Now), DurabilityProbe)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.True(result.IsFailure);
        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        var attempt = Assert.Single(verify.Attempts.AsNoTracking().Where(candidate => candidate.RunId == run.Id));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    // ---- the planning format repair: the same handler and seam, claimed from an invalid source ------------------------------

    private async Task<(Run Run, Attempt Source)> SeedManualRepairScenarioAsync()
    {
        await using var seed = _fixture.CreateContext();
        var (run, _, _, source) = await SeedWithInvalidSourceAsync(seed);
        await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
        return (run, source);
    }

    [Fact]
    public async Task An_abandonment_that_wins_before_the_repair_claim_transaction_persists_no_repair_and_removes_its_manifest()
    {
        var (run, source) = await SeedManualRepairScenarioAsync();
        await using var innerContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = async _ => Assert.True((await AbandonmentRaceSupport.AbandonAsync(_fixture, run.Id)).IsSuccess),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateCodexPlanningAttemptCommandHandler(
            faulting, RepairEvidenceReader.Matching(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_active", Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.Attempts.Where(a => a.RunId == run.Id), a => a.AgentRepairSourceAttemptId != null);
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.RunId == run.Id));
        Assert.Equal(RunLifecycle.Abandoned, verify.Runs.AsNoTracking().Single(candidate => candidate.Id == run.Id).Lifecycle);
        Assert.Equal(1, verify.Events.Count(e => e.RunId == run.Id && e.EventType == RunEventType.RunAbandoned));
    }

    [Fact]
    public async Task A_repair_claim_that_wins_before_the_abandonment_transaction_makes_the_abandonment_refuse()
    {
        var (run, source) = await SeedManualRepairScenarioAsync();

        var result = await AbandonmentRaceSupport.AbandonWhileClaimCommitsAsync(_fixture, run.Id, async cancellationToken =>
        {
            await using var claimContext = _fixture.CreateContext();
            var claim = await new CreateCodexPlanningAttemptCommandHandler(
                    claimContext, RepairEvidenceReader.Matching(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), cancellationToken);
            Assert.True(claim.IsSuccess);
        });

        Assert.Equal(RunAbandonmentErrors.ActiveAttemptCode, Assert.Single(result.Errors).Code);
        await AbandonmentRaceSupport.AssertNotAbandonedAsync(_fixture, run.Id, RunLifecycle.Running);
        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Attempts.Where(a => a.RunId == run.Id && a.AgentRepairSourceAttemptId == source.Id && a.Status == AttemptStatus.Running));
    }
}
