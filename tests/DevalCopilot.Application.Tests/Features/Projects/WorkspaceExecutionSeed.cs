using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>Seeds the existing rows that own a workspace's execution: Agent attempts, verification executions and, through
/// <c>LocalCommitRowsSeed</c>, local-commit operations. Rows are written exactly as the domain factories produce them, then moved to the
/// requested state with a direct update, so a test never depends on a handler whose behavior it is judging.</summary>
internal static class WorkspaceExecutionSeed
{
    public static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    public static readonly string Fingerprint = new('a', 64);

    public static async Task<WorkspaceExecutionScene> SeedAsync(DevalCopilotDbContext db)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-1", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Objective", Now);
        run.Claim(Now);

        db.Projects.Add(project);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(lease);
        db.VerificationCommands.Add(recipe);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return new WorkspaceExecutionScene(project, workspace, checkpoint, recipe, run, lease);
    }

    /// <summary>Another claimed run of the same project, as a second operator action would create.</summary>
    public static async Task<Run> AddRunAsync(DevalCopilotDbContext db, Guid projectId, int number)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), projectId, number, $"Objective {number}", Now);
        run.Claim(Now);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    /// <summary>A Running Agent attempt claimed on the exact workspace of a run, optionally dispatched, with an explicit attempt id so
    /// a test can exclude it exactly.</summary>
    public static async Task<Guid> AddAgentAttemptAsync(
        DevalCopilotDbContext db,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId,
        bool dispatched = true,
        Guid? attemptId = null,
        int attemptNumber = 1)
    {
        var id = attemptId ?? Guid.NewGuid();
        var attempt = Attempt.ClaimAgentImplementation(
            id, runId, attemptNumber, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048, Now,
            attemptNumber);
        if (dispatched)
        {
            attempt.MarkAgentDispatched(Now);
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(CancellationToken.None);
        return id;
    }

    /// <summary>A Running verification execution claimed on the scene's workspace. The recipe may be edited or deleted afterwards
    /// without changing the execution, so the execution is the owner regardless of its recipe.</summary>
    public static async Task<Guid> AddVerificationExecutionAsync(
        DevalCopilotDbContext db, WorkspaceExecutionScene scene, int number = 1, Guid? executionId = null, bool dispatched = true)
    {
        var id = executionId ?? Guid.NewGuid();
        var execution = VerificationExecution.Claim(id, scene.Project.Id, number, scene.Workspace, scene.Checkpoint, scene.Recipe, Now);
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync(CancellationToken.None);
        if (dispatched)
        {
            await db.VerificationExecutions.Where(candidate => candidate.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.DispatchedAtUtc, Now));
        }

        return id;
    }

    /// <summary>The minimal local-commit rows of <c>LocalCommitRowsSeed</c> with the approving review attempt it leaves Running recorded
    /// as the Completed attempt it is in production, so the only owner left on the workspace is the reservation itself.</summary>
    public static async Task<LocalCommitRows> SeedLocalCommitAsync(DevalCopilotDbContext db, LocalCommitStatus operation, WorkspaceStatus workspace)
    {
        var rows = await LocalCommitRowsSeed.SeedAsync(db, operation, workspace);
        await db.Attempts.Where(candidate => candidate.RunId == rows.Run.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, AttemptStatus.Completed));
        return rows;
    }

    public static Task SetWorkspaceStatusAsync(DevalCopilotDbContext db, Guid workspaceId, WorkspaceStatus status) =>
        db.GitWorkspaces.Where(candidate => candidate.Id == workspaceId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, status));

    public static Task SetAttemptStatusAsync(DevalCopilotDbContext db, Guid attemptId, AttemptStatus status) =>
        db.Attempts.Where(candidate => candidate.Id == attemptId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, status));

    public static Task SetExecutionStatusAsync(DevalCopilotDbContext db, Guid executionId, VerificationExecutionStatus status) =>
        db.VerificationExecutions.Where(candidate => candidate.Id == executionId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, status));
}
