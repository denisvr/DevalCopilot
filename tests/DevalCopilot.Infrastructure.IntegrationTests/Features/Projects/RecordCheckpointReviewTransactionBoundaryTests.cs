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

    [Fact]
    public async Task A_checkpoint_committed_by_another_connection_during_capture_is_refused_through_the_real_mediator_pipeline()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddReviewEvidenceAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        evidenceReader.OnCapture = async () =>
        {
            // A second connection must be able to commit while the capture runs: no write lock is held across the observation.
            await using var other = CreateContext();
            var workspaceId = await other.GitWorkspaces.Select(workspace => workspace.Id).SingleAsync();
            other.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), workspaceId, 2, Now.AddMinutes(1), new string('a', 40), new string('d', 64), []));
            await other.SaveChangesAsync(CancellationToken.None);
        };
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(data.ProjectId, data.CheckpointId, null, ReviewActorKind.Human, ReviewDecision.Pending),
            CancellationToken.None);

        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);
        Assert.True(result.IsFailure);
        Assert.Equal("reviews.checkpoint_not_current", result.Errors[0].Code);
        await using var verify = CreateContext();
        Assert.Empty(verify.CheckpointReviews);
        Assert.Empty(verify.CheckpointReviewEvidence);
        Assert.Equal(2, await verify.GitCheckpoints.CountAsync());
    }

    [Fact]
    public async Task A_complete_set_is_recorded_atomically_through_the_real_mediator_pipeline_outside_any_ambient_transaction()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddCompleteSetEvidenceAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(
                data.ProjectId, data.CheckpointId, null, ReviewActorKind.Human, ReviewDecision.Approved, [data.LintExecutionId, data.UnitExecutionId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);
        await using var verify = CreateContext();
        var review = await verify.CheckpointReviews.Include(candidate => candidate.Evidence).SingleAsync();
        Assert.Equal(
            new[] { data.UnitExecutionId, data.LintExecutionId }.ToHashSet(),
            review.Evidence.Select(member => member.VerificationExecutionId).ToHashSet());
    }

    [Fact]
    public async Task A_newer_execution_committed_by_another_connection_during_capture_refuses_the_complete_set_through_the_real_pipeline()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddCompleteSetEvidenceAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        evidenceReader.OnCapture = async () =>
        {
            await using var other = CreateContext();
            var project = await other.Projects.SingleAsync();
            var workspace = await other.GitWorkspaces.SingleAsync();
            var checkpoint = await other.GitCheckpoints.SingleAsync();
            var lint = await other.VerificationCommands.SingleAsync(command => command.CommandNumber == 2);
            var newer = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 9, workspace, checkpoint, lint, Now);
            newer.MarkDispatched(Now);
            newer.Complete(VerificationExecutionOutcome.Exited, 1, data.FingerprintSha256, Now);
            other.VerificationExecutions.Add(newer);
            await other.SaveChangesAsync(CancellationToken.None);
        };
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(
                data.ProjectId, data.CheckpointId, null, ReviewActorKind.Human, ReviewDecision.Approved, [data.UnitExecutionId, data.LintExecutionId]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.approval_requires_complete_verification_set", result.Errors[0].Code);
        await using var verify = CreateContext();
        Assert.Empty(verify.CheckpointReviews);
        Assert.Empty(verify.CheckpointReviewEvidence);
    }

    [Fact]
    public async Task A_recipe_enabled_by_another_connection_during_capture_refuses_the_complete_set_through_the_real_pipeline()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddCompleteSetEvidenceAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        evidenceReader.OnCapture = async () =>
        {
            await using var other = CreateContext();
            var project = await other.Projects.SingleAsync();
            other.VerificationCommands.Add(VerificationCommand.Configure(Guid.NewGuid(), project.Id, 3, "Format", @"C:\dotnet.exe", ["format"], 60, true, Now));
            await other.SaveChangesAsync(CancellationToken.None);
        };
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(
                data.ProjectId, data.CheckpointId, null, ReviewActorKind.Human, ReviewDecision.Approved, [data.UnitExecutionId, data.LintExecutionId]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("reviews.approval_requires_complete_verification_set", result.Errors[0].Code);
        await using var verify = CreateContext();
        Assert.Empty(verify.CheckpointReviews);
        Assert.Empty(verify.CheckpointReviewEvidence);
    }

    [Fact]
    public async Task A_member_the_database_refuses_rolls_the_whole_multi_member_review_back_under_the_real_pipeline()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var data = await AddCompleteSetEvidenceAsync();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only trigger assembled from a constant and a test-owned identifier.
            trigger.CommandText =
                "CREATE TRIGGER trg_test_refuse_lint BEFORE INSERT ON checkpoint_review_evidence "
                + $"WHEN NEW.\"VerificationExecutionId\" = '{data.LintExecutionId.ToString().ToUpperInvariant()}' "
                + "BEGIN SELECT RAISE(ABORT, 'test_member_refused'); END;";
#pragma warning restore CA2100
            await trigger.ExecuteNonQueryAsync();
        }

        var evidenceReader = new TransactionObservingEvidenceReader(data.FingerprintSha256);
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new RecordCheckpointReviewCommand(
                data.ProjectId, data.CheckpointId, null, ReviewActorKind.Human, ReviewDecision.Approved, [data.UnitExecutionId, data.LintExecutionId]),
            CancellationToken.None));

        await using var verify = CreateContext();
        Assert.Empty(verify.CheckpointReviews);
        Assert.Empty(verify.CheckpointReviewEvidence);
    }

    private async Task<(Guid ProjectId, Guid CheckpointId, Guid UnitExecutionId, Guid LintExecutionId, string FingerprintSha256)> AddCompleteSetEvidenceAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var fingerprint = new string('b', 64);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), fingerprint, []);
        var unit = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var lint = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 2, "Lint", @"C:\dotnet.exe", ["format"], 60, true, Now);
        var unitRun = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, unit, Now);
        unitRun.MarkDispatched(Now);
        unitRun.Complete(VerificationExecutionOutcome.Exited, 0, fingerprint, Now);
        var lintRun = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 2, workspace, checkpoint, lint, Now);
        lintRun.MarkDispatched(Now);
        lintRun.Complete(VerificationExecutionOutcome.Exited, 0, fingerprint, Now);

        context.Projects.Add(project);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.AddRange(unit, lint);
        context.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        context.VerificationExecutions.AddRange(unitRun, lintRun);
        await context.SaveChangesAsync(CancellationToken.None);
        return (project.Id, checkpoint.Id, unitRun.Id, lintRun.Id, fingerprint);
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

        public Func<Task>? OnCapture { get; set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            ObservedCurrentTransactionWasNull = _dbContext?.Database.CurrentTransaction is null;
            if (OnCapture is not null)
            {
                await OnCapture();
            }

            return new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success,
                new string('a', 40),
                fingerprintSha256,
                [],
                null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
