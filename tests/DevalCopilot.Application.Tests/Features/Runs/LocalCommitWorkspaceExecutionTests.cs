using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0057 beside ADR-0029's local commit. The shared authority reader (used by admission, the locked reservation seam, the execution
/// seam and the status) requires an Idle workspace to admit a commit and the commit's own reservation to be the workspace's only owner
/// to execute it: a commit may execute its own Committing reservation but never ignore another Agent attempt, a verification
/// execution, or ownership it cannot trust. Its approval identity and every other gate are read after, and unchanged by, this check.
/// </summary>
public sealed class LocalCommitWorkspaceExecutionTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<LocalCommitRows> SeedAsync(LocalCommitStatus operation, WorkspaceStatus workspace)
    {
        await using var db = _fixture.CreateContext();
        var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, operation, workspace);
        await RunExecutionModeTestSupport.SetStoredModeAsync(db, rows.Run.Id, AbandonmentRaceSupport.ManualAgentMode);
        return rows;
    }

    private async Task<string?> RefusalAsync(LocalCommitRows rows, WorkspaceStatus required, Guid? ownOperationId = null)
    {
        await using var db = _fixture.CreateContext();
        var read = await new LocalCommitAuthorityReader(db).ReadAsync(
            rows.Run.Id, new LocalCommitAuthorityReader.Selection(null, null, null), required, ownOperationId, CancellationToken.None);
        return read.Error?.Code;
    }

    private async Task PlantAsync(LocalCommitRows rows, string owner)
    {
        await using var db = _fixture.CreateContext();
        var scene = new WorkspaceExecutionScene(rows.Project, rows.Workspace, rows.Checkpoint, null!, rows.Run, rows.Lease);
        if (owner == "verification")
        {
            var recipe = VerificationCommand.Configure(
                Guid.NewGuid(), rows.Project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, LocalCommitRowsSeed.Now);
            db.VerificationCommands.Add(recipe);
            await db.SaveChangesAsync();
            db.VerificationExecutions.Add(VerificationExecution.Claim(
                Guid.NewGuid(), rows.Project.Id, 1, rows.Workspace, rows.Checkpoint, recipe, LocalCommitRowsSeed.Now));
            await db.SaveChangesAsync();
            return;
        }

        var other = Run.RecordIntent(Guid.NewGuid(), rows.Project.Id, 50, "Another run", LocalCommitRowsSeed.Now);
        other.Claim(LocalCommitRowsSeed.Now);
        db.Runs.Add(other);
        await db.SaveChangesAsync();
        var attemptId = await WorkspaceExecutionSeed.AddAgentAttemptAsync(
            db, other.Id, scene.Workspace.Id, scene.Checkpoint.Id, dispatched: owner != "undispatched agent attempt");
        if (owner == "agent attempt without a workspace")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = {attemptId}");
        }
    }

    [Theory]
    [InlineData("verification")]
    [InlineData("agent attempt")]
    [InlineData("undispatched agent attempt")]
    [InlineData("agent attempt without a workspace")]
    public async Task Admission_and_status_refuse_a_Ready_workspace_that_another_owner_holds(string owner)
    {
        var rows = await SeedAsync(LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        Assert.NotEqual(LocalCommitErrors.ActiveWorkCode, await RefusalAsync(rows, WorkspaceStatus.Ready, rows.Operation.Id));

        await PlantAsync(rows, owner);

        Assert.Equal(LocalCommitErrors.ActiveWorkCode, await RefusalAsync(rows, WorkspaceStatus.Ready, rows.Operation.Id));
    }

    [Fact]
    public async Task The_execution_seam_accepts_its_own_reservation_when_it_is_the_workspaces_only_owner()
    {
        var rows = await SeedAsync(LocalCommitStatus.Executing, WorkspaceStatus.Committing);

        Assert.NotEqual(LocalCommitErrors.ActiveWorkCode, await RefusalAsync(rows, WorkspaceStatus.Committing, rows.Operation.Id));
    }

    [Theory]
    [InlineData("verification")]
    [InlineData("agent attempt")]
    [InlineData("agent attempt without a workspace")]
    public async Task The_execution_seam_refuses_to_run_beside_an_owner_that_predates_the_reservation(string owner)
    {
        // The database guards of ADR-0029 refuse a new Agent attempt or verification execution once the workspace is Committing, so the
        // conflicting owner is recorded first, exactly like a pre-existing inconsistent database, and the reservation is applied after.
        var rows = await SeedAsync(LocalCommitStatus.Executing, WorkspaceStatus.Ready);
        await PlantAsync(rows, owner);
        await using (var db = _fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, rows.Workspace.Id, WorkspaceStatus.Committing);
        }

        Assert.Equal(LocalCommitErrors.ActiveWorkCode, await RefusalAsync(rows, WorkspaceStatus.Committing, rows.Operation.Id));
    }

    [Fact]
    public async Task A_reservation_without_an_owner_is_not_admissible_as_a_new_commit()
    {
        var rows = await SeedAsync(LocalCommitStatus.Executing, WorkspaceStatus.Committing);

        // Admission requires a Ready workspace, so a reserved one is a different refusal, never ActiveWork and never an admission.
        Assert.NotNull(await RefusalAsync(rows, WorkspaceStatus.Ready, rows.Operation.Id));
    }

    [Fact]
    public async Task A_terminal_owner_or_an_owner_of_another_repository_does_not_refuse_the_commit()
    {
        var rows = await SeedAsync(LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        await using (var db = _fixture.CreateContext())
        {
            var other = Run.RecordIntent(Guid.NewGuid(), rows.Project.Id, 60, "Finished", LocalCommitRowsSeed.Now);
            other.Claim(LocalCommitRowsSeed.Now);
            db.Runs.Add(other);
            await db.SaveChangesAsync();
            var finished = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, other.Id, rows.Workspace.Id, rows.Checkpoint.Id);
            await WorkspaceExecutionSeed.SetAttemptStatusAsync(db, finished, AttemptStatus.Interrupted);
            var unrelated = await WorkspaceExecutionSeed.SeedAsync(db);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, unrelated.Run.Id, unrelated.Workspace.Id, unrelated.Checkpoint.Id);
        }

        Assert.NotEqual(LocalCommitErrors.ActiveWorkCode, await RefusalAsync(rows, WorkspaceStatus.Ready, rows.Operation.Id));
    }
}
