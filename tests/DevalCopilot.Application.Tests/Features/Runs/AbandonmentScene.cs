using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>One project with one manual run and, optionally, the workspace rows a started run would own, seeded through the model into
/// a real file-backed SQLite database. Variants that the model cannot express (an unrecognized stored status, an incoherent
/// abandonment) are written with test-owned SQL through an independent context, exactly as another process would.</summary>
internal sealed class AbandonmentScene
{
    public static readonly DateTimeOffset Created = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private AbandonmentScene(SqliteDatabaseFixture fixture, Guid projectId, Guid runId)
    {
        Fixture = fixture;
        ProjectId = projectId;
        RunId = runId;
    }

    public SqliteDatabaseFixture Fixture { get; }

    public Guid ProjectId { get; }

    public Guid RunId { get; }

    public Guid? WorkspaceId { get; private set; }

    public Guid? CheckpointId { get; private set; }

    public Guid? CommandId { get; private set; }

    public static async Task<AbandonmentScene> CreateAsync(
        SqliteDatabaseFixture fixture, bool running = false, bool withWorkspace = false, RunExecutionMode mode = RunExecutionMode.ManualAgent)
    {
        await using var db = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Created);
        var number = project.ReserveExecutionNumber();
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, number, "Plan the increment", Created)
            : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, number, mode, "Plan the increment", Created);
        if (running)
        {
            run.Claim(Created.AddSeconds(10));
        }

        db.Projects.Add(project);
        db.Runs.Add(run);
        db.Events.Add(RunEvent.Record(
            Guid.NewGuid(), run.Id, null, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), "{\"objective\":\"Plan the increment\"}", Created));
        await db.SaveChangesAsync();

        var scene = new AbandonmentScene(fixture, project.Id, run.Id);
        if (withWorkspace)
        {
            await scene.AddWorkspaceAsync();
        }

        return scene;
    }

    public async Task AddWorkspaceAsync()
    {
        await using var db = Fixture.CreateContext();
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == ProjectId);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), ProjectId, project.ReserveWorkspaceNumber(), $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Created);
        workspace.MarkReady();
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), ProjectId, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Created);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Created, new string('a', 40), new string('b', 64), []);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), ProjectId, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 300, true, Created);
        db.GitWorkspaces.Add(workspace);
        db.RepositoryMutationLeases.Add(lease);
        db.GitCheckpoints.Add(checkpoint);
        db.VerificationCommands.Add(command);
        await db.SaveChangesAsync();
        WorkspaceId = workspace.Id;
        CheckpointId = checkpoint.Id;
        CommandId = command.Id;
    }

    /// <summary>A simulated attempt of this run: Running unless <paramref name="finish"/> ends it. Its kind is irrelevant to the check.</summary>
    public async Task<Guid> AddAttemptAsync(Action<Attempt>? finish = null)
    {
        await using var db = Fixture.CreateContext();
        var number = await db.Attempts.CountAsync(candidate => candidate.RunId == RunId) + 1;
        var attempt = Attempt.Claim(Guid.NewGuid(), RunId, number, Created);
        finish?.Invoke(attempt);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    public async Task<Guid> AddVerificationAsync(Action<VerificationExecution>? finish = null)
    {
        await using var db = Fixture.CreateContext();
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == WorkspaceId);
        var checkpoint = await db.GitCheckpoints.SingleAsync(candidate => candidate.Id == CheckpointId);
        var command = await db.VerificationCommands.SingleAsync(candidate => candidate.Id == CommandId);
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), ProjectId, project.ReserveVerificationExecutionNumber(), workspace, checkpoint, command, Created);
        finish?.Invoke(execution);
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
        return execution.Id;
    }

    /// <summary>An operation of this project in the given status, on the scene's workspace. It belongs to a second run of the same
    /// project so this scene's own run keeps its identity and lifecycle.</summary>
    public async Task<Guid> AddLocalCommitOperationAsync(LocalCommitStatus status)
    {
        await using var db = Fixture.CreateContext();
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == ProjectId);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == WorkspaceId);
        var lease = await db.RepositoryMutationLeases.SingleAsync(candidate => candidate.WorkspaceId == WorkspaceId);
        var rows = await LocalCommitRowsSeed.AddOperationAsync(
            db, project, workspace, lease, status, new string('a', 40), new string('d', 40));

        // The seed's reviewing attempt is Running; only the operation's own status is the fact this scene varies.
        await db.Database.ExecuteSqlRawAsync("UPDATE attempts SET Status = 'Completed' WHERE RunId = {0}", rows.Run.Id);
        return rows.Operation.Id;
    }

    public async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var db = Fixture.CreateContext();
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(sql, parameters);
#pragma warning restore EF1002
    }

    public Task SetRunModeAsync(string literal) => RunExecutionModeTestSupport.SetStoredRawAsync(Fixture, RunId, literal);

    /// <summary>Every durable fact a refused or rolled-back abandonment must leave exactly as it found it.</summary>
    public async Task<AbandonmentSnapshot> SnapshotAsync()
    {
        await using var db = Fixture.CreateContext();
        var lifecycle = await db.Database.SqlQuery<string>($"SELECT Lifecycle AS Value FROM runs WHERE Id = {RunId}").SingleAsync();
        var stage = await db.Database.SqlQuery<string>($"SELECT Stage AS Value FROM runs WHERE Id = {RunId}").SingleAsync();
        var run = await db.Runs.AsNoTracking().Where(candidate => candidate.Id == RunId).Select(candidate => new
        {
            candidate.AccumulatedAutonomousSeconds,
            candidate.LastAdvancedAtUtc,
            candidate.AbandonmentReason,
            candidate.AbandonedAtUtc,
            candidate.ActiveParticipantKind,
        }).SingleAsync();
        var runIds = await db.Runs.AsNoTracking().Where(candidate => candidate.ProjectId == ProjectId).Select(candidate => candidate.Id).ToListAsync();
        return new AbandonmentSnapshot(
            lifecycle,
            stage,
            run.AccumulatedAutonomousSeconds,
            run.LastAdvancedAtUtc,
            run.AbandonmentReason,
            run.AbandonedAtUtc,
            run.ActiveParticipantKind,
            await db.Events.AsNoTracking().CountAsync(candidate => runIds.Contains(candidate.RunId)),
            await db.Events.AsNoTracking().CountAsync(candidate => runIds.Contains(candidate.RunId) && candidate.EventType == RunEventType.RunAbandoned),
            await db.Attempts.AsNoTracking().CountAsync(candidate => runIds.Contains(candidate.RunId)),
            await db.GitWorkspaces.AsNoTracking().CountAsync(candidate => candidate.ProjectId == ProjectId),
            await db.Runs.AsNoTracking().CountAsync(candidate => candidate.ProjectId == ProjectId),
            await db.Projects.AsNoTracking().Where(candidate => candidate.Id == ProjectId).Select(candidate => candidate.NextExecutionNumber).SingleAsync());
    }
}
