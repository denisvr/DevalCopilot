using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <see cref="ReconcileInterruptedImplementationAttemptsCommandHandler"/> through the
/// real mediator and EF Core transaction pipeline (mirroring
/// <c>RecoverInterruptedVerificationOutputArtifactsTransactionBoundaryTests</c>'s own fixture
/// style exactly) — the one way to actually prove Git evidence capture never runs inside an
/// ambient EF transaction, and that each attempt's own reconciliation is durably committed
/// independently of any later attempt's outcome.
/// </summary>
public sealed class ReconcileInterruptedImplementationAttemptsTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-reconcile-txn-{Guid.NewGuid():N}.db");

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
    public async Task CurrentTransaction_is_null_during_every_git_capture()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var seeded = await SeedDispatchedAttemptAsync();
        var reader = new TransactionObservingEvidenceReader();
        await using var provider = BuildContainer(reader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.True(reader.ObservedCurrentTransactionWasNull);
    }

    [Fact]
    public async Task An_evidence_reader_exception_for_a_later_attempt_never_undoes_an_earlier_attempts_durable_reconciliation()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var first = await SeedDispatchedAttemptAsync(headOffsetMinutes: 0);
        var second = await SeedDispatchedAttemptAsync(headOffsetMinutes: 1);
        var reader = new TransactionObservingEvidenceReader(
            matchingWorkspacePath: first.WorkspacePath,
            throwingWorkspacePath: second.WorkspacePath);
        await using var provider = BuildContainer(reader);
        await using var scope = provider.CreateAsyncScope();

        // The handler itself must never let a non-cancellation evidence-reader exception escape
        // as a startup-wide failure — the whole command still succeeds.
        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);

        await using var verifyContext = CreateContext();
        var firstAttempt = await verifyContext.Attempts.SingleAsync(a => a.Id == first.AttemptId);
        var secondAttempt = await verifyContext.Attempts.SingleAsync(a => a.Id == second.AttemptId);
        var firstWorkspace = await verifyContext.GitWorkspaces.SingleAsync(w => w.Id == first.WorkspaceId);
        var secondWorkspace = await verifyContext.GitWorkspaces.SingleAsync(w => w.Id == second.WorkspaceId);

        Assert.Equal(AttemptStatus.Interrupted, firstAttempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, firstWorkspace.Status);

        // The evidence-reader exception for the second attempt's workspace is treated exactly
        // like unavailable evidence: the attempt is still durably Interrupted, and — because a
        // dispatched attempt's worktree state can never be trusted without evidence — its
        // workspace is flagged NeedsAttention rather than silently left Ready.
        Assert.Equal(AttemptStatus.Interrupted, secondAttempt.Status);
        Assert.Equal(WorkspaceStatus.NeedsAttention, secondWorkspace.Status);
    }

    [Fact]
    public async Task An_undispatched_attempt_becomes_Interrupted_without_ever_flagging_its_workspace()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var seeded = await SeedUndispatchedAttemptAsync();
        var reader = new TransactionObservingEvidenceReader();
        await using var provider = BuildContainer(reader);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.False(reader.WasEverCalled);

        await using var verifyContext = CreateContext();
        var attempt = await verifyContext.Attempts.SingleAsync(a => a.Id == seeded.AttemptId);
        var workspace = await verifyContext.GitWorkspaces.SingleAsync(w => w.Id == seeded.WorkspaceId);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task Repeated_startup_reconciliation_is_idempotent()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var seeded = await SeedDispatchedAttemptAsync();
        var reader = new TransactionObservingEvidenceReader(matchingWorkspacePath: seeded.WorkspacePath);
        await using var provider = BuildContainer(reader);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var firstPass = await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);
        var secondPass = await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(firstPass.IsSuccess);
        Assert.Equal(1, firstPass.Value);
        Assert.True(secondPass.IsSuccess);
        Assert.Equal(0, secondPass.Value);

        await using var verifyContext = CreateContext();
        var attempt = await verifyContext.Attempts.SingleAsync(a => a.Id == seeded.AttemptId);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private async Task<(Guid AttemptId, Guid WorkspaceId, string WorkspacePath)> SeedDispatchedAttemptAsync(int headOffsetMinutes = 0)
    {
        var claimedAtUtc = Now.AddMinutes(headOffsetMinutes);
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Reconcile project", $@"C:\repos\{Guid.NewGuid():N}", claimedAtUtc);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", claimedAtUtc);
        run.Claim(claimedAtUtc);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", claimedAtUtc);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, claimedAtUtc, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), claimedAtUtc);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, claimedAtUtc);
        attempt.MarkAgentDispatched(claimedAtUtc);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync(CancellationToken.None);

        return (attempt.Id, workspace.Id, workspace.WorkspacePath);
    }

    private async Task<(Guid AttemptId, Guid WorkspaceId)> SeedUndispatchedAttemptAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Reconcile project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync(CancellationToken.None);

        return (attempt.Id, workspace.Id);
    }

    private ServiceProvider BuildContainer(TransactionObservingEvidenceReader evidenceReader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now.AddHours(1)));
        services.AddScoped<IGitWorkspaceEvidenceReader>(provider =>
        {
            evidenceReader.SetDbContext(provider.GetRequiredService<DevalCopilotDbContext>());
            return evidenceReader;
        });
        services.AddDevalenteMediator(typeof(ReconcileInterruptedImplementationAttemptsCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    /// <summary>A minimal, deterministic hand-rolled fake, mirroring
    /// <c>RecoverInterruptedVerificationOutputArtifactsTransactionBoundaryTests.TransactionObservingArtifactStore</c>
    /// exactly: records whether <see cref="DevalCopilotDbContext.Database"/>'s current
    /// transaction was ever non-null at capture time, and can be configured to throw for one
    /// specific workspace path while succeeding for another.</summary>
    private sealed class TransactionObservingEvidenceReader(
        string? matchingWorkspacePath = null, string? throwingWorkspacePath = null) : IGitWorkspaceEvidenceReader
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public bool WasEverCalled { get; private set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            WasEverCalled = true;
            ObservedCurrentTransactionWasNull ??= _dbContext?.Database.CurrentTransaction is null;

            if (throwingWorkspacePath == workspacePath)
            {
                throw new InvalidOperationException("Synthetic evidence-reader failure for this workspace.");
            }

            var fingerprint = workspacePath == matchingWorkspacePath ? Fingerprint : new string('z', 64);
            return Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprint, [], null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
