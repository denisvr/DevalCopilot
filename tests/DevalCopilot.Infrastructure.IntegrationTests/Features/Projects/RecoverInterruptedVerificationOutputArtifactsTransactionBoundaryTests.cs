using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class RecoverInterruptedVerificationOutputArtifactsTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-verification-recovery-txn-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Recovery_inspects_external_artifacts_without_an_ambient_transaction()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var execution = await AddExecutionAsync(Now);
        var store = new TransactionObservingArtifactStore(execution.ExecutionId);
        await using var provider = BuildContainer(store);
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecoverInterruptedVerificationOutputArtifactsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(store.ObservedCurrentTransactionWasNull);
    }

    [Fact]
    public async Task Later_external_failure_does_not_roll_back_an_earlier_execution_import()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var first = await AddExecutionAsync(Now);
        var second = await AddExecutionAsync(Now.AddMinutes(1));
        var store = new TransactionObservingArtifactStore(first.ExecutionId, second.ExecutionId);
        await using var provider = BuildContainer(store);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.SendAsync(
            new RecoverInterruptedVerificationOutputArtifactsCommand(), CancellationToken.None));

        await using var verifyContext = CreateContext();
        var imported = Assert.Single(verifyContext.VerificationOutputArtifacts);
        Assert.Equal(first.ExecutionId, imported.VerificationExecutionId);
        Assert.Equal(VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, imported.CaptureOutcome);
        Assert.Null(imported.Truncated);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private async Task<(Guid ProjectId, Guid ExecutionId)> AddExecutionAsync(DateTimeOffset claimedAtUtc)
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Recovery project", $@"C:\repos\{Guid.NewGuid():N}", claimedAtUtc);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", claimedAtUtc);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, claimedAtUtc, new string('a', 40), new string('b', 64), []);
        var command = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, claimedAtUtc);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, claimedAtUtc);
        execution.MarkDispatched(claimedAtUtc);

        context.Projects.Add(project);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.Add(command);
        context.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), claimedAtUtc));
        context.VerificationExecutions.Add(execution);
        await context.SaveChangesAsync(CancellationToken.None);
        return (project.Id, execution.Id);
    }

    private ServiceProvider BuildContainer(TransactionObservingArtifactStore artifactStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<IVerificationOutputArtifactStore>(provider =>
        {
            artifactStore.SetDbContext(provider.GetRequiredService<DevalCopilotDbContext>());
            return artifactStore;
        });
        services.AddDevalenteMediator(typeof(RecoverInterruptedVerificationOutputArtifactsCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class TransactionObservingArtifactStore(
        Guid successfulExecutionId,
        Guid? failingExecutionId = null) : IVerificationOutputArtifactStore
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public string GetPartialPath(Guid verificationExecutionId, VerificationOutputPurpose purpose) =>
            $@"C:\artifacts\{verificationExecutionId}\{purpose}.partial";

        public void DeletePartialFile(Guid verificationExecutionId, VerificationOutputPurpose purpose)
        {
        }

        public Task<SealedVerificationOutputFile?> SealAsync(
            Guid verificationExecutionId,
            VerificationOutputPurpose purpose,
            CancellationToken cancellationToken) => Task.FromResult<SealedVerificationOutputFile?>(null);

        public Task<SealedVerificationOutputFile?> DescribeSealedFileAsync(
            Guid verificationExecutionId,
            VerificationOutputPurpose purpose,
            CancellationToken cancellationToken)
        {
            ObservedCurrentTransactionWasNull ??= _dbContext?.Database.CurrentTransaction is null;
            if (failingExecutionId == verificationExecutionId)
            {
                throw new InvalidOperationException("Synthetic later artifact inspection failure.");
            }

            if (verificationExecutionId == successfulExecutionId && purpose == VerificationOutputPurpose.StandardOutput)
            {
                return Task.FromResult<SealedVerificationOutputFile?>(new(
                    $@"verifications\{verificationExecutionId}\stdout.sealed",
                    7,
                    "sha256:recovered"));
            }

            return Task.FromResult<SealedVerificationOutputFile?>(null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
