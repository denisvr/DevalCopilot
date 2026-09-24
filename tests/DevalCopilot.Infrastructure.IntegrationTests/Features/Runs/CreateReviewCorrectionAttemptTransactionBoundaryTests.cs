using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Drives <see cref="CreateReviewCorrectionAttemptCommand"/> through the real mediator and
/// EF transaction pipeline against file-backed SQLite. The evidence reader observes the actual
/// handler DbContext so the external Git boundary is proven, rather than inferred from the
/// handler's state after it returns.
/// </summary>
public sealed class CreateReviewCorrectionAttemptTransactionBoundaryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-correction-txn-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-correction-artifacts-{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Evidence_capture_runs_without_an_ambient_transaction_and_claim_persists_afterward()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var seed = await SeedAsync();
        var evidenceReader = new TransactionObservingEvidenceReader(seed.Evidence);
        await using var provider = BuildContainer(evidenceReader);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var result = await mediator.SendAsync(
            new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join("; ", result.Errors.Select(error => error.Code)) : null);
        Assert.True(evidenceReader.ObservedCurrentTransactionWasNull);

        await using var verify = CreateContext();
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var attempt = await verify.Attempts.SingleAsync(item => item.Id == created.AttemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal(AgentProvider.ClaudeCode, attempt.AgentProvider);

        var inputMessageIds = await verify.AttemptInputMessages
            .Where(item => item.AttemptId == attempt.Id)
            .OrderBy(item => item.Sequence)
            .Select(item => item.CollaborationMessageId)
            .ToListAsync();
        Assert.Equal(new[] { seed.ExecutionReportId, seed.FindingId }, inputMessageIds);

        var manifest = await verify.Artifacts.SingleAsync(item =>
            item.AttemptId == attempt.Id && item.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(ArtifactCaptureOutcome.Captured, manifest.CaptureOutcome);
        Assert.False(string.IsNullOrWhiteSpace(manifest.RelativeStoragePath));
        Assert.True(manifest.ByteLength > 0);
        Assert.False(string.IsNullOrWhiteSpace(manifest.ContentHash));
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
        services.AddSingleton<IArtifactStore>(new FilesystemArtifactStore(_artifactRoot));
        services.AddDevalenteMediator(typeof(CreateReviewCorrectionAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Correction transaction boundary", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), Now);
        var capability = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        capability.MarkDispatched(Now);
        capability.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", Now, Now.AddMinutes(5));

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        planningAttempt.MarkAgentDispatched(Now);
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Implement the requested correction.", JsonSerializer.Serialize(new { scope = "Correction", implementationSteps = "Apply the finding.", risks = "None.", verificationPlan = "Run tests.", escalationPoints = "None." }),
            CollaborationMessageProvenance.ProviderObserved, Now);

        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        acceptanceAttempt.MarkAgentDispatched(Now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, proposal.Id,
            "Accepted the implementation plan.", JsonSerializer.Serialize(new { rationale = "The plan is complete." }),
            CollaborationMessageProvenance.ProviderObserved, Now);

        var implementation = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        implementation.MarkAgentDispatched(Now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, checkpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        var executionReport = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, implementation.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the requested correction.", JsonSerializer.Serialize(new { completedWork = "Updated the implementation.", verification = "Tests passed." }),
            CollaborationMessageProvenance.ProviderObserved, Now);

        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 4, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var finding = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding, executionReport.Id,
            "The branch needs correction.", JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The branch is incomplete.", requiredChange = "Complete the branch." }),
            CollaborationMessageProvenance.ProviderObserved, Now.AddSeconds(1));

        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        context.HostCapabilitySnapshots.Add(capability);
        context.Attempts.AddRange(planningAttempt, acceptanceAttempt, implementation, review);
        context.CollaborationMessages.AddRange(proposal, acceptance, executionReport, finding);
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0));
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0));
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1));
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, executionReport.Id, 0));
        await context.SaveChangesAsync(CancellationToken.None);

        return new Seed(run.Id, review.Id, executionReport.Id, finding.Id,
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [new GitWorkspaceChangedPath("src/Changed.cs", null, "M", " ")], "diff"));
    }

    private sealed record Seed(Guid RunId, Guid ReviewId, Guid ExecutionReportId, Guid FindingId, GitWorkspaceEvidenceResult Evidence);

    private sealed class TransactionObservingEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
    {
        private DevalCopilotDbContext? _dbContext;

        public bool? ObservedCurrentTransactionWasNull { get; private set; }

        public void SetDbContext(DevalCopilotDbContext dbContext) => _dbContext = dbContext;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            ObservedCurrentTransactionWasNull = _dbContext?.Database.CurrentTransaction is null;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
