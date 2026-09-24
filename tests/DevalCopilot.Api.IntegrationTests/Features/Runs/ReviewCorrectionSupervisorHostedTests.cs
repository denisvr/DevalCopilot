using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Hosted integration coverage for the real ReviewCorrectionSupervisor, real mediator,
/// and real EF transaction behavior. Only the provider adapter, evidence reader, artifact store,
/// and post-commit notifier are deterministic test boundaries.</summary>
public sealed class ReviewCorrectionSupervisorHostedTests : IDisposable
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string ChangedFingerprint = new('c', 64);
    private static readonly string Head = new('a', 40);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-review-supervisor-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-review-supervisor-artifacts-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public ReviewCorrectionSupervisorHostedTests() => _artifactStore = new FilesystemArtifactStore(_artifactRoot);

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot)) Directory.Delete(_artifactRoot, recursive: true);
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    [Fact]
    public async Task Applied_correction_is_recorded_once_and_invokes_the_adapter_once()
    {
        var evidence = new SequencedEvidence(call => call == 1 ? Matching() : Changed());
        var adapter = new GatedAdapter(_artifactStore);
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        adapter.FindingId = seed.FindingId;
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            adapter.Release();
            await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Completed);
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        var attempt = await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.CorrectionApplied, attempt.AgentOutcome);
        Assert.Single(context.CollaborationMessages.Where(item => item.AttemptId == seed.CorrectionId && item.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(2, context.CollaborationMessages.Count(item => item.AttemptId == seed.CorrectionId));
    }

    [Fact]
    public async Task Pre_dispatch_drift_never_invokes_the_provider()
    {
        var evidence = new SequencedEvidence(_ => Drifted());
        var adapter = new GatedAdapter(_artifactStore);
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Failed);
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(0, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        var attempt = await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task Provider_failure_after_mutation_records_failure_and_needs_attention()
    {
        var evidence = new SequencedEvidence(call => call == 1 ? Matching() : Changed());
        var adapter = new GatedAdapter(_artifactStore) { Result = new ReviewCorrectionInvocationResult(ImplementationInvocationOutcome.Failed, false, false, null) };
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            adapter.Release();
            await notifier.Notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        var attempt = await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Equal(WorkspaceStatus.NeedsAttention, await context.GitWorkspaces.Where(item => item.Id == seed.WorkspaceId).Select(item => item.Status).SingleAsync());
        Assert.Null(attempt.AgentResultGitCheckpointId);
    }

    [Fact]
    public async Task Non_zero_correction_exit_records_its_exit_code_with_the_provider_failure()
    {
        var evidence = new SequencedEvidence(call => call == 1 ? Matching() : Changed());
        var nonZeroExit = new ReviewCorrectionInvocationResult(
            ImplementationInvocationOutcome.Failed, false, false, null,
            new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromSeconds(5)));
        var adapter = new GatedAdapter(_artifactStore) { Result = nonZeroExit };
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            adapter.Release();
            await notifier.Notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        var attempt = await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Equal(WorkspaceStatus.NeedsAttention, await context.GitWorkspaces.Where(item => item.Id == seed.WorkspaceId).Select(item => item.Status).SingleAsync());
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(ProcessOutcome.Exited, attempt.AgentProcessOutcome);
        Assert.Equal(1, attempt.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromSeconds(5), attempt.AgentProcessDuration);
    }

    [Fact]
    public async Task No_changes_produced_is_terminal_and_the_adapter_is_not_repeated()
    {
        var evidence = new SequencedEvidence(_ => Matching());
        var adapter = new GatedAdapter(_artifactStore);
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        adapter.FindingId = seed.FindingId;
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await adapter.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            adapter.Release();
            await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Failed);
        }
        finally { await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }

        Assert.Equal(1, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        Assert.Equal(AgentOutcome.CorrectionNoChangesProduced, (await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId)).AgentOutcome);
    }

    [Fact]
    public async Task Provider_cancellation_is_recorded_without_a_second_invocation()
    {
        var evidence = new SequencedEvidence(_ => Matching());
        var adapter = new GatedAdapter(_artifactStore) { ThrowCancellation = true };
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try { await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Failed); }
        finally { await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }

        Assert.Equal(1, adapter.InvocationCount);
        await using var context = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, (await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId)).AgentOutcome);
    }

    [Fact]
    public async Task A_committed_duplicate_race_is_recorded_as_already_corrected_without_provider_invocation()
    {
        var evidence = new SequencedEvidence(_ => Matching());
        var adapter = new GatedAdapter(_artifactStore);
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var original = await db.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
            var duplicate = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), seed.RunId, 6, original.AgentGitWorkspaceId!.Value, original.AgentGitCheckpointId!.Value, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, DateTimeOffset.UtcNow, 6);
            duplicate.MarkAgentDispatched(DateTimeOffset.UtcNow);
            duplicate.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), DateTimeOffset.UtcNow, processEvidence: TestProcessEvidence.CleanExit);
            db.Attempts.Add(duplicate);
            var input = await db.AttemptInputMessages.Where(item => item.AttemptId == seed.CorrectionId).OrderBy(item => item.Sequence).Select(item => item.CollaborationMessageId).ToListAsync();
            db.AttemptInputMessages.AddRange(input.Select((id, index) => AttemptInputMessage.Record(Guid.NewGuid(), duplicate.Id, id, index)));
            await db.SaveChangesAsync();
        }

        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try { await WaitForStatusAsync(provider, seed.CorrectionId, AttemptStatus.Failed); }
        finally { await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }
        Assert.Equal(0, adapter.InvocationCount);
        await using var verify = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        Assert.Equal(AgentOutcome.InputAlreadyCorrected, (await verify.Attempts.SingleAsync(item => item.Id == seed.CorrectionId)).AgentOutcome);
    }

    [Fact]
    public async Task Restart_reconciliation_interrupts_a_dispatched_correction_without_invoking_the_provider()
    {
        var evidence = new SequencedEvidence(_ => Matching());
        var adapter = new GatedAdapter(_artifactStore);
        var notifier = new TestNotifier();
        await using var provider = BuildProvider(evidence, adapter, notifier);
        var seed = await SeedAsync(provider);
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var context = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var attempt = await context.Attempts.SingleAsync(item => item.Id == seed.CorrectionId);
            attempt.MarkAgentDispatched(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
            var result = await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var supervisor = CreateSupervisor(provider, adapter, evidence);
        await supervisor.StartAsync(CancellationToken.None);
        try { await Task.Delay(300); }
        finally { await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token); }
        Assert.Equal(0, adapter.InvocationCount);
        await using var verify = provider.GetRequiredService<IDbContextFactory<DevalCopilotDbContext>>().CreateDbContext();
        Assert.Equal(AttemptStatus.Interrupted, (await verify.Attempts.SingleAsync(item => item.Id == seed.CorrectionId)).Status);
    }

    private ServiceProvider BuildProvider(IGitWorkspaceEvidenceReader evidence, GatedAdapter adapter, TestNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddDbContextFactory<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IClaudeReviewCorrectionAdapter>(adapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<IRunEventNotifier>(notifier);
        services.AddDevalenteMediator(typeof(ReconcileInterruptedImplementationAttemptsCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(ReconcileInterruptedImplementationAttemptsCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private static ReviewCorrectionSupervisor CreateSupervisor(ServiceProvider provider, GatedAdapter adapter, IGitWorkspaceEvidenceReader evidence) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), adapter, evidence, provider.GetRequiredService<IArtifactStore>(), NullLogger<ReviewCorrectionSupervisor>.Instance);

    private async Task<Seed> SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Hosted correction", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", now); run.Claim(now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", Head, "main", now); workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), now);
        var capability = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now); capability.MarkDispatched(now); capability.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0", now, now.AddMinutes(5));
        var planning = Attempt.ClaimAgent(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 1); planning.MarkAgentDispatched(now); planning.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = CollaborationMessage.Record(Guid.NewGuid(), run.Id, planning.Id, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null, "Implement the correction.", JsonSerializer.Serialize(new { scope = "Correction", implementationSteps = "Apply findings.", risks = "None.", verificationPlan = "Run tests.", escalationPoints = "None." }), CollaborationMessageProvenance.ProviderObserved, now);
        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 2); acceptanceAttempt.MarkAgentDispatched(now); acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.Record(Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, proposal.Id, "Accepted the implementation plan.", JsonSerializer.Serialize(new { rationale = "The plan is complete." }), CollaborationMessageProvenance.ProviderObserved, now);
        var implementation = Attempt.ClaimAgentImplementation(Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 3); implementation.MarkAgentDispatched(now); implementation.CompleteImplementation(AgentOutcome.Implemented, checkpoint.Id, now, processEvidence: TestProcessEvidence.CleanExit);
        var report = CollaborationMessage.Record(Guid.NewGuid(), run.Id, implementation.Id, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id, "Implementation complete.", JsonSerializer.Serialize(new { completedWork = "Applied the implementation.", verification = "Tests passed." }), CollaborationMessageProvenance.ProviderObserved, now);
        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 4, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 4); review.MarkAgentDispatched(now); review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var finding = CollaborationMessage.Record(Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding, report.Id, "The branch needs correction.", JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The branch is incomplete.", requiredChange = "Complete the branch." }), CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(1));
        var manifestId = Guid.NewGuid();
        var correction = Attempt.ClaimAgentReviewCorrection(Guid.NewGuid(), run.Id, 5, workspace.Id, checkpoint.Id, Fingerprint, manifestId, TimeSpan.FromMinutes(20), 262144, 524288, now, 5);
        var manifestPath = _artifactStore.GetPartialPath(run.Id, correction.Id, ArtifactPurpose.AgentContextManifest); Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!); await File.WriteAllTextAsync(manifestPath, "manifest");
        var sealedManifest = await _artifactStore.SealAsync(run.Id, correction.Id, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        db.Projects.Add(project); db.Runs.Add(run); db.GitWorkspaces.Add(workspace); db.GitCheckpoints.Add(checkpoint); db.RepositoryMutationLeases.Add(lease); db.HostCapabilitySnapshots.Add(capability); db.Attempts.AddRange(planning, acceptanceAttempt, implementation, review, correction); db.CollaborationMessages.AddRange(proposal, acceptance, report, finding);
        db.AttemptInputMessages.AddRange(AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0), AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0), AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1), AttemptInputMessage.Record(Guid.NewGuid(), review.Id, report.Id, 0), AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, report.Id, 0), AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, finding.Id, 1));
        db.Artifacts.Add(Artifact.Record(manifestId, run.Id, correction.Id, ArtifactPurpose.AgentContextManifest, "application/json", sealedManifest!.RelativeStoragePath, sealedManifest.ContentHash, sealedManifest.ByteLength, false, ArtifactCaptureOutcome.Captured, ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
        await db.SaveChangesAsync();
        return new Seed(run.Id, correction.Id, workspace.Id, finding.Id);
    }

    private static async Task WaitForStatusAsync(ServiceProvider provider, Guid attemptId, AttemptStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts.Where(item => item.Id == attemptId).Select(item => item.Status).SingleAsync();
            if (status == expected) return;
            await Task.Delay(25);
        }
        await using var finalScope = provider.CreateAsyncScope();
        var final = await finalScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts.Where(item => item.Id == attemptId).Select(item => new { item.Status, item.AgentOutcome }).SingleAsync();
        throw new TimeoutException($"Attempt {attemptId} did not reach {expected}; actual {final.Status}/{final.AgentOutcome}.");
    }

    private static GitWorkspaceEvidenceResult Matching() => new(GitWorkspaceEvidenceOutcome.Success, Head, Fingerprint, [], null);
    private static GitWorkspaceEvidenceResult Changed() => new(GitWorkspaceEvidenceOutcome.Success, Head, ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], "diff");
    private static GitWorkspaceEvidenceResult Drifted() => new(GitWorkspaceEvidenceOutcome.Success, Head, new string('b', 64), [], null);

    private sealed record Seed(Guid RunId, Guid CorrectionId, Guid WorkspaceId, Guid FindingId);

    private sealed class SequencedEvidence(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
    {
        private int _calls;
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) => Task.FromResult(resultForCall(Interlocked.Increment(ref _calls)));
    }

    private sealed class GatedAdapter(IArtifactStore artifactStore) : IClaudeReviewCorrectionAdapter
    {
        private int _invocations;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount => Volatile.Read(ref _invocations);
        public ReviewCorrectionInvocationResult Result { get; set; } = new(ImplementationInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        public Guid FindingId { get; set; }
        public bool ThrowCancellation { get; set; }
        public void Release() => _release.TrySetResult();
        public async Task<ReviewCorrectionInvocationResult> InvokeAsync(ReviewCorrectionInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations); Invoked.TrySetResult();
            if (ThrowCancellation) throw new OperationCanceledException(cancellationToken);
            await WriteAsync(request, ArtifactPurpose.AgentFinalResponse, JsonSerializer.Serialize(new { revisionResponses = new[] { new { findingMessageId = FindingId, disposition = "Fixed", evidence = "The incomplete branch was corrected.", resultingSourceChanges = "Completed the branch." } }, executionReport = new { summary = "Correction complete.", changedRelativePaths = new[] { "src/Foo.cs" }, implementationNotes = "Applied the requested correction.", unexpectedDiscoveries = "None.", remainingRisks = "None.", recommendedVerification = "Run tests." } }), cancellationToken);
            await _release.Task.WaitAsync(cancellationToken);
            return Result;
        }
        private async Task WriteAsync(ReviewCorrectionInvocationRequest request, ArtifactPurpose purpose, string content, CancellationToken cancellationToken)
        {
            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, purpose); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, content, cancellationToken);
        }
    }

    private sealed class TestNotifier : IRunEventNotifier
    {
        public TaskCompletionSource Notified { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken) { Notified.TrySetResult(); return Task.CompletedTask; }
    }
}
