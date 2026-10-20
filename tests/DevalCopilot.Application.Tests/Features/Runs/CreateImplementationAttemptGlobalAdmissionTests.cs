using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0058: the Claude implementation claim (ordinary path) is one of at most two recorded potentially mutating owners across
/// the database. A full database refuses it before any Git capture or sealing, and an owner that takes the last place in the window
/// before the write lock, or a verification claim racing it for that place, refuses exactly one claimant with nothing consumed.</summary>
public sealed partial class CreateImplementationAttemptCommandHandlerTests
{
    private const string GlobalLimitReachedCode = "execution_capacity.limit_reached";

    private async Task<WorkspaceExecutionScene> AddUnrelatedScenarioAsync()
    {
        await using var db = _fixture.CreateContext();
        return await WorkspaceExecutionSeed.SeedAsync(db);
    }

    private async Task OccupyAsync(WorkspaceExecutionScene scene, bool agent)
    {
        await using var db = _fixture.CreateContext();
        if (agent)
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
        }
        else
        {
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
        }
    }

    [Fact]
    public async Task A_database_at_capacity_refuses_the_claim_before_any_external_work_and_consumes_nothing()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await OccupyAsync(await AddUnrelatedScenarioAsync(), agent: true);
        await OccupyAsync(await AddUnrelatedScenarioAsync(), agent: false);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var captures = 0;
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext,
            new RaceInjectingEvidenceReader(
                new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
                _ =>
                {
                    captures++;
                    return Task.CompletedTask;
                }),
            artifactStore,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GlobalLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_last_place_taken_before_the_commit_seam_refuses_the_claim_and_removes_its_manifest(bool agentOwner)
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await OccupyAsync(await AddUnrelatedScenarioAsync(), agent: false);
        var late = await AddUnrelatedScenarioAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(() => OccupyAsync(late, agentOwner)));
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GlobalLimitReachedCode, Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Fact]
    public async Task An_implementation_claim_and_a_verification_claim_of_other_projects_racing_for_the_last_place_admit_exactly_one()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await OccupyAsync(await AddUnrelatedScenarioAsync(), agent: true);
        var verifying = await AddUnrelatedScenarioAsync();
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var reader = new BarrierEvidenceReader(new Barrier(2));

        async Task<bool> ImplementAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new CreateImplementationAttemptCommandHandler(context, reader, new FakeArtifactStore(), new FixedTimeProvider(Now))
                .HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);
            Assert.True(result.IsSuccess || result.Errors[0].Code == GlobalLimitReachedCode);
            return result.IsSuccess;
        }

        async Task<bool> VerifyAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new ClaimVerificationExecutionCommandHandler(context, reader, new FixedTimeProvider(Now))
                .HandleAsync(
                    new ClaimVerificationExecutionCommand(verifying.Project.Id, verifying.Recipe.Id, verifying.Checkpoint.Id),
                    CancellationToken.None);
            Assert.True(result.IsSuccess || result.Errors[0].Code == GlobalLimitReachedCode);
            return result.IsSuccess;
        }

        var results = await Task.WhenAll(Task.Run(ImplementAsync), Task.Run(VerifyAsync));

        Assert.Single(results, won => won);
        await using var verify = _fixture.CreateContext();
        var mutating = await verify.Attempts.CountAsync(candidate => candidate.AgentRole == AgentRole.Implementer)
            + await verify.VerificationExecutions.CountAsync();
        Assert.Equal(2, mutating);
        if (!results[0])
        {
            await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
            Assert.Equal(1, await verify.Projects.Where(p => p.Id == verifying.Project.Id).Select(p => p.NextVerificationExecutionNumber).SingleAsync() - 1);
        }
        else
        {
            Assert.Equal(1, await verify.Projects.Where(p => p.Id == verifying.Project.Id).Select(p => p.NextVerificationExecutionNumber).SingleAsync());
            Assert.Equal(0, await verify.VerificationExecutions.CountAsync(e => e.ProjectId == verifying.Project.Id));
        }
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_capacity_read_removes_the_prepared_manifest_and_keeps_the_original_failure(FenceReadFault fault)
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CapacityReadFaultInterceptor(fault, cancellation);
        await using var handlerContext = _fixture.CreateContext(interceptor);
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));
        var command = new CreateImplementationAttemptCommand(runId, proposalId);

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

    [Fact]
    public async Task A_malformed_recorded_owner_anywhere_refuses_the_claim_as_unavailable_before_any_external_work()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var foreign = await AddUnrelatedScenarioAsync();
        await OccupyAsync(foreign, agent: true);
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE AgentGitWorkspaceId = {foreign.Workspace.Id}");
        }

        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("execution_capacity.unavailable", Assert.Single(result.Errors).Code);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Fact]
    public async Task ReadOnly_attempts_and_terminal_history_never_consume_a_place_for_the_claim()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var finished = await AddUnrelatedScenarioAsync();
        await OccupyAsync(finished, agent: false);
        await using (var db = _fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.SetExecutionStatusAsync(
                db, await db.VerificationExecutions.Select(e => e.Id).SingleAsync(), VerificationExecutionStatus.Failed);
            foreach (var role in new[] { 1, 2 })
            {
                var scene = await WorkspaceExecutionSeed.SeedAsync(db);
                var attempt = Attempt.ClaimAgent(
                    Guid.NewGuid(), scene.Run.Id, role, scene.Workspace.Id, scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
                    TimeSpan.FromMinutes(10), 1024, 2048, Now, role);
                attempt.MarkAgentDispatched(Now);
                db.Attempts.Add(attempt);
                await db.SaveChangesAsync();
            }
        }

        await OccupyAsync(await AddUnrelatedScenarioAsync(), agent: true);
        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
