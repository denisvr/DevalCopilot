using System.Diagnostics;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
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
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Drives <see cref="ReconcileWorkspacesCommand"/> through the real
/// <see cref="IApplicationMediator"/> and the real <c>Devalente.Shared.EntityFrameworkCore</c>
/// transaction pipeline — never a direct handler call — proving the same class of invariant
/// already proven for <see cref="PrepareRepositoryWorkspaceCommand"/> in
/// <c>PrepareRepositoryWorkspaceTransactionBoundaryTests</c>: no EF Core transaction is open on
/// the scoped <see cref="DevalCopilotDbContext"/> while external Git evidence is being gathered.
///
/// <para>
/// Unlike that test, no blocking gate or independent second connection is needed here — the
/// property under test (<c>Database.CurrentTransaction</c> is <see langword="null"/> at the
/// exact moment the external call happens) is directly observable, synchronously, on the very
/// same scoped context the handler itself is using. An
/// <see cref="TransactionObservingGitWorktreeAdapter"/> wraps the real adapter and records that
/// observation the first time it is called.
/// </para>
///
/// <para>
/// This test is meaningless — and was written to fail — if
/// <see cref="ReconcileWorkspacesCommand"/> is ever declared <c>ICommand&lt;TResult&gt;</c>
/// instead of <see cref="IManualTransactionCommand{TResult}"/>: only the manual-transaction
/// declaration keeps <c>AddDevalenteEfCoreTransactions</c> from opening an ambient transaction
/// before the handler's external Git/marker reads run. Empirically confirmed by temporarily
/// reverting the command's declared interface: the observed <c>CurrentTransaction</c> is then
/// non-null and this test fails.
/// </para>
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class ReconcileWorkspacesTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-reconcile-txn-{Guid.NewGuid():N}");
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-reconcile-txn-{Guid.NewGuid():N}.db");

    public ReconcileWorkspacesTransactionBoundaryTests()
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

    /// <summary>Wraps the real <see cref="GitWorktreeAdapter"/> and, on the first call made
    /// against it, records whether the scoped <see cref="DevalCopilotDbContext"/> it was
    /// constructed with currently has an open transaction — the exact moment reconciliation's
    /// external evidence-gathering begins.</summary>
    private sealed class TransactionObservingGitWorktreeAdapter(IGitWorktreeAdapter real, DevalCopilotDbContext dbContext) : IGitWorktreeAdapter
    {
        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public Task<GitWorktreeCreationResult> CreateAsync(
            string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation never creates a worktree.");

        public Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken)
        {
            RecordObservationOnce();
            return real.ResolveAdministrativeDirectoryAsync(mainRepositoryPath, workspacePath, cancellationToken);
        }

        public Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken)
        {
            RecordObservationOnce();
            return real.GetHeadCommitShaAsync(workspacePath, cancellationToken);
        }

        public Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
            string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken)
        {
            RecordObservationOnce();
            return real.IsRegisteredAsync(mainRepositoryPath, workspacePath, cancellationToken);
        }

        private void RecordObservationOnce()
        {
            ObservedCurrentTransactionWasNull ??= dbContext.Database.CurrentTransaction is null;
        }
    }

    /// <summary>Prepares one real, fully <c>Ready</c> workspace (real Git, real physical
    /// identity, real marker) using the already-proven <see cref="PrepareRepositoryWorkspaceCommandHandler"/>
    /// directly — this test is about reconciliation's own transaction boundary, not preparation's,
    /// which <c>PrepareRepositoryWorkspaceTransactionBoundaryTests</c> already covers.</summary>
    private async Task<Guid> SeedReadyWorkspaceAsync(string repoPath)
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "Test", repoPath, Now);
        var physicalIdentityInspector = new RepositoryPhysicalIdentityInspector();
        var identity = physicalIdentityInspector.Resolve(new RepositoryRootCandidate(repoPath));
        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, identity.Outcome);
        project.RecordPhysicalIdentityResolved(identity.VolumeSerialNumber!.Value, identity.FileId!);
        dbContext.Projects.Add(project);

        var gitReady = HostCapabilitySnapshot.Seed(Capability.Git, Now);
        gitReady.MarkDispatched(Now);
        gitReady.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.45.0", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(gitReady);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new PrepareRepositoryWorkspaceCommandHandler(
            dbContext,
            new RepositoryRootPathInspector(),
            physicalIdentityInspector,
            new GitRepositoryInspector(new ChildProcessExecutionAdapter()),
            new GitWorktreeAdapter(new ChildProcessExecutionAdapter()),
            new WorkspaceOwnershipMarkerStore(),
            new WorkspaceRootPathProvider(Path.Combine(_root, "workspaces")),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new PrepareRepositoryWorkspaceCommand(project.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);

        return project.Id;
    }

    private ServiceProvider BuildContainer(out TransactionObservingGitWorktreeAdapterFactory adapterFactory)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now.AddMinutes(1)));
        services.AddSingleton<IWorkspaceOwnershipMarkerStore, WorkspaceOwnershipMarkerStore>();

        var factory = new TransactionObservingGitWorktreeAdapterFactory();
        services.AddScoped<IGitWorktreeAdapter>(provider =>
        {
            var adapter = new TransactionObservingGitWorktreeAdapter(
                new GitWorktreeAdapter(new ChildProcessExecutionAdapter()), provider.GetRequiredService<DevalCopilotDbContext>());
            factory.Created = adapter;
            return adapter;
        });

        services.AddDevalenteMediator(typeof(ReconcileWorkspacesCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        adapterFactory = factory;
        return services.BuildServiceProvider();
    }

    /// <summary>Carries the <see cref="TransactionObservingGitWorktreeAdapter"/> instance
    /// created for whichever DI scope resolves it — captured by reference since the adapter
    /// itself is only constructed once a scope actually asks for it.</summary>
    private sealed class TransactionObservingGitWorktreeAdapterFactory
    {
        public TransactionObservingGitWorktreeAdapter? Created { get; set; }
    }

    [Fact]
    public async Task ReconcileWorkspaces_gathers_git_evidence_with_no_ambient_transaction_open()
    {
        var repoPath = CreateRepo("reconcile-txn-boundary");
        var projectId = await SeedReadyWorkspaceAsync(repoPath);

        await using var provider = BuildContainer(out var adapterFactory);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        // Resolving IGitWorktreeAdapter here (before dispatch) forces this scope's instance to
        // exist so it can be inspected afterward — the mediator, resolved from the same scope,
        // receives the identical scoped instance.
        scope.ServiceProvider.GetRequiredService<IGitWorktreeAdapter>();

        var result = await mediator.SendAsync(new ReconcileWorkspacesCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(adapterFactory.Created);
        Assert.True(adapterFactory.Created!.ObservedCurrentTransactionWasNull);

        await using var verifyContext = CreateContext();
        var workspace = await verifyContext.GitWorkspaces.SingleAsync(w => w.ProjectId == projectId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }
}
