using DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// Drives <see cref="PrepareRepositoryWorkspaceCommandHandler"/> against fakes for all five
/// ports it composes — proving the handler's own ordering, blocking rules, and compensation
/// logic in isolation from real Git/filesystem/Win32 behavior, which are proven separately at
/// the Infrastructure layer.
/// </summary>
public sealed class PrepareRepositoryWorkspaceCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private const string CanonicalPath = @"C:\repos\devalcopilot-test";
    private static readonly byte[] FileId = CreateFileId();
    private const ulong VolumeSerialNumber = 99UL;

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static byte[] CreateFileId()
    {
        var fileId = new byte[16];
        fileId[0] = 42;
        return fileId;
    }

    private sealed class FakeRootPathInspector(RepositoryRootInspectionResult result) : IRepositoryRootPathInspector
    {
        public RepositoryRootInspectionResult Inspect(string requestedPath) => result;
    }

    private sealed class FakePhysicalIdentityInspector(RepositoryPhysicalIdentityInspectionResult result)
        : IRepositoryPhysicalIdentityInspector
    {
        public RepositoryPhysicalIdentityInspectionResult Resolve(RepositoryRootCandidate candidate) => result;
    }

    private sealed class FakeGitRepositoryInspector(GitRepositoryInspectionResult result) : IGitRepositoryInspector
    {
        public Task<GitRepositoryInspectionResult> InspectAsync(RepositoryRootCandidate candidate, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeGitWorktreeAdapter(
        GitWorktreeCreationOutcome creationOutcome = GitWorktreeCreationOutcome.Success,
        GitWorktreeAdministrativeDirectoryOutcome adminDirOutcome = GitWorktreeAdministrativeDirectoryOutcome.Resolved)
        : IGitWorktreeAdapter
    {
        public bool CreateAsyncCalled { get; private set; }

        public Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken)
        {
            CreateAsyncCalled = true;
            return Task.FromResult(new GitWorktreeCreationResult(creationOutcome));
        }

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(adminDirOutcome == GitWorktreeAdministrativeDirectoryOutcome.Resolved
                ? new GitWorktreeAdministrativeDirectoryResult(adminDirOutcome, workspacePath + @"\.git-admin", workspacePath + @"\.git-common")
                : new GitWorktreeAdministrativeDirectoryResult(adminDirOutcome, null, null));

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorktreeHeadResult(GitWorktreeHeadOutcome.Resolved, new string('a', 40)));

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.Registered));
    }

    private sealed class FakeMarkerStore(WorkspaceOwnershipMarkerWriteOutcome writeOutcome = WorkspaceOwnershipMarkerWriteOutcome.Success)
        : IWorkspaceOwnershipMarkerStore
    {
        public Task<WorkspaceOwnershipMarkerWriteResult> WriteAsync(
            string administrativeDirectory, WorkspaceOwnershipMarker marker, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceOwnershipMarkerWriteResult(writeOutcome));

        public Task<WorkspaceOwnershipMarkerReadResult> ReadAsync(string administrativeDirectory, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not used by the preparation handler.");
    }

    private sealed class FakeWorkspaceRootPathProvider : IWorkspaceRootPathProvider
    {
        public string ComputeWorkspacePath(Guid projectId, int workspaceNumber) =>
            $@"C:\workspaces\{projectId:N}\{workspaceNumber}";
    }

    private static readonly RepositoryRootInspectionResult SuccessfulRoot = new(
        RepositoryRootInspectionOutcome.Success, new RepositoryRootCandidate(CanonicalPath));

    private static readonly RepositoryPhysicalIdentityInspectionResult ResolvedIdentity = new(
        RepositoryPhysicalIdentityInspectionOutcome.Resolved, VolumeSerialNumber, FileId);

    private static readonly GitRepositoryInspectionResult CleanOnBranch = new(
        GitRepositoryInspectionOutcome.Success, RepositoryHeadState.OnBranch, "main", new string('a', 40), IsDirty: false);

    private async Task<Project> SeedResolvedProjectAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        project.RecordPhysicalIdentityResolved(VolumeSerialNumber, FileId);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project;
    }

    private static async Task SeedGitReadyAsync(DevalCopilotDbContext dbContext)
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.45.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private PrepareRepositoryWorkspaceCommandHandler CreateHandler(
        DevalCopilotDbContext dbContext,
        GitRepositoryInspectionResult? gitResult = null,
        FakeGitWorktreeAdapter? worktreeAdapter = null,
        FakeMarkerStore? markerStore = null) =>
        new(
            dbContext,
            new FakeRootPathInspector(SuccessfulRoot),
            new FakePhysicalIdentityInspector(ResolvedIdentity),
            new FakeGitRepositoryInspector(gitResult ?? CleanOnBranch),
            worktreeAdapter ?? new FakeGitWorktreeAdapter(),
            markerStore ?? new FakeMarkerStore(),
            new FakeWorkspaceRootPathProvider(),
            new FixedTimeProvider(Now));

    [Fact]
    public async Task HandleAsync_prepares_a_ready_workspace_and_an_active_lease_on_success()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var handler = CreateHandler(dbContext);
        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Equal(1, workspace.WorkspaceNumber);
        Assert.Equal("main", workspace.SourceBranchName);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(VolumeSerialNumber, lease.PhysicalVolumeSerialNumber);
    }

    [Fact]
    public async Task HandleAsync_allows_a_detached_head_and_records_a_null_source_branch()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var detached = new GitRepositoryInspectionResult(
            GitRepositoryInspectionOutcome.Success, RepositoryHeadState.Detached, null, new string('b', 40), IsDirty: false);
        var handler = CreateHandler(dbContext, gitResult: detached);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Null(workspace.SourceBranchName);
        Assert.Equal(new string('b', 40), workspace.SourceCommitSha);
    }

    [Fact]
    public async Task HandleAsync_blocks_a_dirty_repository_without_persisting_anything()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var dirty = new GitRepositoryInspectionResult(
            GitRepositoryInspectionOutcome.Success, RepositoryHeadState.OnBranch, "main", new string('a', 40), IsDirty: true);
        var handler = CreateHandler(dbContext, gitResult: dirty);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.dirty_repository_not_supported", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.GitWorkspaces);
        Assert.Empty(dbContext.RepositoryMutationLeases);
    }

    [Fact]
    public async Task HandleAsync_blocks_an_unborn_repository_without_persisting_anything()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var unborn = new GitRepositoryInspectionResult(
            GitRepositoryInspectionOutcome.Success, RepositoryHeadState.Unborn, "main", null, IsDirty: false);
        var handler = CreateHandler(dbContext, gitResult: unborn);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.unborn_repository_not_supported", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.GitWorkspaces);
    }

    [Fact]
    public async Task HandleAsync_blocks_when_git_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        // No HostCapabilitySnapshot seeded.

        var handler = CreateHandler(dbContext);
        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.git_unavailable", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.GitWorkspaces);
    }

    [Fact]
    public async Task HandleAsync_blocks_when_physical_identity_is_still_unresolved_after_the_inline_recheck_fails()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", CanonicalPath, Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await SeedGitReadyAsync(dbContext);

        var handler = new PrepareRepositoryWorkspaceCommandHandler(
            dbContext,
            new FakeRootPathInspector(SuccessfulRoot),
            new FakePhysicalIdentityInspector(
                new RepositoryPhysicalIdentityInspectionResult(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible, null, null)),
            new FakeGitRepositoryInspector(CleanOnBranch),
            new FakeGitWorktreeAdapter(),
            new FakeMarkerStore(),
            new FakeWorkspaceRootPathProvider(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.physical_identity_path_inaccessible", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.GitWorkspaces);
    }

    [Fact]
    public async Task HandleAsync_blocks_a_fresh_physical_identity_mismatch_without_overwriting_the_stored_identity()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var differentIdentity = new RepositoryPhysicalIdentityInspectionResult(
            RepositoryPhysicalIdentityInspectionOutcome.Resolved, VolumeSerialNumber + 1, FileId);
        var handler = new PrepareRepositoryWorkspaceCommandHandler(
            dbContext,
            new FakeRootPathInspector(SuccessfulRoot),
            new FakePhysicalIdentityInspector(differentIdentity),
            new FakeGitRepositoryInspector(CleanOnBranch),
            new FakeGitWorktreeAdapter(),
            new FakeMarkerStore(),
            new FakeWorkspaceRootPathProvider(),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.physical_identity_mismatch", Assert.Single(result.Errors).Code);
        var reloaded = await dbContext.Projects.FindAsync(project.Id);
        Assert.Equal(VolumeSerialNumber, reloaded!.PhysicalVolumeSerialNumber);
    }

    [Fact]
    public async Task HandleAsync_blocks_a_second_request_while_an_active_lease_already_exists()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var firstHandler = CreateHandler(dbContext);
        var firstResult = await firstHandler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondHandler = CreateHandler(dbContext);
        var secondResult = await secondHandler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("workspaces.already_has_active_workspace", Assert.Single(secondResult.Errors).Code);
        Assert.Equal(1, await dbContext.GitWorkspaces.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_compensates_a_git_creation_failure_by_failing_the_workspace_and_releasing_the_lease()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var worktreeAdapter = new FakeGitWorktreeAdapter(creationOutcome: GitWorktreeCreationOutcome.GitInvocationFailed);
        var handler = CreateHandler(dbContext, worktreeAdapter: worktreeAdapter);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.git_invocation_failed", Assert.Single(result.Errors).Code);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, workspace.Status);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Released, lease.Status);
    }

    [Fact]
    public async Task HandleAsync_maps_a_workspace_repository_path_overlap_to_a_safe_conflict_without_either_path()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var worktreeAdapter = new FakeGitWorktreeAdapter(creationOutcome: GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository);
        var handler = CreateHandler(dbContext, worktreeAdapter: worktreeAdapter);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("workspaces.path_overlaps_repository", error.Code);
        // The safe, fixed message never carries either path — only Infrastructure ever saw them.
        Assert.DoesNotContain(CanonicalPath, error.Description, StringComparison.OrdinalIgnoreCase);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, workspace.Status);
        Assert.Equal("workspaces.path_overlaps_repository", workspace.LastFailureReasonCode);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Released, lease.Status);
    }

    [Fact]
    public async Task HandleAsync_allows_a_new_attempt_with_a_new_workspace_number_after_a_compensated_failure()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var failingAdapter = new FakeGitWorktreeAdapter(creationOutcome: GitWorktreeCreationOutcome.GitInvocationFailed);
        var firstHandler = CreateHandler(dbContext, worktreeAdapter: failingAdapter);
        await firstHandler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        var secondHandler = CreateHandler(dbContext);
        var secondResult = await secondHandler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(2, await dbContext.GitWorkspaces.CountAsync());
        var readyWorkspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Status == WorkspaceStatus.Ready);
        Assert.Equal(2, readyWorkspace.WorkspaceNumber);
    }

    [Fact]
    public async Task HandleAsync_compensates_an_administrative_directory_mismatch()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var worktreeAdapter = new FakeGitWorktreeAdapter(adminDirOutcome: GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree);
        var handler = CreateHandler(dbContext, worktreeAdapter: worktreeAdapter);

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.administrative_directory_not_resolved", Assert.Single(result.Errors).Code);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_compensates_a_marker_write_failure()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = await SeedResolvedProjectAsync(dbContext);
        await SeedGitReadyAsync(dbContext);

        var handler = CreateHandler(dbContext, markerStore: new FakeMarkerStore(WorkspaceOwnershipMarkerWriteOutcome.WriteFailed));

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.marker_write_failed", Assert.Single(result.Errors).Code);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.ProjectId == project.Id);
        Assert.Equal(WorkspaceStatus.FailedToPrepare, workspace.Status);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Released, lease.Status);
    }
}
