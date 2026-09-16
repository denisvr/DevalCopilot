using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;
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
public sealed class RecordCheckpointReviewTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-review-txn-{Guid.NewGuid():N}.db");

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
    public async Task Fresh_git_capture_runs_without_an_ambient_ef_transaction_through_the_real_mediator_pipeline()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddReviewEvidenceAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(
                data.ProjectId,
                data.CheckpointId,
                data.ExecutionId,
                ReviewActorKind.Human,
                ReviewDecision.Approved), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private async Task<(Guid ProjectId, Guid CheckpointId, Guid ExecutionId, string FingerprintSha256)> AddReviewEvidenceAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var fingerprint = new string('b', 64);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), fingerprint, []);
        var command = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, Now);
        execution.MarkDispatched(Now);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, fingerprint, Now);

        context.Projects.Add(project);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.Add(command);
        context.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        context.VerificationExecutions.Add(execution);
        await context.SaveChangesAsync(CancellationToken.None);
        return (project.Id, checkpoint.Id, execution.Id, fingerprint);
    }

    private ServiceProvider BuildContainer(TransactionObservingEvidenceReader evidenceReader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<IGitWorkspaceEvidenceReader>(provider =>
        {
            evidenceReader.SetDbContext(provider.GetRequiredService<DevalCopilotDbContext>());
            return evidenceReader;
        });
        services.AddDevalenteMediator(typeof(RecordCheckpointReviewCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class TransactionObservingEvidenceReader(string fingerprintSha256) : IGitWorkspaceEvidenceReader
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public string FingerprintSha256 => fingerprintSha256;

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            ObservedCurrentTransactionWasNull = _dbContext?.Database.CurrentTransaction is null;
            return Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success,
                new string('a', 40),
                fingerprintSha256,
                [],
                null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
