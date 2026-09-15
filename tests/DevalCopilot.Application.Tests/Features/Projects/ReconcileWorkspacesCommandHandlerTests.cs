using DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// Drives <see cref="ReconcileWorkspacesCommandHandler"/> against fakes for
/// <see cref="IGitWorktreeAdapter"/> and <see cref="IWorkspaceOwnershipMarkerStore"/>, proving
/// each row of ADR-0008's evidence-driven reconciliation table.
/// </summary>
public sealed class ReconcileWorkspacesCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly string SourceCommitSha = new('a', 40);
    private static readonly byte[] FileId = CreateFileId();

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static byte[] CreateFileId()
    {
        var fileId = new byte[16];
        fileId[0] = 11;
        return fileId;
    }

    private sealed class FakeGitWorktreeAdapter(
        GitWorktreeRegistrationOutcome registrationOutcome,
        GitWorktreeAdministrativeDirectoryOutcome adminDirOutcome,
        string? currentHeadSha) : IGitWorktreeAdapter
    {
        public Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never creates a worktree.");

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(adminDirOutcome == GitWorktreeAdministrativeDirectoryOutcome.Resolved
                ? new GitWorktreeAdministrativeDirectoryResult(adminDirOutcome, workspacePath + @"\.admin", workspacePath + @"\.common")
                : new GitWorktreeAdministrativeDirectoryResult(adminDirOutcome, null, null));

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(currentHeadSha is null
                ? new GitWorktreeHeadResult(GitWorktreeHeadOutcome.GitInvocationFailed, null)
                : new GitWorktreeHeadResult(GitWorktreeHeadOutcome.Resolved, currentHeadSha));

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorktreeRegistrationResult(registrationOutcome));
    }

    /// <summary>Throws on its second invocation regardless of which lease it was called for —
    /// used to prove that whichever lease was already fully evaluated before the throw has its
    /// transition durably committed, independent of the later lease's failure.</summary>
    private sealed class ThrowsOnSecondCallGitWorktreeAdapter : IGitWorktreeAdapter
    {
        private int _callCount;

        public Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never creates a worktree.");

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached: each lease in this test is decided by IsRegisteredAsync alone.");

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached: each lease in this test is decided by IsRegisteredAsync alone.");

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount == 2)
            {
                throw new InvalidOperationException("Simulated failure while gathering evidence for the second lease.");
            }

            return Task.FromResult(new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.NotRegistered));
        }
    }

    private sealed class FakeMarkerStore(WorkspaceOwnershipMarkerReadResult result) : IWorkspaceOwnershipMarkerStore
    {
        public Task<WorkspaceOwnershipMarkerWriteResult> WriteAsync(
            string administrativeDirectory, WorkspaceOwnershipMarker marker, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never writes the marker.");

        public Task<WorkspaceOwnershipMarkerReadResult> ReadAsync(string administrativeDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private async Task<(Project Project, GitWorkspace Workspace, RepositoryMutationLease Lease)> SeedAsync(
        DevalCopilotDbContext dbContext, WorkspaceStatus workspaceStatus)
    {
        // A distinct canonical path (and therefore a distinct physical file ID) per call: tests
        // that seed more than one project/lease in the same database must never collide on the
        // unique RegistrationIdentityKey or the unique physical-identity-active-lease index.
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\devalcopilot-test-{uniqueSuffix}", Now);
        var fileId = new byte[16];
        fileId[0] = FileId[0];
        Guid.NewGuid().ToByteArray().AsSpan(0, 15).CopyTo(fileId.AsSpan(1));
        project.RecordPhysicalIdentityResolved(1UL, fileId);
        var workspaceNumber = project.ReserveWorkspaceNumber();
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, workspaceNumber, $@"C:\workspaces\{uniqueSuffix}\1",
            $"devalcopilot/workspace/{uniqueSuffix}/1", SourceCommitSha, "main", Now);
        if (workspaceStatus == WorkspaceStatus.Ready)
        {
            workspace.MarkReady();
        }

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1UL, fileId, Now);

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.RepositoryMutationLeases.Add(lease);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (project, workspace, lease);
    }

    private WorkspaceOwnershipMarker ValidMarkerFor(GitWorkspace workspace, RepositoryMutationLease lease) => new(
        workspace.Id, workspace.ProjectId, lease.Id, lease.PhysicalVolumeSerialNumber, Convert.ToHexString(lease.PhysicalFileId));

    [Fact]
    public async Task HandleAsync_leaves_an_owned_recoverable_ready_workspace_untouched()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Ready);
        var marker = ValidMarkerFor(workspace, lease);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.Registered, GitWorktreeAdministrativeDirectoryOutcome.Resolved, SourceCommitSha),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Valid, marker)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        var reloaded = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.Ready, reloaded!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Active, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_promotes_a_preparing_workspace_to_ready_when_all_evidence_agrees()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Preparing);
        var marker = ValidMarkerFor(workspace, lease);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.Registered, GitWorktreeAdministrativeDirectoryOutcome.Resolved, SourceCommitSha),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Valid, marker)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        Assert.Equal(1, result.Value);
        var reloaded = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.Ready, reloaded!.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_a_preparing_workspace_closed_when_the_worktree_was_never_created()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Preparing);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.NotRegistered, GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null)),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var reloadedWorkspace = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, reloadedWorkspace!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Released, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_a_preparing_workspace_closed_when_the_marker_was_never_written()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Preparing);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.Registered, GitWorktreeAdministrativeDirectoryOutcome.Resolved, SourceCommitSha),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null)),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var reloadedWorkspace = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, reloadedWorkspace!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Released, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_marks_a_ready_workspace_missing_externally_when_the_worktree_disappeared()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Ready);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.NotRegistered, GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null)),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var reloadedWorkspace = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.MissingExternally, reloadedWorkspace!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Superseded, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_marks_a_ready_workspace_altered_externally_when_the_marker_is_invalid()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Ready);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.Registered, GitWorktreeAdministrativeDirectoryOutcome.Resolved, SourceCommitSha),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Invalid, null)),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var reloadedWorkspace = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.AlteredExternally, reloadedWorkspace!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Superseded, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_marks_a_ready_workspace_needs_attention_when_content_diverged_but_keeps_the_lease_active()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, lease) = await SeedAsync(dbContext, WorkspaceStatus.Ready);
        var marker = ValidMarkerFor(workspace, lease);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.Registered, GitWorktreeAdministrativeDirectoryOutcome.Resolved, new string('c', 40)),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Valid, marker)),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var reloadedWorkspace = await dbContext.GitWorkspaces.FindAsync(workspace.Id);
        Assert.Equal(WorkspaceStatus.NeedsAttention, reloadedWorkspace!.Status);
        var reloadedLease = await dbContext.RepositoryMutationLeases.FindAsync(lease.Id);
        Assert.Equal(LeaseStatus.Active, reloadedLease!.Status);
    }

    [Fact]
    public async Task HandleAsync_persists_an_already_decided_leases_transition_even_when_a_later_lease_throws()
    {
        await using var dbContext = _fixture.CreateContext();
        await SeedAsync(dbContext, WorkspaceStatus.Ready);
        await SeedAsync(dbContext, WorkspaceStatus.Ready);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new ThrowsOnSecondCallGitWorktreeAdapter(),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null)),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None));

        // Exactly one of the two leases was fully evaluated (found missing) before the other's
        // evidence-gathering threw — and that one transition is durably committed despite the
        // handler as a whole never completing, because each lease's transition is saved
        // independently, never inside one ambient transaction spanning the whole pass.
        Assert.Equal(1, await dbContext.RepositoryMutationLeases.CountAsync(l => l.Status == LeaseStatus.Superseded));
        Assert.Equal(1, await dbContext.RepositoryMutationLeases.CountAsync(l => l.Status == LeaseStatus.Active));
    }

    [Fact]
    public async Task HandleAsync_never_reconciles_a_lease_that_is_already_released()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, _, lease) = await SeedAsync(dbContext, WorkspaceStatus.Ready);
        lease.Release(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileWorkspacesCommandHandler(
            dbContext,
            new FakeGitWorktreeAdapter(GitWorktreeRegistrationOutcome.NotRegistered, GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null),
            new FakeMarkerStore(new WorkspaceOwnershipMarkerReadResult(WorkspaceOwnershipMarkerReadOutcome.Absent, null)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        Assert.Equal(0, result.Value);
    }
}
