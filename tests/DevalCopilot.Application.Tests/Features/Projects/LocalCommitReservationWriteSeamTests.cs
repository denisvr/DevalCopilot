using DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The write seams of ADR-0029's Committing reservation at the handler level. Each competing writer reads a Ready workspace, and the
/// reservation commits from another connection immediately before that writer's own save. The database guard refuses the write
/// inside the writer's transaction; the handler maps it to the stable <c>workspaces.committing</c> conflict, persists nothing
/// and never turns an unrelated persistence failure into that conflict.
/// </summary>
public sealed class LocalCommitReservationWriteSeamTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(LocalCommitRows Rows, VerificationCommand Recipe)> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(
            db, DevalCopilot.Domain.Features.Runs.LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == rows.Project.Id);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 300, true,
            LocalCommitRowsSeed.Now);
        db.VerificationCommands.Add(recipe);
        await db.SaveChangesAsync();
        return (rows, recipe);
    }

    private FaultInjectingDbContext ReserveBeforeSave(DevalCopilotDbContext inner, Guid workspaceId) => new(inner)
    {
        BeforeSaveChanges = async cancellationToken =>
        {
            await using var competitor = _fixture.CreateContext();
            await competitor.GitWorkspaces.Where(candidate => candidate.Id == workspaceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, WorkspaceStatus.Committing), cancellationToken);
        },
    };

    private async Task AssertNothingPersistedAsync((LocalCommitRows Rows, VerificationCommand Recipe) scene, string recipeName = "Tests")
    {
        await using var db = _fixture.CreateContext();
        Assert.Equal(WorkspaceStatus.Committing, (await db.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == scene.Rows.Workspace.Id)).Status);
        var recipes = await db.VerificationCommands.AsNoTracking().Where(c => c.ProjectId == scene.Rows.Project.Id).ToListAsync();
        Assert.Equal(scene.Recipe.Id, Assert.Single(recipes).Id);
        Assert.Equal(recipeName, recipes[0].Name);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == scene.Rows.Project.Id);
        Assert.Equal(2, project.NextVerificationCommandNumber);
        Assert.Equal(1, await db.GitCheckpoints.AsNoTracking().CountAsync(c => c.WorkspaceId == scene.Rows.Workspace.Id));
    }

    [Fact]
    public async Task A_recipe_configured_while_the_reservation_commits_is_a_committing_conflict_and_persists_nothing()
    {
        var scene = await SeedAsync();
        await using var inner = _fixture.CreateContext();
        var handler = new ConfigureVerificationCommandCommandHandler(
            ReserveBeforeSave(inner, scene.Rows.Workspace.Id), new FixedTimeProvider(LocalCommitRowsSeed.Now));

        var result = await handler.HandleAsync(
            new ConfigureVerificationCommandCommand(scene.Rows.Project.Id, "Late", @"C:\dotnet.exe", ["test"], 300, true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CommitReservation.WorkspaceCommittingCode, Assert.Single(result.Errors).Code);
        await AssertNothingPersistedAsync(scene);
    }

    [Fact]
    public async Task A_recipe_updated_while_the_reservation_commits_is_a_committing_conflict_and_persists_nothing()
    {
        var scene = await SeedAsync();
        await using var inner = _fixture.CreateContext();
        var handler = new UpdateVerificationCommandCommandHandler(
            ReserveBeforeSave(inner, scene.Rows.Workspace.Id), new FixedTimeProvider(LocalCommitRowsSeed.Now));

        var result = await handler.HandleAsync(
            new UpdateVerificationCommandCommand(
                scene.Rows.Project.Id, scene.Recipe.Id, "Renamed", @"C:\dotnet.exe", ["test"], 300, true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CommitReservation.WorkspaceCommittingCode, Assert.Single(result.Errors).Code);
        await AssertNothingPersistedAsync(scene);
    }

    [Fact]
    public async Task A_recipe_deleted_while_the_reservation_commits_is_a_committing_conflict_and_persists_nothing()
    {
        var scene = await SeedAsync();
        await using var inner = _fixture.CreateContext();
        var handler = new DeleteVerificationCommandCommandHandler(ReserveBeforeSave(inner, scene.Rows.Workspace.Id));

        var result = await handler.HandleAsync(
            new DeleteVerificationCommandCommand(scene.Rows.Project.Id, scene.Recipe.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CommitReservation.WorkspaceCommittingCode, Assert.Single(result.Errors).Code);
        await AssertNothingPersistedAsync(scene);
    }

    private sealed class SuccessfulEvidence : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, LocalCommitRowsSeed.SourceCommit, new string('f', 64), [], string.Empty));
    }

    [Fact]
    public async Task A_checkpoint_captured_while_the_reservation_commits_is_a_committing_conflict_and_persists_nothing()
    {
        var scene = await SeedAsync();
        await using var inner = _fixture.CreateContext();
        var handler = new CaptureGitWorkspaceCheckpointCommandHandler(
            ReserveBeforeSave(inner, scene.Rows.Workspace.Id), new SuccessfulEvidence(), new FixedTimeProvider(LocalCommitRowsSeed.Now));

        var result = await handler.HandleAsync(new CaptureGitWorkspaceCheckpointCommand(scene.Rows.Project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CommitReservation.WorkspaceCommittingCode, Assert.Single(result.Errors).Code);
        await AssertNothingPersistedAsync(scene);
    }

    [Fact]
    public async Task An_unrelated_persistence_failure_is_never_reinterpreted_as_a_committing_conflict()
    {
        var scene = await SeedAsync();
        await using var inner = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };
        var handler = new ConfigureVerificationCommandCommandHandler(faulting, new FixedTimeProvider(LocalCommitRowsSeed.Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
            new ConfigureVerificationCommandCommand(scene.Rows.Project.Id, "Late", @"C:\dotnet.exe", ["test"], 300, true), CancellationToken.None));
    }

    [Fact]
    public async Task Writers_for_an_unrelated_project_are_unaffected_by_another_projects_reservation()
    {
        var reserved = await SeedAsync();
        var other = await SeedAsync();
        await using (var db = _fixture.CreateContext())
        {
            await db.GitWorkspaces.Where(w => w.Id == reserved.Rows.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WorkspaceStatus.Committing));
        }

        await using var context = _fixture.CreateContext();
        var handler = new ConfigureVerificationCommandCommandHandler(context, new FixedTimeProvider(LocalCommitRowsSeed.Now));

        var result = await handler.HandleAsync(
            new ConfigureVerificationCommandCommand(other.Rows.Project.Id, "Second", @"C:\dotnet.exe", ["test"], 300, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.SaveChangesAsync();
        await using var verify = _fixture.CreateContext();
        Assert.Equal(2, await verify.VerificationCommands.AsNoTracking().CountAsync(c => c.ProjectId == other.Rows.Project.Id));
    }
}
