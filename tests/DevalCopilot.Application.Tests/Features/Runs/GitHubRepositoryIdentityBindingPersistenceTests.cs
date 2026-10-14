using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0043 storage on a real file-backed SQLite database with the production migrations: the binding round-trips exactly, one target, run,
/// operation and event can each hold at most one binding, a node identifier is deliberately not unique, every reference is enforced and
/// restrictive, a delete that would silently remove the record or what it names is refused, and a rolled-back transaction leaves nothing.
/// </summary>
public sealed class GitHubRepositoryIdentityBindingPersistenceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(LocalCommitRows Rows, GitHubObservationTarget Target)> SeedWithTargetAsync()
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        db.Events.Add(RunEvent.Record(
            target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
            GitHubObservationTargetEventPayload.Build(target), LocalCommitRowsSeed.Now));
        await db.SaveChangesAsync();
        return (rows, target);
    }

    private static GitHubRepositoryIdentityBinding BindingFor(
        LocalCommitRows rows, GitHubObservationTarget target, Guid? targetId = null, Guid? runId = null, Guid? projectId = null,
        Guid? operationId = null, Guid? workspaceId = null, Guid? eventId = null, string node = "R_kgDOExample01") =>
        GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(),
            runId ?? rows.Run.Id,
            projectId ?? rows.Project.Id,
            operationId ?? rows.Operation.Id,
            workspaceId ?? rows.Workspace.Id,
            targetId ?? target.Id,
            rows.Operation.CommitSha,
            rows.Operation.BranchName,
            node,
            "octo-cat",
            "hello.world",
            LocalCommitRowsSeed.Now,
            LocalCommitRowsSeed.Now.AddSeconds(1),
            LocalCommitRowsSeed.Now.AddSeconds(2),
            eventId ?? Guid.NewGuid());

    private static RunEvent EventFor(Guid runId, GitHubRepositoryIdentityBinding binding) =>
        RunEvent.Record(
            binding.EventId, runId, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
            GitHubRepositoryIdentityBindingEventPayload.Build(binding), binding.RecordedAtUtc);

    [Fact]
    public async Task A_binding_and_its_event_round_trip_exactly()
    {
        var (rows, target) = await SeedWithTargetAsync();
        var binding = BindingFor(rows, target);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubRepositoryIdentityBindings.Add(binding);
            db.Events.Add(EventFor(rows.Run.Id, binding));
            await db.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.GitHubRepositoryIdentityBindings.AsNoTracking().SingleAsync();
        Assert.Equal(binding.Id, stored.Id);
        Assert.Equal(target.Id, stored.ObservationTargetId);
        Assert.Equal(rows.Operation.CommitSha, stored.CommitSha);
        Assert.Equal("R_kgDOExample01", stored.RepositoryNodeId);
        Assert.Equal(binding.ObservedAtUtc, stored.ObservedAtUtc);
        Assert.Equal(binding.RecordedAtUtc, stored.RecordedAtUtc);
        var runEvent = await read.Events.AsNoTracking().SingleAsync(candidate => candidate.Id == binding.EventId);
        Assert.Equal(GitHubRepositoryIdentityBindingEventPayload.Build(stored), runEvent.PayloadJson);
    }

    [Fact]
    public async Task A_second_binding_for_the_same_target_run_operation_or_event_is_refused_and_leaves_the_first_alone()
    {
        var (rows, target) = await SeedWithTargetAsync();
        var first = BindingFor(rows, target);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubRepositoryIdentityBindings.Add(first);
            await db.SaveChangesAsync();
        }

        // Each duplicate shares exactly one unique key with the first; the other references are valid, so only that index can refuse it.
        foreach (var duplicate in new[]
                 {
                     BindingFor(rows, target),
                     BindingFor(rows, target, eventId: first.EventId),
                 })
        {
            await using var db = _fixture.CreateContext();
            db.GitHubRepositoryIdentityBindings.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Equal(first.Id, (await read.GitHubRepositoryIdentityBindings.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task The_four_unique_indexes_exist_and_no_unique_index_covers_the_node_identifier()
    {
        await SeedWithTargetAsync();
        await using var db = _fixture.CreateContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var unique = new Dictionary<string, string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM pragma_index_list('github_repository_identity_bindings') WHERE \"unique\" = 1 AND origin = 'c'";
            await using var reader = await list.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            await reader.CloseAsync();
            foreach (var name in names)
            {
                await using var info = connection.CreateCommand();
#pragma warning disable CA2100 // Index names come from the database catalog.
                info.CommandText = $"SELECT group_concat(name) FROM pragma_index_info('{name}')";
#pragma warning restore CA2100
                unique[name] = (string)(await info.ExecuteScalarAsync())!;
            }
        }

        Assert.Equal(
            ["EventId", "LocalCommitOperationId", "ObservationTargetId", "RunId"],
            unique.Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_reference_must_exist()
    {
        var (rows, target) = await SeedWithTargetAsync();
        foreach (var binding in new[]
                 {
                     BindingFor(rows, target, targetId: Guid.NewGuid()),
                     BindingFor(rows, target, runId: Guid.NewGuid()),
                     BindingFor(rows, target, projectId: Guid.NewGuid()),
                     BindingFor(rows, target, operationId: Guid.NewGuid()),
                     BindingFor(rows, target, workspaceId: Guid.NewGuid()),
                 })
        {
            await using var db = _fixture.CreateContext();
            db.GitHubRepositoryIdentityBindings.Add(binding);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubRepositoryIdentityBindings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task No_delete_of_a_referenced_target_run_project_workspace_or_operation_can_remove_the_binding_or_its_event()
    {
        var (rows, target) = await SeedWithTargetAsync();
        var binding = BindingFor(rows, target);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubRepositoryIdentityBindings.Add(binding);
            db.Events.Add(EventFor(rows.Run.Id, binding));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.GitHubObservationTargets.Where(candidate => candidate.Id == target.Id).ExecuteDeleteAsync());
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
        Assert.Equal(binding.Id, (await read.GitHubRepositoryIdentityBindings.AsNoTracking().SingleAsync()).Id);
        Assert.True(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.Id == binding.EventId));
        Assert.True(await read.GitHubObservationTargets.AsNoTracking().AnyAsync(candidate => candidate.Id == target.Id));
    }

    [Fact]
    public async Task A_transaction_that_is_rolled_back_leaves_neither_the_binding_nor_the_event()
    {
        var (rows, target) = await SeedWithTargetAsync();
        var binding = BindingFor(rows, target);
        await using (var db = _fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.GitHubRepositoryIdentityBindings.Add(binding);
            db.Events.Add(EventFor(rows.Run.Id, binding));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubRepositoryIdentityBindings.AsNoTracking().ToListAsync());
        Assert.False(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.EventType == RunEventType.GitHubRepositoryIdentityBound));
    }

    [Fact]
    public async Task A_failed_save_of_the_event_rolls_back_the_binding_with_it()
    {
        var (rows, target) = await SeedWithTargetAsync();
        var binding = BindingFor(rows, target);
        await using (var db = _fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.GitHubRepositoryIdentityBindings.Add(binding);
            var existingEventId = target.EventId;
            db.Events.Add(RunEvent.Record(
                existingEventId, rows.Run.Id, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
                GitHubRepositoryIdentityBindingEventPayload.Build(binding), binding.RecordedAtUtc));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubRepositoryIdentityBindings.AsNoTracking().ToListAsync());
    }
}
