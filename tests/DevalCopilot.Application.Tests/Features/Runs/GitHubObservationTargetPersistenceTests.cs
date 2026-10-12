using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0040 storage on a real file-backed SQLite database with the production migrations: the target round-trips exactly, one run, one
/// operation and one event can each hold at most one target, every reference is enforced and restrictive, and a delete that would
/// silently remove the record (or the run, project, workspace or operation it names) is refused with everything left in place.
/// </summary>
public sealed class GitHubObservationTargetPersistenceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static GitHubObservationTarget TargetFor(
        LocalCommitRows rows, Guid? eventId = null, Guid? runId = null, Guid? projectId = null, Guid? operationId = null,
        Guid? workspaceId = null) =>
        GitHubObservationTarget.Record(
            Guid.NewGuid(),
            runId ?? rows.Run.Id,
            projectId ?? rows.Project.Id,
            operationId ?? rows.Operation.Id,
            workspaceId ?? rows.Workspace.Id,
            rows.Operation.CommitSha,
            rows.Operation.BranchName,
            "octo-cat",
            "hello.world",
            LocalCommitRowsSeed.Now,
            eventId ?? Guid.NewGuid());

    private async Task<LocalCommitRows> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        return await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
    }

    [Fact]
    public async Task A_target_and_its_event_round_trip_exactly()
    {
        var rows = await SeedAsync();
        var target = TargetFor(rows);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubObservationTargets.Add(target);
            db.Events.Add(RunEvent.Record(
                target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
                GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
            await db.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.GitHubObservationTargets.AsNoTracking().SingleAsync();
        Assert.Equal(target.Id, stored.Id);
        Assert.Equal(rows.Run.Id, stored.RunId);
        Assert.Equal(rows.Project.Id, stored.ProjectId);
        Assert.Equal(rows.Operation.Id, stored.LocalCommitOperationId);
        Assert.Equal(rows.Workspace.Id, stored.GitWorkspaceId);
        Assert.Equal(rows.Operation.CommitSha, stored.CommitSha);
        Assert.Equal(rows.Operation.BranchName, stored.BranchName);
        Assert.Equal("octo-cat", stored.Owner);
        Assert.Equal("hello.world", stored.Repository);
        Assert.Equal(LocalCommitRowsSeed.Now, stored.RecordedAtUtc);
        Assert.Equal(target.EventId, stored.EventId);
        var runEvent = await read.Events.AsNoTracking().SingleAsync(candidate => candidate.Id == target.EventId);
        Assert.Equal(GitHubObservationTargetEventPayload.Build(stored), runEvent.PayloadJson);
    }

    [Fact]
    public async Task A_second_target_for_the_same_run_operation_or_event_is_refused_and_leaves_the_first_alone()
    {
        var rows = await SeedAsync();
        var first = TargetFor(rows);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubObservationTargets.Add(first);
            await db.SaveChangesAsync();
        }

        var otherRun = await OtherCompletedRowsAsync(rows);
        foreach (var duplicate in new[]
                 {
                     TargetFor(rows),
                     TargetFor(otherRun, runId: rows.Run.Id),
                     TargetFor(otherRun, operationId: rows.Operation.Id),
                     TargetFor(otherRun, eventId: first.EventId),
                 })
        {
            await using var db = _fixture.CreateContext();
            db.GitHubObservationTargets.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Equal(first.Id, (await read.GitHubObservationTargets.AsNoTracking().SingleAsync()).Id);
    }

    private async Task<LocalCommitRows> OtherCompletedRowsAsync(LocalCommitRows rows)
    {
        await using var db = _fixture.CreateContext();
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == rows.Project.Id);
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == rows.Workspace.Id);
        var lease = await db.RepositoryMutationLeases.SingleAsync(candidate => candidate.Id == rows.Lease.Id);
        return await LocalCommitRowsSeed.AddOperationAsync(
            db, project, workspace, lease, LocalCommitStatus.Completed, LocalCommitRowsSeed.SourceCommit, new string('b', 40));
    }

    [Fact]
    public async Task Every_reference_must_exist()
    {
        var rows = await SeedAsync();
        foreach (var target in new[]
                 {
                     TargetFor(rows, runId: Guid.NewGuid()),
                     TargetFor(rows, projectId: Guid.NewGuid()),
                     TargetFor(rows, operationId: Guid.NewGuid()),
                     TargetFor(rows, workspaceId: Guid.NewGuid()),
                 })
        {
            await using var db = _fixture.CreateContext();
            db.GitHubObservationTargets.Add(target);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubObservationTargets.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task No_delete_of_a_referenced_run_project_workspace_or_operation_can_remove_the_target_or_its_event()
    {
        var rows = await SeedAsync();
        var target = TargetFor(rows);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubObservationTargets.Add(target);
            db.Events.Add(RunEvent.Record(
                target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
                GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.Runs.Where(candidate => candidate.Id == rows.Run.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.Projects.Where(candidate => candidate.Id == rows.Project.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.GitWorkspaces.Where(candidate => candidate.Id == rows.Workspace.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.LocalCommitOperations.Where(candidate => candidate.Id == rows.Operation.Id).ExecuteDeleteAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Equal(target.Id, (await read.GitHubObservationTargets.AsNoTracking().SingleAsync()).Id);
        Assert.True(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.Id == target.EventId));
        Assert.True(await read.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == rows.Run.Id));
        Assert.True(await read.Projects.AsNoTracking().AnyAsync(candidate => candidate.Id == rows.Project.Id));
        Assert.True(await read.GitHubObservationTargets.AsNoTracking().AnyAsync());
    }

    [Fact]
    public async Task A_transaction_that_is_rolled_back_leaves_neither_the_target_nor_the_event()
    {
        var rows = await SeedAsync();
        var target = TargetFor(rows);
        await using (var db = _fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.GitHubObservationTargets.Add(target);
            db.Events.Add(RunEvent.Record(
                target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
                GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubObservationTargets.AsNoTracking().ToListAsync());
        Assert.False(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.EventType == RunEventType.GitHubObservationTargetRecorded));
    }
}
