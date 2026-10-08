using DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0008 reconciliation with ADR-0029's narrowly extended expected tip. Only the unique coherent chain of COMPLETED recorded
/// parent-to-commit edges rooted at the immutable source commit extends what a Ready workspace's HEAD may be; an unrecorded advance,
/// a stale head, a broken or forked chain all fail closed, and a reserved or attention workspace is never repaired by it.
/// </summary>
public sealed class ReconcileWorkspacesLocalCommitChainTests : IAsyncLifetime
{
    private static readonly string Source = LocalCommitRowsSeed.SourceCommit;
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Oid(char value) => new(value, 40);

    private sealed class FakeWorktrees(string? head) : IGitWorktreeAdapter
    {
        public Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never creates a worktree.");

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorktreeAdministrativeDirectoryResult(
                GitWorktreeAdministrativeDirectoryOutcome.Resolved, workspacePath + @"\.admin", workspacePath + @"\.common"));

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(head is null
                ? new GitWorktreeHeadResult(GitWorktreeHeadOutcome.GitInvocationFailed, null)
                : new GitWorktreeHeadResult(GitWorktreeHeadOutcome.Resolved, head));

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.Registered));
    }

    private sealed class ValidMarkers(RepositoryMutationLease lease, GitWorkspace workspace) : IWorkspaceOwnershipMarkerStore
    {
        public Task<WorkspaceOwnershipMarkerWriteResult> WriteAsync(
            string administrativeDirectory, WorkspaceOwnershipMarker marker, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never writes the marker.");

        public Task<WorkspaceOwnershipMarkerReadResult> ReadAsync(string administrativeDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceOwnershipMarkerReadResult(
                WorkspaceOwnershipMarkerReadOutcome.Valid,
                new WorkspaceOwnershipMarker(
                    workspace.Id, workspace.ProjectId, lease.Id, lease.PhysicalVolumeSerialNumber, Convert.ToHexString(lease.PhysicalFileId))));
    }

    private async Task<(LocalCommitRows Rows, DevalCopilotDbContext Db)> SeedChainAsync(
        WorkspaceStatus status, params (string Parent, string Commit, LocalCommitStatus Status)[] edges)
    {
        var db = _fixture.CreateContext();
        var first = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
        // The seed created one Prepared operation; the requested chain replaces it with its recorded edges.
        await db.LocalCommitAuthorityMembers.Where(member => member.OperationId == first.Operation.Id).ExecuteDeleteAsync();
        await db.LocalCommitOperations.Where(operation => operation.Id == first.Operation.Id).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == first.Workspace.Id);
        var lease = await db.RepositoryMutationLeases.SingleAsync(candidate => candidate.Id == first.Lease.Id);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == first.Project.Id);
        LocalCommitRows last = first;
        foreach (var (parent, commit, edgeStatus) in edges)
        {
            last = await LocalCommitRowsSeed.AddOperationAsync(db, project, workspace, lease, edgeStatus, parent, commit);
        }

        if (status != WorkspaceStatus.Ready)
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == workspace.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, status));
        }

        db.ChangeTracker.Clear();
        return (last with { Workspace = workspace, Lease = lease, Project = project }, db);
    }

    private static async Task<WorkspaceStatus> ReconcileAsync(
        DevalCopilotDbContext db, LocalCommitRows rows, string? head)
    {
        var workspace = await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Workspace.Id);
        var lease = await db.RepositoryMutationLeases.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Lease.Id);
        var handler = new ReconcileWorkspacesCommandHandler(
            db, new FakeWorktrees(head), new ValidMarkers(lease, workspace), new FixedTimeProvider(LocalCommitRowsSeed.Now));
        db.ChangeTracker.Clear();
        var result = await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        db.ChangeTracker.Clear();
        return (await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Workspace.Id)).Status;
    }

    [Fact]
    public async Task A_workspace_with_no_recorded_commit_expects_exactly_the_source_commit()
    {
        var (rows, db) = await SeedChainAsync(WorkspaceStatus.Ready);
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.Ready, await ReconcileAsync(db, rows, Source));
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(db, rows, Oid('9')));
        }
    }

    [Fact]
    public async Task A_completed_recorded_commit_extends_the_expected_head_and_the_old_head_is_then_a_divergence()
    {
        var (rows, db) = await SeedChainAsync(WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed));
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.Ready, await ReconcileAsync(db, rows, Oid('1')));
        }

        var (stale, staleDb) = await SeedChainAsync(WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed));
        await using (staleDb)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(staleDb, stale, Source));
        }
    }

    [Fact]
    public async Task A_later_run_in_the_same_workspace_extends_the_same_unique_chain()
    {
        var (rows, db) = await SeedChainAsync(
            WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed), (Oid('1'), Oid('2'), LocalCommitStatus.Completed));
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.Ready, await ReconcileAsync(db, rows, Oid('2')));
        }

        var (middle, middleDb) = await SeedChainAsync(
            WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed), (Oid('1'), Oid('2'), LocalCommitStatus.Completed));
        await using (middleDb)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(middleDb, middle, Oid('1')));
        }
    }

    [Theory]
    [InlineData(LocalCommitStatus.Failed)]
    [InlineData(LocalCommitStatus.Interrupted)]
    [InlineData(LocalCommitStatus.NeedsAttention)]
    [InlineData(LocalCommitStatus.Executing)]
    public async Task A_recorded_commit_that_did_not_complete_never_extends_the_expected_head(LocalCommitStatus status)
    {
        var (rows, db) = await SeedChainAsync(WorkspaceStatus.Ready, (Source, Oid('1'), status));
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(db, rows, Oid('1')));
        }
    }

    [Fact]
    public async Task A_broken_or_forked_completed_chain_expects_nothing_and_fails_closed_for_every_head()
    {
        var (broken, brokenDb) = await SeedChainAsync(
            WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed), (Oid('8'), Oid('2'), LocalCommitStatus.Completed));
        await using (brokenDb)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(brokenDb, broken, Oid('2')));
        }

        var (forked, forkedDb) = await SeedChainAsync(
            WorkspaceStatus.Ready, (Source, Oid('1'), LocalCommitStatus.Completed), (Source, Oid('2'), LocalCommitStatus.Completed));
        await using (forkedDb)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(forkedDb, forked, Oid('1')));
        }
    }

    [Fact]
    public async Task A_reserved_workspace_is_never_changed_by_reconciliation_whatever_its_head()
    {
        var (rows, db) = await SeedChainAsync(WorkspaceStatus.Committing, (Source, Oid('1'), LocalCommitStatus.Executing));
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.Committing, await ReconcileAsync(db, rows, Oid('9')));
            Assert.Equal(WorkspaceStatus.Committing, await ReconcileAsync(db, rows, Source));
        }
    }

    [Fact]
    public async Task A_workspace_already_needing_attention_is_not_repaired_even_when_the_head_is_now_the_chain_tip()
    {
        var (rows, db) = await SeedChainAsync(WorkspaceStatus.NeedsAttention, (Source, Oid('1'), LocalCommitStatus.Completed));
        await using (db)
        {
            Assert.Equal(WorkspaceStatus.NeedsAttention, await ReconcileAsync(db, rows, Oid('1')));
        }
    }
}
