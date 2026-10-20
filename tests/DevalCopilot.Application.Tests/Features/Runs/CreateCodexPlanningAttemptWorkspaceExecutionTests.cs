using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the Codex planning claim (an ordinary request, which may claim a Created or a Running run) is admitted only into
/// an Idle workspace. A competing owner refuses it before any Git capture or sealing, and an owner that wins in the window between the
/// early check and the claim transaction's write lock refuses it at the commit seam with nothing consumed.</summary>
public sealed partial class CreateCodexPlanningAttemptCommandHandlerTests
{
    [Theory]
    [InlineData(CompetingOwner.Verification, true)]
    [InlineData(CompetingOwner.AgentOfAnotherRun, true)]
    [InlineData(CompetingOwner.Verification, false)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner, bool running)
    {
        Run run;
        await using (var seed = _fixture.CreateContext())
        {
            (_, run, _, _) = await SeedEligibleRunAsync(seed, claimRun: running);
            await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
        }

        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, run.Id);
        var captures = 0;
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateCodexPlanningAttemptCommandHandler(
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

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, run.Id, before);
    }

    /// <summary>Holds each capture until both participants reached it, so the two claims pass their early checks together and meet at
    /// the database write lock.</summary>
    private sealed class BarrierEvidenceReader(Barrier barrier) : IGitWorkspaceEvidenceReader
    {
        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(20)), cancellationToken);
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        }
    }

    [Fact]
    public async Task A_planning_claim_and_a_verification_claim_racing_for_one_workspace_leave_exactly_one_owner()
    {
        Run run;
        Project project;
        GitCheckpoint checkpoint;
        VerificationCommand recipe;
        await using (var seed = _fixture.CreateContext())
        {
            (project, run, _, checkpoint) = await SeedEligibleRunAsync(seed, claimRun: true);
            await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
            recipe = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
            seed.VerificationCommands.Add(recipe);
            await seed.SaveChangesAsync();
        }

        var reader = new BarrierEvidenceReader(new Barrier(2));

        async Task<bool> PlanAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new CreateCodexPlanningAttemptCommandHandler(context, reader, new FakeArtifactStore(), new FixedTimeProvider(Now), DurabilityProbe)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
            return result.IsSuccess;
        }

        async Task<bool> VerifyAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new ClaimVerificationExecutionCommandHandler(context, reader, new FixedTimeProvider(Now))
                .HandleAsync(new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);
            return result.IsSuccess;
        }

        var results = await Task.WhenAll(Task.Run(PlanAsync), Task.Run(VerifyAsync));

        Assert.Single(results, won => won);
        await using var verify = _fixture.CreateContext();
        var planners = await verify.Attempts.CountAsync(a => a.RunId == run.Id && a.AgentRole == AgentRole.Planner);
        var executions = await verify.VerificationExecutions.CountAsync();
        Assert.Equal(1, planners + executions);
        Assert.Equal(executions == 1 ? 2 : 1, await verify.Projects.Where(p => p.Id == project.Id).Select(p => p.NextVerificationExecutionNumber).SingleAsync());
        Assert.Equal(planners, await verify.Artifacts.CountAsync(a => a.RunId == run.Id && a.Purpose == ArtifactPurpose.AgentContextManifest));
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_planning_repair_is_refused_by_a_competing_owner_before_any_work_and_at_its_commit_seam(CompetingOwner owner)
    {
        Run run;
        Attempt source;
        await using (var seed = _fixture.CreateContext())
        {
            (run, _, _, source) = await SeedWithInvalidSourceAsync(seed);
        }

        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, run.Id, owner);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, run.Id);
        var captures = 0;
        var earlyStore = new FakeArtifactStore();
        await using (var early = _fixture.CreateContext())
        {
            var refused = await new CreateCodexPlanningAttemptCommandHandler(
                    early,
                    new RaceInjectingEvidenceReader(
                        new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
                        _ =>
                        {
                            captures++;
                            return Task.CompletedTask;
                        }),
                    earlyStore,
                    new FixedTimeProvider(Now),
                    DurabilityProbe)
                .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

            Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(refused.Errors).Code);
        }

        Assert.Equal(0, captures);
        Assert.Empty(earlyStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, run.Id, before);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_planning_repair_that_loses_the_race_before_the_transaction_consumes_no_repair_slot(CompetingOwner owner)
    {
        Run run;
        Attempt source;
        await using (var seed = _fixture.CreateContext())
        {
            (run, _, _, source) = await SeedWithInvalidSourceAsync(seed);
        }

        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, run.Id);
        await using var inner = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, run.Id, owner),
        };
        var artifactStore = new FakeArtifactStore();

        var result = await new CreateCodexPlanningAttemptCommandHandler(
                faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe)
            .HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id, source.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, run.Id, before);
        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(await verify.Attempts.AsNoTracking().Where(a => a.RunId == run.Id).ToListAsync(), a => a.AgentRepairSourceAttemptId is not null);
    }

    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_owner_that_wins_before_the_claim_transaction_refuses_the_claim_and_removes_its_manifest(CompetingOwner owner)
    {
        Run run;
        await using (var seed = _fixture.CreateContext())
        {
            (_, run, _, _) = await SeedEligibleRunAsync(seed, claimRun: false);
            await RunExecutionModeTestSupport.SetStoredModeAsync(seed, run.Id, AbandonmentRaceSupport.ManualAgentMode);
        }

        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, run.Id);
        await using var innerContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(innerContext)
        {
            BeforeBeginTransaction = _ => WorkspaceExecutionRaceSupport.AddAsync(_fixture, run.Id, owner),
        };
        var artifactStore = new FakeArtifactStore();
        var handler = new CreateCodexPlanningAttemptCommandHandler(
            faulting, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now), DurabilityProbe);

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, run.Id, before);
    }
}
