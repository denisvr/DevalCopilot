using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0048 storage on a real file-backed SQLite database with the production migrations: the approval round-trips exactly, one run,
/// operation, target, binding and event can each hold at most one approval, every reference is enforced and restrictive, no update or
/// delete of a recorded approval is possible, a delete that would silently remove what it names is refused, and a rolled-back transaction
/// leaves nothing.
/// </summary>
public sealed class GitHubBranchPublicationApprovalPersistenceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Seeded(
        LocalCommitRows Rows, GitHubObservationTarget Target, GitHubRepositoryIdentityBinding Binding);

    private async Task<Seeded> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        var binding = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, target.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "R_kgDOExample01", "octo-cat", "hello.world", LocalCommitRowsSeed.Now,
            LocalCommitRowsSeed.Now.AddSeconds(1), LocalCommitRowsSeed.Now.AddSeconds(2), Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        db.GitHubRepositoryIdentityBindings.Add(binding);
        await db.SaveChangesAsync();
        return new Seeded(rows, target, binding);
    }

    private static GitHubBranchPublicationApproval ApprovalFor(
        Seeded seeded, Guid? runId = null, Guid? projectId = null, Guid? operationId = null, Guid? workspaceId = null,
        Guid? targetId = null, Guid? bindingId = null, Guid? eventId = null, Guid? id = null) =>
        GitHubBranchPublicationApproval.Record(
            id ?? Guid.NewGuid(),
            eventId ?? Guid.NewGuid(),
            runId ?? seeded.Rows.Run.Id,
            projectId ?? seeded.Rows.Project.Id,
            operationId ?? seeded.Rows.Operation.Id,
            workspaceId ?? seeded.Rows.Workspace.Id,
            seeded.Rows.Operation.CommitSha,
            seeded.Rows.Operation.BranchName,
            targetId ?? seeded.Target.Id,
            seeded.Target.EventId,
            seeded.Target.Owner,
            seeded.Target.Repository,
            seeded.Target.RecordedAtUtc,
            bindingId ?? seeded.Binding.Id,
            seeded.Binding.EventId,
            seeded.Binding.RepositoryNodeId,
            seeded.Binding.Owner,
            seeded.Binding.Repository,
            seeded.Binding.ObservedAtUtc,
            seeded.Binding.RecordedAtUtc,
            seeded.Binding.RecordedAtUtc.AddSeconds(1));

    private static RunEvent EventFor(Guid runId, GitHubBranchPublicationApproval approval) =>
        RunEvent.Record(
            approval.EventId, runId, null, RunEventType.GitHubBranchPublicationApproved, ParticipantIdentity.ForHuman(),
            GitHubBranchPublicationApprovalEventPayload.Build(approval), approval.ApprovedAtUtc);

    [Fact]
    public async Task An_approval_and_its_event_round_trip_exactly()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Add(approval);
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
            await db.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.GitHubBranchPublicationApprovals.AsNoTracking().SingleAsync();
        Assert.Equal(approval.Id, stored.Id);
        Assert.Equal(approval.EventId, stored.EventId);
        Assert.Equal(seeded.Target.Id, stored.TargetId);
        Assert.Equal(seeded.Target.EventId, stored.TargetEventId);
        Assert.Equal(seeded.Binding.Id, stored.BindingId);
        Assert.Equal(seeded.Binding.EventId, stored.BindingEventId);
        Assert.Equal(seeded.Rows.Operation.CommitSha, stored.CommitSha);
        Assert.Equal("R_kgDOExample01", stored.RepositoryNodeId);
        Assert.Equal(approval.TargetRecordedAtUtc, stored.TargetRecordedAtUtc);
        Assert.Equal(approval.BindingObservedAtUtc, stored.BindingObservedAtUtc);
        Assert.Equal(approval.BindingRecordedAtUtc, stored.BindingRecordedAtUtc);
        Assert.Equal(approval.ApprovedAtUtc, stored.ApprovedAtUtc);
        var runEvent = await read.Events.AsNoTracking().SingleAsync(candidate => candidate.Id == approval.EventId);
        Assert.Equal(GitHubBranchPublicationApprovalEventPayload.Build(stored), runEvent.PayloadJson);
    }

    [Fact]
    public async Task A_second_approval_for_the_same_run_operation_target_binding_or_event_is_refused_and_leaves_the_first_alone()
    {
        var seeded = await SeedAsync();
        var first = ApprovalFor(seeded);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Add(first);
            await db.SaveChangesAsync();
        }

        // Each duplicate shares exactly one unique key with the first (the primary key included); the other references are valid, so only
        // that index can refuse it. The extra rows of a second target and binding keep the other unique keys free where they must be.
        var duplicates = new[]
        {
            ApprovalFor(seeded, id: first.Id, eventId: Guid.NewGuid()),
            ApprovalFor(seeded, eventId: first.EventId),
            ApprovalFor(seeded),
        };
        foreach (var duplicate in duplicates)
        {
            await using var db = _fixture.CreateContext();
            db.GitHubBranchPublicationApprovals.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Equal(first.Id, (await read.GitHubBranchPublicationApprovals.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task The_five_unique_indexes_exist_and_the_six_references_are_restrictive()
    {
        await SeedAsync();
        await using var db = _fixture.CreateContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var unique = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT group_concat(ii.name) FROM pragma_index_list('github_branch_publication_approvals') il "
                + "JOIN pragma_index_info(il.name) ii WHERE il.\"unique\" = 1 AND il.origin = 'c' GROUP BY il.name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                unique.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            ["BindingId", "EventId", "LocalCommitOperationId", "RunId", "TargetId"],
            unique.Order(StringComparer.Ordinal));

        var references = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT \"from\" || '>' || \"table\" || '>' || on_delete FROM pragma_foreign_key_list('github_branch_publication_approvals')";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                references.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            [
                "BindingId>github_repository_identity_bindings>RESTRICT",
                "GitWorkspaceId>git_workspaces>RESTRICT",
                "LocalCommitOperationId>local_commit_operations>RESTRICT",
                "ProjectId>projects>RESTRICT",
                "RunId>runs>RESTRICT",
                "TargetId>github_observation_targets>RESTRICT",
            ],
            references.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_reference_must_exist()
    {
        var seeded = await SeedAsync();
        foreach (var approval in new[]
                 {
                     ApprovalFor(seeded, runId: Guid.NewGuid()),
                     ApprovalFor(seeded, projectId: Guid.NewGuid()),
                     ApprovalFor(seeded, operationId: Guid.NewGuid()),
                     ApprovalFor(seeded, workspaceId: Guid.NewGuid()),
                     ApprovalFor(seeded, targetId: Guid.NewGuid()),
                     ApprovalFor(seeded, bindingId: Guid.NewGuid()),
                 })
        {
            await using var db = _fixture.CreateContext();
            db.GitHubBranchPublicationApprovals.Add(approval);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubBranchPublicationApprovals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_recorded_approval_cannot_be_updated_or_deleted_by_any_path_and_nothing_it_names_can_be_deleted()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Add(approval);
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            var tracked = await db.GitHubBranchPublicationApprovals.SingleAsync();
            db.Entry(tracked).Property(candidate => candidate.RepositoryNodeId).CurrentValue = "R_other";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.GitHubBranchPublicationApprovals
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ApprovedAtUtc, candidate => candidate.ApprovedAtUtc)));
        }

        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Remove(await db.GitHubBranchPublicationApprovals.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.GitHubBranchPublicationApprovals.ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.GitHubRepositoryIdentityBindings.Where(candidate => candidate.Id == seeded.Binding.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.GitHubObservationTargets.Where(candidate => candidate.Id == seeded.Target.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.Runs.Where(candidate => candidate.Id == seeded.Rows.Run.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.Projects.Where(candidate => candidate.Id == seeded.Rows.Project.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.GitWorkspaces.Where(candidate => candidate.Id == seeded.Rows.Workspace.Id).ExecuteDeleteAsync());
        }

        await using (var db = _fixture.CreateContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => db.LocalCommitOperations.Where(candidate => candidate.Id == seeded.Rows.Operation.Id).ExecuteDeleteAsync());
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.GitHubBranchPublicationApprovals.AsNoTracking().SingleAsync();
        Assert.Equal(approval.Id, stored.Id);
        Assert.Equal("R_kgDOExample01", stored.RepositoryNodeId);
        Assert.True(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.Id == approval.EventId));
        Assert.True(await read.GitHubRepositoryIdentityBindings.AsNoTracking().AnyAsync(candidate => candidate.Id == seeded.Binding.Id));
        Assert.True(await read.GitHubObservationTargets.AsNoTracking().AnyAsync(candidate => candidate.Id == seeded.Target.Id));
    }

    [Fact]
    public async Task A_transaction_that_is_rolled_back_leaves_neither_the_approval_nor_the_event()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded);
        await using (var db = _fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.GitHubBranchPublicationApprovals.Add(approval);
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubBranchPublicationApprovals.AsNoTracking().ToListAsync());
        Assert.False(await read.Events.AsNoTracking().AnyAsync(candidate => candidate.EventType == RunEventType.GitHubBranchPublicationApproved));
    }

    [Fact]
    public async Task A_failed_save_of_the_event_rolls_the_approval_back_with_it()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded);
        await using (var db = _fixture.CreateContext())
        {
            db.Events.Add(RunEvent.Record(
                approval.EventId, seeded.Rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
                "{}", approval.ApprovedAtUtc));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.GitHubBranchPublicationApprovals.Add(approval);
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var read = _fixture.CreateContext();
        Assert.Empty(await read.GitHubBranchPublicationApprovals.AsNoTracking().ToListAsync());
    }
}
