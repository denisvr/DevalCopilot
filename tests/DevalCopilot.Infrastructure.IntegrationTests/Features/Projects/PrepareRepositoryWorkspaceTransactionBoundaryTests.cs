using System.Diagnostics;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Drives <see cref="PrepareRepositoryWorkspaceCommand"/> through the real
/// <see cref="IApplicationMediator"/> and the real <c>Devalente.Shared.EntityFrameworkCore</c>
/// transaction pipeline — never a direct handler call — against a real file-backed SQLite
/// database, proving the exact invariant ADR-0008 requires: the durable-intent
/// <c>GitWorkspace</c>(<see cref="WorkspaceStatus.Preparing"/>)/<c>RepositoryMutationLease</c>(<see
/// cref="LeaseStatus.Active"/>) commit is genuinely visible to an independent database
/// connection <em>before</em> the external Git worktree side effect runs, not merely before the
/// handler method returns. A <see cref="GatedGitWorktreeAdapter"/> blocks exactly inside
/// <c>CreateAsync</c> to create the window this test inspects.
///
/// <para>
/// This test is meaningless — and was written to fail — if
/// <see cref="PrepareRepositoryWorkspaceCommand"/> is ever declared <c>ICommand&lt;TResult&gt;</c>
/// instead of <see cref="IManualTransactionCommand{TResult}"/>: only the manual-transaction
/// declaration keeps <c>AddDevalenteEfCoreTransactions</c> from wrapping the whole handler
/// (including its first explicit <c>SaveChangesAsync</c>) inside one ambient transaction that
/// would not actually commit until the handler returns, i.e. until after the gated Git call is
/// released. Empirically confirmed by temporarily reverting the command's declared interface: the
/// independent-context read then finds nothing during the blocked window and this test fails.
/// </para>
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class PrepareRepositoryWorkspaceTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(15);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-txn-boundary-{Guid.NewGuid():N}");
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-txn-boundary-{Guid.NewGuid():N}.db");

    public PrepareRepositoryWorkspaceTransactionBoundaryTests()
    {
        Directory.CreateDirectory(_root);
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

    private string CreateRepo(string name)
    {
        var path = Path.Combine(_root, "repos", name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(path, "file.txt"), "content");
        RunGit(path, "add", "file.txt");
        RunGit(path, "commit", "-q", "-m", "initial");
        return path;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    /// <summary>Wraps the real <see cref="GitWorktreeAdapter"/>, suspending exactly inside
    /// <see cref="CreateAsync"/> until the test releases it — the window in which the durable
    /// intent commit's real visibility is inspected.</summary>
    private sealed class GatedGitWorktreeAdapter(IGitWorktreeAdapter real) : IGitWorktreeAdapter
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilEnteredAsync() => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task;
            return await real.CreateAsync(mainRepositoryPath, workspacePath, branchName, resolvedCommitSha, cancellationToken);
        }

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            real.ResolveAdministrativeDirectoryAsync(mainRepositoryPath, workspacePath, cancellationToken);

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken) =>
            real.GetHeadCommitShaAsync(workspacePath, cancellationToken);

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken) =>
            real.IsRegisteredAsync(mainRepositoryPath, workspacePath, cancellationToken);
    }

    private async Task<Guid> SeedResolvedProjectAsync(string canonicalPath)
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "Test", canonicalPath, Now);
        var identity = new RepositoryPhysicalIdentityInspector().Resolve(new RepositoryRootCandidate(canonicalPath));
        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, identity.Outcome);
        project.RecordPhysicalIdentityResolved(identity.VolumeSerialNumber!.Value, identity.FileId!);
        dbContext.Projects.Add(project);

        var gitReady = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        gitReady.MarkDispatched(Now);
        gitReady.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.45.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(gitReady);

        await dbContext.SaveChangesAsync(CancellationToken.None);
        return project.Id;
    }

    /// <summary>A minimal DI container wiring the real Devalente mediator
    /// (<c>AddDevalenteMediator</c>) and the real EF Core transaction pipeline
    /// (<c>AddDevalenteEfCoreTransactions</c>) — the exact mechanism under test — never a fake
    /// or a direct handler call standing in for either.</summary>
    private ServiceProvider BuildContainer(IGitWorktreeAdapter gitWorktreeAdapter)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<IRepositoryRootPathInspector, RepositoryRootPathInspector>();
        services.AddSingleton<IRepositoryPhysicalIdentityInspector, RepositoryPhysicalIdentityInspector>();
        services.AddSingleton<IProcessExecutionAdapter, ChildProcessExecutionAdapter>();
        services.AddSingleton<IGitRepositoryInspector, GitRepositoryInspector>();
        services.AddSingleton(gitWorktreeAdapter);
        services.AddSingleton<IWorkspaceOwnershipMarkerStore, WorkspaceOwnershipMarkerStore>();
        services.AddSingleton<IWorkspaceRootPathProvider>(new WorkspaceRootPathProvider(Path.Combine(_root, "workspaces")));

        services.AddDevalenteMediator(typeof(PrepareRepositoryWorkspaceCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PrepareRepositoryWorkspace_commits_durable_intent_before_git_worktree_creation_runs()
    {
        var repoPath = CreateRepo("txn-boundary");
        var projectId = await SeedResolvedProjectAsync(repoPath);

        var gatedAdapter = new GatedGitWorktreeAdapter(new GitWorktreeAdapter(new ChildProcessExecutionAdapter()));
        await using var provider = BuildContainer(gatedAdapter);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var prepareTask = mediator.SendAsync(new PrepareRepositoryWorkspaceCommand(projectId), CancellationToken.None);

        await gatedAdapter.WaitUntilEnteredAsync().WaitAsync(GateTimeout);

        // The decisive assertion: while the external Git side effect is blocked mid-flight, an
        // INDEPENDENT DbContext/connection against the same database file must already see the
        // durable-intent commit. If PrepareRepositoryWorkspaceCommand were ever declared
        // ICommand<TResult> instead of IManualTransactionCommand<TResult>, the pipeline's
        // automatic transaction wrap would keep this first SaveChangesAsync's writes inside a
        // still-open ambient transaction here, invisible to this independent connection — and
        // both assertions below would fail.
        await using (var independentContext = CreateContext())
        {
            var committedWorkspace = await independentContext.GitWorkspaces
                .SingleOrDefaultAsync(w => w.ProjectId == projectId);
            Assert.NotNull(committedWorkspace);
            Assert.Equal(WorkspaceStatus.Preparing, committedWorkspace!.Status);

            var committedLease = await independentContext.RepositoryMutationLeases
                .SingleOrDefaultAsync(l => l.WorkspaceId == committedWorkspace.Id);
            Assert.NotNull(committedLease);
            Assert.Equal(LeaseStatus.Active, committedLease!.Status);
        }

        gatedAdapter.Release();
        var result = await prepareTask;

        Assert.True(result.IsSuccess);

        await using var finalContext = CreateContext();
        var finalWorkspace = await finalContext.GitWorkspaces.SingleAsync(w => w.ProjectId == projectId);
        Assert.Equal(WorkspaceStatus.Ready, finalWorkspace.Status);
        Assert.True(Directory.Exists(finalWorkspace.WorkspacePath));
    }
}
