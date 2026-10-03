using System.Data.Common;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// The explicit verification claim through the real mediator pipeline, the real EF Core SQLite provider and a file-backed
/// database. Failures are injected at the provider seams (transaction start, save, commit and just after commit) so a failure
/// before the durable commit, which must leave no execution and no consumed number, is distinguished from a failure after it,
/// which cannot prove that nothing was recorded.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class ClaimVerificationExecutionTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('b', 64);
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-claim-txn-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_competing_connection_committing_during_the_capture_is_refused_and_the_capture_holds_no_transaction()
    {
        var scene = await SeedAsync();
        var reader = new ObservingReader();
        reader.OnCapture = async () =>
        {
            // A second connection must be able to commit while the capture runs: no write lock is held across the observation.
            await using var other = CreateContext();
            await other.RepositoryMutationLeases.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
        };
        await using var provider = BuildContainer(reader, new PassThroughInterceptor());
        await using var scope = provider.CreateAsyncScope();

        var result = await SendAsync(scope, scene);

        Assert.True(reader.ObservedNoTransaction);
        Assert.True(result.IsFailure);
        Assert.Equal("verification.workspace_not_ready", result.Errors[0].Code);
        await AssertStateAsync(executions: 0, nextNumber: 1);
    }

    [Fact]
    public async Task An_unchanged_claim_commits_one_execution_and_the_counter_together()
    {
        var scene = await SeedAsync();
        await using var provider = BuildContainer(new ObservingReader(), new PassThroughInterceptor());
        await using var scope = provider.CreateAsyncScope();

        var result = await SendAsync(scope, scene);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ExecutionNumber);
        await AssertStateAsync(executions: 1, nextNumber: 2);
    }

    [Theory]
    [InlineData(FailureAt.TransactionStart)]
    [InlineData(FailureAt.Save)]
    [InlineData(FailureAt.BeforeCommit)]
    public async Task A_failure_before_the_durable_commit_leaves_no_execution_and_no_consumed_number(FailureAt failureAt)
    {
        var scene = await SeedAsync();
        await using var provider = BuildContainer(new ObservingReader(), new FailingInterceptor(failureAt));
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(scope, scene));

        await AssertStateAsync(executions: 0, nextNumber: 1);
    }

    [Fact]
    public async Task A_failure_after_the_durable_commit_surfaces_as_an_error_that_cannot_prove_nothing_was_recorded()
    {
        var scene = await SeedAsync();
        await using var provider = BuildContainer(new ObservingReader(), new FailingInterceptor(FailureAt.AfterCommit));
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(scope, scene));

        await AssertStateAsync(executions: 1, nextNumber: 2);
    }

    [Fact]
    public async Task A_cancellation_between_the_capture_and_the_transaction_leaves_nothing()
    {
        var scene = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        var reader = new ObservingReader();
        reader.OnCapture = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        await using var provider = BuildContainer(reader, new PassThroughInterceptor());
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(scope, scene, cancellation.Token));

        await AssertStateAsync(executions: 0, nextNumber: 1);
    }

    private Task<Devalente.Shared.Results.Result<ClaimVerificationExecutionCommandResult>> SendAsync(
        AsyncServiceScope scope, Scene scene, CancellationToken cancellationToken = default) =>
        scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ClaimVerificationExecutionCommand(scene.ProjectId, scene.RecipeId, scene.CheckpointId), cancellationToken);

    private async Task AssertStateAsync(int executions, int nextNumber)
    {
        await using var verify = CreateContext();
        Assert.Equal(executions, await verify.VerificationExecutions.CountAsync());
        Assert.Equal(nextNumber, await verify.Projects.Select(project => project.NextVerificationExecutionNumber).SingleAsync());
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private sealed record Scene(Guid ProjectId, Guid RecipeId, Guid CheckpointId);

    private async Task<Scene> SeedAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var recipe = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, Now);
        context.Projects.Add(project);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.Add(recipe);
        context.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        await context.SaveChangesAsync(CancellationToken.None);
        return new Scene(project.Id, recipe.Id, checkpoint.Id);
    }

    private ServiceProvider BuildContainer(ObservingReader reader, DbInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={databasePath}").AddInterceptors(interceptor));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<IGitWorkspaceEvidenceReader>(provider =>
        {
            reader.SetDbContext(provider.GetRequiredService<DevalCopilotDbContext>());
            return reader;
        });
        services.AddDevalenteMediator(typeof(ClaimVerificationExecutionCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    public enum FailureAt
    {
        TransactionStart,
        Save,
        BeforeCommit,
        AfterCommit,
    }

    private abstract class DbInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor;

    private sealed class PassThroughInterceptor : DbInterceptor;

    private sealed class FailingInterceptor(FailureAt failureAt) : DbInterceptor, ISaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default) =>
            failureAt == FailureAt.TransactionStart ? throw new InvalidOperationException("Simulated acquisition failure.") : base.TransactionStartingAsync(connection, eventData, result, cancellationToken);

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            failureAt == FailureAt.Save ? throw new InvalidOperationException("Simulated save failure.") : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            failureAt == FailureAt.BeforeCommit ? throw new InvalidOperationException("Simulated commit failure.") : base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            failureAt == FailureAt.AfterCommit ? throw new InvalidOperationException("Simulated failure after the durable commit.") : base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    private sealed class ObservingReader : IGitWorkspaceEvidenceReader
    {
        private DevalCopilotDbContext? dbContext;

        public bool ObservedNoTransaction { get; private set; }

        public Func<Task>? OnCapture { get; set; }

        public void SetDbContext(DevalCopilotDbContext context) => dbContext = context;

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            ObservedNoTransaction = dbContext?.Database.CurrentTransaction is null;
            if (OnCapture is not null)
            {
                await OnCapture();
            }

            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
