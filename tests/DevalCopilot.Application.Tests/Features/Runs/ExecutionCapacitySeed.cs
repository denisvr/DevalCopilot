using DevalCopilot.Application.Tests.Features.Projects;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Places coherent owners of each family on unrelated projects and workspaces of one database, through their own connections,
/// exactly as production rows look, so the global ceiling can be judged without depending on a handler under test.</summary>
internal static class ExecutionCapacitySeed
{
    private static int extraRunNumber = 1000;

    public static async Task<PlacedOwner> AddAsync(SqliteDatabaseFixture fixture, OwnerFamily family)
    {
        await using var db = fixture.CreateContext();
        switch (family)
        {
            case OwnerFamily.Agent:
                {
                    var scene = await WorkspaceExecutionSeed.SeedAsync(db);
                    var id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
                    return new PlacedOwner(family, id, scene.Project.Id, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
                }

            case OwnerFamily.Verification:
                {
                    var scene = await WorkspaceExecutionSeed.SeedAsync(db);
                    var id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
                    return new PlacedOwner(family, id, scene.Project.Id, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
                }

            default:
                {
                    var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, LocalCommitStatus.Prepared, WorkspaceStatus.Committing);
                    return new PlacedOwner(
                        family, rows.Operation.Id, rows.Project.Id, rows.Run.Id, rows.Workspace.Id, rows.Checkpoint.Id);
                }
        }
    }

    public static async Task<IReadOnlyList<PlacedOwner>> AddAsync(SqliteDatabaseFixture fixture, params OwnerFamily[] families)
    {
        var placed = new List<PlacedOwner>(families.Length);
        foreach (var family in families)
        {
            placed.Add(await AddAsync(fixture, family));
        }

        return placed;
    }

    /// <summary>Records a second owner on the workspace of an existing one, as a pre-existing inconsistent database would hold it. The
    /// workspace is made Ready while the row is recorded (the database guards refuse a new owner on a Committing one) and Committing
    /// again afterwards when the first owner or the new one is a reservation.</summary>
    public static async Task<PlacedOwner> AddOntoAsync(SqliteDatabaseFixture fixture, PlacedOwner existing, OwnerFamily family)
    {
        await using var db = fixture.CreateContext();
        var reserved = existing.Family == OwnerFamily.LocalCommit || family == OwnerFamily.LocalCommit;
        await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, existing.WorkspaceId, WorkspaceStatus.Ready);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == existing.ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == existing.WorkspaceId);
        var checkpoint = await db.GitCheckpoints.AsNoTracking().SingleAsync(candidate => candidate.Id == existing.CheckpointId);
        PlacedOwner placed;
        switch (family)
        {
            case OwnerFamily.Agent:
                {
                    var run = await WorkspaceExecutionSeed.AddRunAsync(db, project.Id, Interlocked.Increment(ref extraRunNumber));
                    var id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, run.Id, workspace.Id, checkpoint.Id);
                    placed = new PlacedOwner(family, id, project.Id, run.Id, workspace.Id, checkpoint.Id);
                    break;
                }

            case OwnerFamily.Verification:
                {
                    var recipe = await db.VerificationCommands.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.ProjectId == project.Id);
                    if (recipe is null)
                    {
                        recipe = VerificationCommand.Configure(
                            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true,
                            WorkspaceExecutionSeed.Now);
                        db.VerificationCommands.Add(recipe);
                        await db.SaveChangesAsync();
                    }

                    var number = 1 + (await db.VerificationExecutions.AsNoTracking().Where(candidate => candidate.ProjectId == project.Id)
                        .Select(candidate => (int?)candidate.ExecutionNumber).MaxAsync() ?? 0);
                    var scene = new WorkspaceExecutionScene(project, workspace, checkpoint, recipe, null!, null!);
                    var id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, number);
                    placed = new PlacedOwner(family, id, project.Id, existing.RunId, workspace.Id, checkpoint.Id);
                    break;
                }

            default:
                {
                    var lease = await db.RepositoryMutationLeases.AsNoTracking().SingleAsync(candidate => candidate.WorkspaceId == workspace.Id);
                    // Scenes that wrote their first checkpoint directly never reserved its number, so the counter is advanced past it first.
                    var highest = await db.GitCheckpoints.AsNoTracking().Where(candidate => candidate.WorkspaceId == workspace.Id)
                        .MaxAsync(candidate => (int?)candidate.CheckpointNumber) ?? 0;
                    while (workspace.NextCheckpointNumber <= highest)
                    {
                        workspace.ReserveCheckpointNumber();
                    }

                    var rows = await LocalCommitRowsSeed.AddOperationAsync(
                        db, project, workspace, lease, LocalCommitStatus.Prepared, workspace.SourceCommitSha, new string('f', 40));
                    placed = new PlacedOwner(family, rows.Operation.Id, project.Id, rows.Run.Id, workspace.Id, rows.Checkpoint.Id);
                    break;
                }
        }

        if (reserved)
        {
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, workspace.Id, WorkspaceStatus.Committing);
        }

        return placed;
    }

    /// <summary>Appends the next configuration revision with <paramref name="limit"/> through its own connection, exactly as the explicit
    /// operator command would, so an admission under test sees the persisted limit without depending on that command.</summary>
    public static async Task SetLimitAsync(SqliteDatabaseFixture fixture, int limit)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO execution_capacity_configurations (Revision, \"Limit\") SELECT MAX(Revision) + 1, {limit} FROM execution_capacity_configurations");
    }

    /// <summary>A genuine terminal transition of the owner's own row, which is what frees a place.</summary>
    public static async Task FinishAsync(SqliteDatabaseFixture fixture, PlacedOwner owner)
    {
        await using var db = fixture.CreateContext();
        switch (owner.Family)
        {
            case OwnerFamily.Agent:
                await WorkspaceExecutionSeed.SetAttemptStatusAsync(db, owner.Id, AttemptStatus.Completed);
                break;
            case OwnerFamily.Verification:
                await WorkspaceExecutionSeed.SetExecutionStatusAsync(db, owner.Id, VerificationExecutionStatus.Passed);
                break;
            default:
                await db.LocalCommitOperations.Where(operation => operation.Id == owner.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(operation => operation.Status, LocalCommitStatus.Completed));
                await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, owner.WorkspaceId, WorkspaceStatus.Ready);
                break;
        }
    }
}
