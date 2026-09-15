using System.Diagnostics;
using DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Drives <see cref="PrepareRepositoryWorkspaceCommandHandler"/> and
/// <see cref="ReconcileWorkspacesCommandHandler"/> with every real Infrastructure adapter — real
/// <c>git.exe</c>, the real Win32 physical-identity P/Invoke, a real file-backed SQLite
/// database, and a real temporary workspace root — proving the full vertical end to end rather
/// than only its individually faked Application-layer branches.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class PrepareRepositoryWorkspaceRealGitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-prepare-workspace-{Guid.NewGuid():N}");
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-prepare-workspace-{Guid.NewGuid():N}.db");
    private readonly IRepositoryPhysicalIdentityInspector _physicalIdentityInspector = new RepositoryPhysicalIdentityInspector();
    private readonly IRepositoryRootPathInspector _rootPathInspector = new RepositoryRootPathInspector();
    private readonly IGitRepositoryInspector _gitRepositoryInspector = new GitRepositoryInspector(new ChildProcessExecutionAdapter());
    private readonly IGitWorktreeAdapter _gitWorktreeAdapter = new GitWorktreeAdapter(new ChildProcessExecutionAdapter());
    private readonly IWorkspaceOwnershipMarkerStore _markerStore = new WorkspaceOwnershipMarkerStore();
    private readonly IWorkspaceRootPathProvider _workspaceRootPathProvider;

    public PrepareRepositoryWorkspaceRealGitTests()
    {
        Directory.CreateDirectory(_root);
        _workspaceRootPathProvider = new WorkspaceRootPathProvider(Path.Combine(_root, "workspaces"));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string CreateRepo(string name, bool withCommit = true)
    {
        var path = Path.Combine(_root, "repos", name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");

        if (withCommit)
        {
            File.WriteAllText(Path.Combine(path, "file.txt"), "content");
            RunGit(path, "add", "file.txt");
            RunGit(path, "commit", "-q", "-m", "initial");
        }

        return path;
    }

    private async Task<Project> RegisterAndResolveAsync(DevalCopilotDbContext dbContext, string canonicalPath)
    {
        var project = Project.Register(Guid.NewGuid(), "Test", canonicalPath, Now);
        var identity = _physicalIdentityInspector.Resolve(new RepositoryRootCandidate(canonicalPath));
        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, identity.Outcome);
        project.RecordPhysicalIdentityResolved(identity.VolumeSerialNumber!.Value, identity.FileId!);

        dbContext.Projects.Add(project);

        var gitReady = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        gitReady.MarkDispatched(Now);
        gitReady.RecordSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.45.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(gitReady);

        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project;
    }

    private PrepareRepositoryWorkspaceCommandHandler CreateHandler(DevalCopilotDbContext dbContext) => new(
        dbContext,
        _rootPathInspector,
        _physicalIdentityInspector,
        _gitRepositoryInspector,
        _gitWorktreeAdapter,
        _markerStore,
        _workspaceRootPathProvider,
        new FixedTimeProvider(Now));

    [Fact]
    public async Task HandleAsync_prepares_a_real_ready_workspace_with_a_valid_marker_at_the_real_administrative_directory()
    {
        var repoPath = CreateRepo("clean");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);

        var result = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(result.Value.WorkspacePath));

        var adminDirResult = await _gitWorktreeAdapter.ResolveAdministrativeDirectoryAsync(
            repoPath, result.Value.WorkspacePath, CancellationToken.None);
        Assert.Equal(GitWorktreeAdministrativeDirectoryOutcome.Resolved, adminDirResult.Outcome);

        var markerResult = await _markerStore.ReadAsync(adminDirResult.AdministrativeDirectory!, CancellationToken.None);
        Assert.Equal(WorkspaceOwnershipMarkerReadOutcome.Valid, markerResult.Outcome);
        Assert.Equal(result.Value.WorkspaceId, markerResult.Marker!.WorkspaceId);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == result.Value.WorkspaceId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_blocks_a_dirty_real_repository_and_creates_no_worktree()
    {
        var repoPath = CreateRepo("dirty");
        File.WriteAllText(Path.Combine(repoPath, "file.txt"), "changed");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);

        var result = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.dirty_repository_not_supported", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.GitWorkspaces);
        Assert.False(Directory.Exists(Path.Combine(_root, "workspaces")));
    }

    [Fact]
    public async Task HandleAsync_blocks_an_unborn_real_repository()
    {
        var repoPath = CreateRepo("unborn", withCommit: false);
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);

        var result = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("workspaces.unborn_repository_not_supported", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_prepares_a_workspace_from_a_real_detached_head()
    {
        var repoPath = CreateRepo("detached");
        RunGit(repoPath, "checkout", "-q", "--detach", "HEAD");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);

        var result = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SourceBranchName);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_second_concurrent_request_for_the_same_repository()
    {
        var repoPath = CreateRepo("concurrent");
        await using var seedContext = CreateContext();
        await seedContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(seedContext, repoPath);
        var projectId = project.Id;

        // Two independent DbContext instances against the same file-backed database, racing to
        // prepare a workspace for the same physical repository — proving the database's
        // partial unique index, not application-level locking, is what enforces exclusivity.
        await using var contextA = CreateContext();
        await using var contextB = CreateContext();

        var taskA = CreateHandler(contextA).HandleAsync(new PrepareRepositoryWorkspaceCommand(projectId), CancellationToken.None);
        var taskB = CreateHandler(contextB).HandleAsync(new PrepareRepositoryWorkspaceCommand(projectId), CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.IsFailure && r.Errors[0].Code == "workspaces.already_has_active_workspace");

        await using var verifyContext = CreateContext();
        Assert.Equal(1, await verifyContext.GitWorkspaces.CountAsync());
        Assert.Equal(1, await verifyContext.RepositoryMutationLeases.CountAsync(l => l.Status == LeaseStatus.Active));
    }

    [Fact]
    public async Task Reconciliation_detects_a_manually_tampered_marker_and_supersedes_the_lease()
    {
        var repoPath = CreateRepo("tamper");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);
        var prepareResult = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(prepareResult.IsSuccess);

        var adminDirResult = await _gitWorktreeAdapter.ResolveAdministrativeDirectoryAsync(
            repoPath, prepareResult.Value.WorkspacePath, CancellationToken.None);
        var markerPath = Path.Combine(adminDirResult.AdministrativeDirectory!, "devalcopilot-ownership.json");
        var tampered = (await File.ReadAllTextAsync(markerPath)).Replace(
            prepareResult.Value.WorkspaceId.ToString(), Guid.NewGuid().ToString());
        await File.WriteAllTextAsync(markerPath, tampered);

        var reconcileHandler = new ReconcileWorkspacesCommandHandler(
            dbContext, _gitWorktreeAdapter, _markerStore, new FixedTimeProvider(Now.AddMinutes(1)));
        var reconcileResult = await reconcileHandler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);
        // Direct HandleAsync calls in this test bypass the mediator's automatic post-handler
        // save, exactly like every other direct-dispatch test in this file — persisted here to
        // make the reconciliation's outcome visible to the retry's own database query below.
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, reconcileResult.Value);
        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == prepareResult.Value.WorkspaceId);
        Assert.Equal(WorkspaceStatus.AlteredExternally, workspace.Status);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Superseded, lease.Status);

        // A fresh preparation request for the same repository succeeds at a brand-new
        // workspace number/path rather than reusing or repairing the tampered one.
        var retryResult = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(retryResult.IsSuccess);
        Assert.NotEqual(prepareResult.Value.WorkspacePath, retryResult.Value.WorkspacePath);
    }

    [Fact]
    public async Task Reconciliation_detects_a_workspace_removed_after_the_host_restarted()
    {
        var repoPath = CreateRepo("removed");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);
        var prepareResult = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(prepareResult.IsSuccess);

        RunGit(repoPath, "worktree", "remove", "--force", prepareResult.Value.WorkspacePath);

        var reconcileHandler = new ReconcileWorkspacesCommandHandler(
            dbContext, _gitWorktreeAdapter, _markerStore, new FixedTimeProvider(Now.AddMinutes(1)));
        await reconcileHandler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == prepareResult.Value.WorkspaceId);
        Assert.Equal(WorkspaceStatus.MissingExternally, workspace.Status);
        var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspace.Id);
        Assert.Equal(LeaseStatus.Superseded, lease.Status);
    }

    [Fact]
    public async Task Reconciliation_leaves_an_owned_recoverable_workspace_undisturbed_after_a_simulated_restart()
    {
        var repoPath = CreateRepo("recoverable");
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var project = await RegisterAndResolveAsync(dbContext, repoPath);
        var prepareResult = await CreateHandler(dbContext).HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(prepareResult.IsSuccess);

        // A fresh DbContext, exactly as a restarted host process would use.
        await using var restartedContext = CreateContext();
        var reconcileHandler = new ReconcileWorkspacesCommandHandler(
            restartedContext, _gitWorktreeAdapter, _markerStore, new FixedTimeProvider(Now.AddMinutes(1)));
        var reconcileResult = await reconcileHandler.HandleAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        Assert.Equal(0, reconcileResult.Value);
        var workspace = await restartedContext.GitWorkspaces.SingleAsync(w => w.Id == prepareResult.Value.WorkspaceId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }
}
