using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>ADR-0057: the Claude implementation claim (the ordinary path, which commits through one short explicit transaction opened
/// after every late read) is admitted only into an Idle workspace. A competing owner refuses it before any Git capture or sealing, and
/// an owner that wins in the window between the early check and the write lock refuses it at the commit seam with nothing consumed.</summary>
public sealed partial class CreateImplementationAttemptCommandHandlerTests
{
    [Theory]
    [InlineData(CompetingOwner.Verification)]
    [InlineData(CompetingOwner.AgentOfAnotherRun)]
    public async Task A_competing_workspace_owner_refuses_the_claim_before_any_external_work(CompetingOwner owner)
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner);
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
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        await using var handlerContext = _fixture.CreateContext(new BeforeFirstSaveInterceptor(
            () => WorkspaceExecutionRaceSupport.AddAsync(_fixture, runId, owner)));
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkspaceExecutionRaceSupport.CodeOf(owner), Assert.Single(result.Errors).Code);
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
    }

    [Fact]
    public async Task A_verification_of_the_project_naming_a_workspace_of_another_project_refuses_the_claim_as_unavailable()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await WorkspaceExecutionRaceSupport.AddMisassociatedVerificationAsync(_fixture, runId);
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
        Assert.Equal(WorkspaceExecutionErrors.UnavailableCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, captures);
        Assert.Empty(artifactStore.DeletedSealedFiles);
        await WorkspaceExecutionRaceSupport.AssertUnchangedAsync(_fixture, runId, before);
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
    public async Task An_implementation_claim_and_a_verification_claim_racing_for_one_workspace_leave_exactly_one_owner()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        Guid projectId;
        Guid recipeId;
        Guid checkpointId;
        await using (var seed = _fixture.CreateContext())
        {
            var run = await seed.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
            var project = await seed.Projects.SingleAsync(candidate => candidate.Id == run.ProjectId);
            projectId = project.Id;
            var workspaceId = (await seed.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.ProjectId == projectId)).Id;
            checkpointId = (await seed.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.WorkspaceId == workspaceId)).Id;
            var recipe = VerificationCommand.Configure(
                Guid.NewGuid(), projectId, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
            seed.VerificationCommands.Add(recipe);
            await seed.SaveChangesAsync();
            recipeId = recipe.Id;
        }

        var reader = new BarrierEvidenceReader(new Barrier(2));

        async Task<bool> ImplementAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new CreateImplementationAttemptCommandHandler(context, reader, new FakeArtifactStore(), new FixedTimeProvider(Now))
                .HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);
            return result.IsSuccess;
        }

        async Task<bool> VerifyAsync()
        {
            await using var context = _fixture.CreateContext();
            var result = await new ClaimVerificationExecutionCommandHandler(context, reader, new FixedTimeProvider(Now))
                .HandleAsync(new ClaimVerificationExecutionCommand(projectId, recipeId, checkpointId), CancellationToken.None);
            return result.IsSuccess;
        }

        var results = await Task.WhenAll(Task.Run(ImplementAsync), Task.Run(VerifyAsync));

        Assert.Single(results, won => won);
        await using var verify = _fixture.CreateContext();
        var implementers = await verify.Attempts.CountAsync(a => a.RunId == runId && a.AgentRole == AgentRole.Implementer);
        var executions = await verify.VerificationExecutions.CountAsync();
        Assert.Equal(1, implementers + executions);
        var executionCounter = await verify.Projects.Where(p => p.Id == projectId).Select(p => p.NextVerificationExecutionNumber).SingleAsync();
        Assert.Equal(executions == 1 ? 2 : 1, executionCounter);
        Assert.Equal(implementers, await verify.AttemptInputMessages.CountAsync(i => verify.Attempts.Any(a => a.Id == i.AttemptId && a.AgentRole == AgentRole.Implementer)) > 0 ? 1 : 0);
        Assert.Equal(implementers, await verify.Artifacts.CountAsync(a => a.RunId == runId && a.Purpose == ArtifactPurpose.AgentContextManifest
            && verify.Attempts.Any(attempt => attempt.Id == a.AttemptId && attempt.AgentRole == AgentRole.Implementer)));
    }

    [Fact]
    public async Task A_competing_owner_in_another_repository_does_not_delay_or_refuse_the_claim()
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        await using (var other = _fixture.CreateContext())
        {
            var unrelated = await Projects.WorkspaceExecutionSeed.SeedAsync(other);
            await Projects.WorkspaceExecutionSeed.AddVerificationExecutionAsync(other, unrelated);
        }

        await using var handlerContext = _fixture.CreateContext();
        var handler = new CreateImplementationAttemptCommandHandler(
            handlerContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateImplementationAttemptCommand(runId, proposalId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Attempts.Where(attempt => attempt.RunId == runId && attempt.AgentRole == AgentRole.Implementer).ToListAsync());
    }

    [Theory]
    [InlineData(FenceReadFault.Cancel)]
    [InlineData(FenceReadFault.DatabaseFailure)]
    public async Task A_failed_or_cancelled_final_workspace_read_removes_the_prepared_manifest_and_keeps_the_original_failure(FenceReadFault fault)
    {
        var (runId, proposalId) = await SeedClaudeModelScenarioAsync(initialAlias: null);
        var before = await WorkspaceExecutionRaceSupport.TakeAsync(_fixture, runId);
        var artifactStore = new FakeArtifactStore();
        using var cancellation = new CancellationTokenSource();
        var interceptor = new FenceReadFaultInterceptor(fault, cancellation);
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
}
