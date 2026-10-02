using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
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
/// Drives the two verification-diagnosis claim commands (ADR-0018) through the real mediator and EF transaction pipeline
/// against file-backed SQLite, mirroring <see cref="CreateCodeReviewAttemptTransactionBoundaryTests"/>: both are manual
/// transaction commands, so their external Git evidence capture, output verification, and artifact sealing never run inside the
/// mediator's automatic per-command transaction.
/// </summary>
public sealed class VerificationDiagnosisTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 16, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-txn-{Guid.NewGuid():N}.db");

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
    public void Both_diagnosis_claim_commands_are_manual_transaction_commands()
    {
        Assert.True(typeof(IManualTransactionCommand<>).MakeGenericType(
            typeof(Devalente.Shared.Results.Result<CreateVerificationDiagnosisAttemptCommandResult>)).IsAssignableFrom(typeof(CreateVerificationDiagnosisAttemptCommand)));
        Assert.True(typeof(IManualTransactionCommand<>).MakeGenericType(
            typeof(Devalente.Shared.Results.Result<DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt.CreateDiagnosisCorrectionAttemptCommandResult>))
            .IsAssignableFrom(typeof(CreateDiagnosisCorrectionAttemptCommand)));
    }

    [Fact]
    public async Task The_diagnosis_claim_captures_git_evidence_without_an_ambient_transaction()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedEligibleRunAsync();
        var evidenceReader = new TransactionObservingEvidenceReader();
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(new CreateVerificationDiagnosisAttemptCommand(runId, Guid.NewGuid()), CancellationToken.None);

        // The fake reader reports a failed capture, so the handler fails closed right after the call this test observes.
        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Equal(1, evidenceReader.CallCount);
        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);
        await using var verify = CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId));
    }

    [Fact]
    public async Task The_diagnosis_origin_correction_captures_git_evidence_without_an_ambient_transaction()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runId = await SeedEligibleRunAsync();
        var evidenceReader = new TransactionObservingEvidenceReader();
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(new CreateDiagnosisCorrectionAttemptCommand(runId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Equal(1, evidenceReader.CallCount);
        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);
        await using var verify = CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.RunId == runId));
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

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
        services.AddScoped<IArtifactStore, UnreachedArtifactStore>();
        services.AddScoped<IAttemptDurabilityProbe>(provider =>
            new AttemptDurabilityProbe(provider.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteMediator(typeof(CreateVerificationDiagnosisAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private async Task<Guid> SeedEligibleRunAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Diagnosis transaction boundary", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Diagnose the failed verification", Now);
        run.Claim(Now);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codex.MarkDispatched(Now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        claude.MarkDispatched(Now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.2.3", Now, Now.AddMinutes(5));

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.RepositoryMutationLeases.Add(lease);
        context.GitCheckpoints.Add(checkpoint);
        context.HostCapabilitySnapshots.AddRange(codex, claude);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    private sealed class TransactionObservingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public int CallCount { get; private set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            CallCount++;
            ObservedCurrentTransactionWasNull = _dbContext?.Database.CurrentTransaction is null;
            return Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null));
        }
    }

    /// <summary>Never legitimately invoked here - the failed capture makes the handler return before any artifact work - so
    /// every member throws to surface an incorrect assumption immediately.</summary>
    private sealed class UnreachedArtifactStore : IArtifactStore
    {
        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new InvalidOperationException();

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
