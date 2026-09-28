using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Drives <see cref="SetCodexAssignmentPreferenceCommand"/> through the real mediator and EF
/// transaction pipeline against file-backed SQLite, mirroring
/// <c>CreateReviewCorrectionAttemptTransactionBoundaryTests</c>'s own established pattern. The
/// fake catalog adapter observes the actual handler <see cref="DevalCopilotDbContext"/> so the
/// external Codex App Server boundary is proven never to run inside an open EF transaction,
/// rather than inferred from the handler's state after it returns.
/// </summary>
public sealed class SetCodexAssignmentPreferenceTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-assignment-preference-txn-{Guid.NewGuid():N}.db");

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
    public async Task Catalog_observation_runs_without_an_ambient_transaction_and_preference_and_event_persist_together()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedAsync();
        var catalogAdapter = new TransactionObservingCatalogAdapter();
        await using var provider = BuildContainer(catalogAdapter);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(
            new SetCodexAssignmentPreferenceCommand(runId, "gpt-6-sol", "high"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.True(catalogAdapter.ObservedCurrentTransactionWasNull);
        Assert.Equal(1, catalogAdapter.CallCount);

        await using var verify = CreateContext();
        var run = await verify.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal("gpt-6-sol", run.RequestedCodexModel);
        Assert.Equal("high", run.RequestedCodexEffort);

        var recordedEvent = await verify.Events.SingleAsync(candidate => candidate.RunId == runId);
        Assert.Equal(RunEventType.CodexAssignmentPreferenceChanged, recordedEvent.EventType);
    }

    [Fact]
    public async Task Clearing_the_preference_never_invokes_the_catalog_adapter()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedAsync();
        var catalogAdapter = new TransactionObservingCatalogAdapter();
        await using var provider = BuildContainer(catalogAdapter);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(
            new SetCodexAssignmentPreferenceCommand(runId, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.Equal(0, catalogAdapter.CallCount);
    }

    /// <summary>
    /// Models a lifecycle transition landing in the window between this handler's own untracked
    /// pre-check and its authoritative fresh read: a genuinely separate connection commits the Run
    /// to <see cref="RunLifecycle.Failed"/> from inside the fake catalog adapter's own
    /// <see cref="ICodexModelCatalogAdapter.ObserveAsync"/> call — the same real external-boundary
    /// call the handler awaits before ever performing that fresh read. The handler's own fresh read
    /// afterward must observe the committed transition and reject the request, persisting neither
    /// the preference nor its event.
    /// </summary>
    [Fact]
    public async Task A_lifecycle_transition_committed_during_catalog_observation_is_rejected_and_persists_nothing()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedAsync();
        var catalogAdapter = new LifecycleMutatingCatalogAdapter(_databasePath, runId, Now);
        await using var provider = BuildContainer(catalogAdapter);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(
            new SetCodexAssignmentPreferenceCommand(runId, "gpt-6-sol", "high"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.RunNotEditableCode, result.Errors.Single().Code);
        Assert.Equal(1, catalogAdapter.CallCount);

        await using var verify = CreateContext();
        var run = await verify.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
        Assert.Null(run.RequestedCodexModel);
        Assert.Null(run.RequestedCodexEffort);
        Assert.False(await verify.Events.AnyAsync(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.CodexAssignmentPreferenceChanged));
    }

    /// <summary>
    /// Proves the narrower race the fresh read alone cannot close: a lifecycle transition
    /// committed by a wholly separate <see cref="DevalCopilotDbContext"/> in the gap between this
    /// handler's own authoritative read and its own single <c>SaveChangesAsync</c>, with no further
    /// I/O of the handler's own in between to re-observe it. This drives the same two EF contexts
    /// directly (not through the mediator, since no catalog-adapter hook exists inside that exact
    /// gap) to prove <c>Run.Lifecycle</c>'s concurrency-token configuration — not an explicit
    /// multi-statement transaction — is what makes the handler's own save fail closed instead of
    /// silently overwriting the newer committed state.
    /// </summary>
    [Fact]
    public async Task A_lifecycle_transition_between_the_authoritative_read_and_the_save_throws_concurrency_conflict_and_persists_nothing()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedAsync();

        await using var handlerSideContext = CreateContext();
        var runOnHandlerSide = await handlerSideContext.Runs.SingleAsync(candidate => candidate.Id == runId);

        await using (var concurrentContext = CreateContext())
        {
            var runOnConcurrentSide = await concurrentContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            runOnConcurrentSide.Fail(Now.AddMinutes(1));
            await concurrentContext.SaveChangesAsync(CancellationToken.None);
        }

        runOnHandlerSide.SetRequestedCodexAssignment("gpt-6-sol", "high");
        handlerSideContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(), runId, attemptId: null, RunEventType.CodexAssignmentPreferenceChanged,
            ParticipantIdentity.ForHuman(), "{}", Now.AddMinutes(2)));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => handlerSideContext.SaveChangesAsync(CancellationToken.None));

        await using var verify = CreateContext();
        var run = await verify.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
        Assert.Null(run.RequestedCodexModel);
        Assert.Null(run.RequestedCodexEffort);
        Assert.False(await verify.Events.AnyAsync(candidate => candidate.RunId == runId && candidate.EventType == RunEventType.CodexAssignmentPreferenceChanged));
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private ServiceProvider BuildContainer(TransactionObservingCatalogAdapter catalogAdapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<ICodexModelCatalogAdapter>(provider =>
        {
            catalogAdapter.SetDbContext(provider.GetRequiredService<DevalCopilotDbContext>());
            return catalogAdapter;
        });
        services.AddDevalenteMediator(typeof(SetCodexAssignmentPreferenceCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildContainer(ICodexModelCatalogAdapter catalogAdapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped(_ => catalogAdapter);
        services.AddDevalenteMediator(typeof(SetCodexAssignmentPreferenceCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private async Task<Guid> SeedAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Assignment preference transaction boundary", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Request an explicit Codex model", Now);
        run.Claim(Now);
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codex.MarkDispatched(Now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.HostCapabilitySnapshots.Add(codex);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    private sealed class TransactionObservingCatalogAdapter : ICodexModelCatalogAdapter
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public int CallCount { get; private set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public Task<CodexModelCatalogObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken)
        {
            CallCount++;
            ObservedCurrentTransactionWasNull = _dbContext?.Database.CurrentTransaction is null;
            return Task.FromResult(new CodexModelCatalogObservation(
                true, Now, [new CodexModelCatalogEntry("gpt-6-sol", "GPT-6 Sol", ["medium", "high"], "medium")]));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// A fake external catalog boundary that, at the exact moment the real handler would be
    /// awaiting the Codex App Server child process, instead opens a genuinely separate
    /// <see cref="DevalCopilotDbContext"/> against the same file-backed SQLite database and commits
    /// a real, independent lifecycle transition for the target run — modeling another concurrent
    /// mediator command finishing while this one's external observation is still in flight.
    /// </summary>
    private sealed class LifecycleMutatingCatalogAdapter(string databasePath, Guid runId, DateTimeOffset now) : ICodexModelCatalogAdapter
    {
        public int CallCount { get; private set; }

        public async Task<CodexModelCatalogObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken)
        {
            CallCount++;

            await using var concurrentContext = new DevalCopilotDbContext(
                new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);
            var run = await concurrentContext.Runs.SingleAsync(candidate => candidate.Id == runId, cancellationToken);
            run.Fail(now);
            await concurrentContext.SaveChangesAsync(cancellationToken);

            return new CodexModelCatalogObservation(
                true, now, [new CodexModelCatalogEntry("gpt-6-sol", "GPT-6 Sol", ["medium", "high"], "medium")]);
        }
    }
}
