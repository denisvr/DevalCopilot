using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Starts the real <see cref="ImplementationReviewSupervisor"/> — the actual
/// <c>BackgroundService</c>, with its real scoped mediator and EF-transaction pipeline — faking
/// only the two real external boundaries: <see cref="ICodexImplementationReviewAdapter"/> (never a
/// real Codex CLI call) and <see cref="IGitWorkspaceEvidenceReader"/> (never a real Git
/// invocation). Every code-review attempt is seeded through the real production command chain — a
/// completed Codex Proposal, a completed Accepted Claude critical review, a completed Claude
/// implementation (producing the real result checkpoint), a passing verification execution against
/// that result checkpoint, then the real <see cref="CreateCodeReviewAttemptCommand"/> — rather than
/// constructing <see cref="Attempt"/> by hand. Mirrors <c>ChallengeResolutionSupervisorHostedTests</c>'s
/// hosting pattern, scoped down to the scenarios genuinely specific to this new stage, including
/// its own dedicated recording-timeout/failure regression (Section 1 of this slice's correction
/// pass) and its own dedicated already-reviewed dispatch-time race test.
/// </summary>
public sealed class ImplementationReviewSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminalPollTimeout = TimeSpan.FromSeconds(10);

    private static readonly string Fingerprint = new('a', 64);
    private static readonly string ResultFingerprint = new('b', 64);
    private static readonly string DriftedFingerprint = new('c', 64);

    private static readonly string ValidCodexProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static readonly string AcceptFinalResponseJson = JsonSerializer.Serialize(new
    {
        decision = ClaudeCriticalReviewOutputSchema.AcceptDecision,
        summary = "The plan is feasible as written.",
        rationale = "It correctly handles the ledger schema.",
    });

    private const string ChangedRelativePath = "src/Ledger.cs";

    private static string ApprovedFinalResponseJson() => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ApprovedOutcome,
        summary = "The implementation matches the plan.",
        rationale = "Every step from the plan was followed and verification passed.",
        residualRisks = "None material.",
        findings = Array.Empty<object>(),
    });

    private static string ChangesRequestedFinalResponseJson(int findingCount = 2) => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
        summary = "Two material issues were found.",
        rationale = (string?)null,
        residualRisks = (string?)null,
        findings = Enumerable.Range(1, findingCount).Select(index => new
        {
            severity = ImplementationReviewOutputSchema.Severities[0],
            category = ImplementationReviewOutputSchema.Categories[0],
            summary = $"Finding {index} summary",
            evidence = $"Evidence {index}",
            requiredChange = $"Required change {index}",
            affectedRelativePath = (string?)ChangedRelativePath,
        }),
    });

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-review-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-review-supervisor-hosted-artifacts-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public ImplementationReviewSupervisorHostedTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task An_approved_attempt_atomically_records_the_review_approval_checkpoint_review_and_its_evidence_exactly_once()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        await using var provider = BuildServiceProvider(evidenceReader, new FakeImplementationReviewAdapter(_artifactStore));
        var (_, attemptId, workspaceId, resultCheckpointId, executionId, commandId, executionReportId) =
            await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var adapter = (FakeImplementationReviewAdapter)provider.GetRequiredService<ICodexImplementationReviewAdapter>();
        adapter.FinalResponseJsonToWrite = ApprovedFinalResponseJson();

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Completed, status);

            // Several more poll ticks pass here while the attempt sits terminal, proving it is
            // never re-dispatched or re-invoked.
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ReviewApproved, persistedAttempt!.AgentOutcome);

        var approvalMessage = Assert.Single(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(CollaborationMessageType.ReviewApproval, approvalMessage.Type);
        Assert.Equal(executionReportId, approvalMessage.InReplyToMessageId);

        var checkpointReview = Assert.Single(dbContext.CheckpointReviews.Include(r => r.Evidence).Where(r => r.GitCheckpointId == resultCheckpointId));
        Assert.Equal(ReviewDecision.Approved, checkpointReview.Decision);
        Assert.Equal(ReviewActorKind.FutureAgent, checkpointReview.ActorKind);
        var evidenceMember = Assert.Single(checkpointReview.Evidence);
        Assert.Equal(executionId, evidenceMember.VerificationExecutionId);
        Assert.Equal(commandId, evidenceMember.VerificationCommandId);
        Assert.Equal(VerificationExecutionStatus.Passed, evidenceMember.VerificationExecutionStatus);

        // Exactly one sealed final-response artifact plus stdout/stderr, all readable back.
        var artifacts = dbContext.Artifacts.Where(a => a.AttemptId == attemptId).ToList();
        var finalResponseArtifact = Assert.Single(artifacts, a => a.Purpose == ArtifactPurpose.AgentFinalResponse);
        var window = await _artifactStore.VerifyAndReadSealedAsync(
            finalResponseArtifact.RelativeStoragePath, finalResponseArtifact.ByteLength, finalResponseArtifact.ContentHash,
            0, 32 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, window.Status);
        Assert.Contains(ImplementationReviewOutputSchema.ApprovedOutcome, window.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changes_requested_attempt_atomically_records_the_complete_bounded_finding_set_and_evidence()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ChangesRequestedFinalResponseJson(2) };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, resultCheckpointId, executionId, _, executionReportId) =
            await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Completed, status);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ReviewChangesRequested, persistedAttempt!.AgentOutcome);

        var findingMessages = dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId).OrderBy(m => m.Sequence).ToList();
        Assert.Equal(2, findingMessages.Count);
        Assert.All(findingMessages, message =>
        {
            Assert.Equal(CollaborationMessageType.ReviewFinding, message.Type);
            Assert.Equal(executionReportId, message.InReplyToMessageId);
            // The affected path is never duplicated into the durable ledger.
            Assert.DoesNotContain(ChangedRelativePath, message.StructuredContentJson);
        });

        var events = dbContext.Events.Where(e => e.AttemptId == attemptId).ToList();
        Assert.Equal(2, events.Count(e => e.EventType == RunEventType.CollaborationMessageRecorded));

        var checkpointReview = Assert.Single(dbContext.CheckpointReviews.Include(r => r.Evidence).Where(r => r.GitCheckpointId == resultCheckpointId));
        Assert.Equal(ReviewDecision.ChangesRequested, checkpointReview.Decision);
        var evidenceMember = Assert.Single(checkpointReview.Evidence);
        Assert.Equal(executionId, evidenceMember.VerificationExecutionId);

        Assert.Equal(1, adapter.InvocationCount);
    }

    [Fact]
    public async Task Pre_dispatch_source_drift_never_invokes_the_provider_and_resolves_to_source_changed()
    {
        // Seeding performs exactly 4 evidence captures: calls 1-3 (Codex-Proposal creation,
        // Claude-review creation, implementation creation) are still against the starting
        // checkpoint; call 4 (code-review creation) is against the real result checkpoint the
        // implementation just produced. Call 5 is the supervisor's own pre-dispatch capture —
        // the exact call this test drifts.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call switch
            {
                <= 3 => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint),
                4 => SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint),
                _ => SequencedGitWorkspaceEvidenceReader.Matching(DriftedFingerprint),
            });
        var adapter = new FakeImplementationReviewAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.SourceChanged, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Empty(dbContext.CheckpointReviews.Where(r => r.GitWorkspaceId == persistedAttempt.AgentGitWorkspaceId));
    }

    [Fact]
    public async Task Workspace_ineligibility_lost_before_dispatch_never_invokes_the_provider_and_reaches_a_truthful_terminal_outcome()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, workspaceId, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var lease = await dbContext.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == workspaceId && l.Status == LeaseStatus.Active);
            lease.Release(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, adapter.InvocationCount);

        await using var scope2 = provider.CreateAsyncScope();
        var dbContext2 = scope2.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext2.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.WorkspaceNoLongerEligible, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task A_failed_provider_invocation_is_recorded_as_provider_invocation_failed_without_retry()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationReviewInvocationResult(ImplementationReviewInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedAttempt!.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(1, adapter.InvocationCount);
    }

    [Fact]
    public async Task A_cancelled_provider_process_records_its_host_measured_evidence_with_the_provider_failure()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationReviewInvocationResult(
                ImplementationReviewInvocationOutcome.Failed, false, false, null,
                new AgentProcessEvidence(ProcessExecutionOutcome.Cancelled, null, TimeSpan.FromSeconds(3))),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedAttempt!.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(1, adapter.InvocationCount);
        Assert.Equal(ProcessOutcome.Cancelled, persistedAttempt.AgentProcessOutcome);
        Assert.Null(persistedAttempt.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromSeconds(3), persistedAttempt.AgentProcessDuration);
    }

    [Fact]
    public async Task An_invalid_final_response_is_recorded_as_invalid_structured_output()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = "{ not valid" };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedAttempt!.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(1, adapter.InvocationCount);
    }

    [Fact]
    public async Task An_attempt_marked_dispatched_but_never_recorded_becomes_interrupted_on_restart_and_the_provider_is_never_invoked()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));

        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider(evidenceReader, new FakeImplementationReviewAdapter(_artifactStore)))
        {
            (runId, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var dispatchResult = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            Assert.True(dispatchResult.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeImplementationReviewAdapter(_artifactStore);
        await using var reopenedProvider = BuildServiceProvider(evidenceReader, reopenedAdapter);
        await using (var migrateScope = reopenedProvider.CreateAsyncScope())
        {
            await migrateScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.MigrateAsync();
        }

        await using (var scope = reopenedProvider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var reconcileResult = await mediator.SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
            Assert.True(reconcileResult.IsSuccess);
            Assert.Equal(1, reconcileResult.Value);
        }

        var supervisor = CreateSupervisor(reopenedProvider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, reopenedAdapter.InvocationCount);

        await using var verificationScope = reopenedProvider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Interrupted, persistedAttempt!.Status);
        Assert.NotNull(persistedAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// Dispatch-time eligibility loss: a competing code-review attempt commits a successful
    /// (ReviewApproved) review of the exact same ExecutionReport-plus-verification-evidence input
    /// identity strictly between the supervisor's eligibility snapshot and its own
    /// <c>MarkAgentAttemptDispatchedCommand</c> call. That command's own CodeReviewer-specific
    /// revalidation — not the eligibility query's snapshot — is what closes this window: zero
    /// provider invocations, and the attempt is explicitly resolved to
    /// <see cref="AgentOutcome.InputAlreadyCodeReviewed"/>. Mirrors
    /// <c>ChallengeResolutionSupervisorHostedTests</c>'s own competing-resolution race test exactly,
    /// one level further down the collaboration protocol.
    /// </summary>
    [Fact]
    public async Task A_competing_review_committed_between_the_eligibility_snapshot_and_dispatch_never_invokes_the_provider()
    {
        var evidenceReader = new CompetingReviewCommittingPreDispatchEvidenceReader(_databasePath, Fingerprint, ResultFingerprint);
        var adapter = new FakeImplementationReviewAdapter(_artifactStore);

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, workspaceId, resultCheckpointId, executionId, commandId, executionReportId) =
            await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        // The real review attempt seeded above is itself #4 (Codex creation = 1, Claude review
        // creation = 2, implementation creation = 3, code-review creation = 4) — the competing
        // attempt must use a free attempt number.
        evidenceReader.Configure(runId, workspaceId, resultCheckpointId, executionReportId, commandId, executionId, competingAttemptNumber: 5);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.InputAlreadyCodeReviewed, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);

        var events = dbContext.Events.Where(e => e.AttemptId == attemptId).ToList();
        Assert.Single(events, e => e.EventType == RunEventType.AgentAttemptCompleted);
    }

    /// <summary>
    /// Section 1 of this slice's correction pass: a recording dispatch that times out or otherwise
    /// throws must never terminate the hosted service, never invent a terminal result, and must
    /// leave the attempt Running/Dispatched — available for restart reconciliation — without
    /// invoking the provider again. This uses a fault-injecting <see cref="IApplicationMediator"/>
    /// decorator to make the recording dispatch throw deterministically, rather than waiting out
    /// the supervisor's own real bounded 15-second timeout.
    /// </summary>
    [Fact]
    public async Task A_recording_dispatch_that_throws_never_terminates_the_supervisor_and_leaves_the_attempt_running_for_reconciliation()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Matching(ResultFingerprint));
        var adapter = new FakeImplementationReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ApprovedFinalResponseJson() };
        var faultInjector = new RecordingFaultInjector();
        await using var provider = BuildServiceProvider(evidenceReader, adapter, faultInjector);
        var (_, attemptId, _, _, _, _, _) = await SeedEligibleCodeReviewAttemptAsync(provider, evidenceReader);

        faultInjector.ThrowOnNextRecordCommand = true;

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            // Long enough for several poll ticks to elapse past the single fault-injected
            // recording attempt, proving the loop keeps running rather than dying with it.
            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            // The BackgroundService's own execute task must never have faulted — this is the
            // exact defect this test regresses: before the fix, the injected
            // OperationCanceledException-shaped failure would propagate out of ExecuteAsync's own
            // catch filter (which explicitly excludes OperationCanceledException) and fault the
            // entire hosted service.
            Assert.False(supervisor.ExecuteTask!.IsFaulted);
            Assert.False(supervisor.ExecuteTask!.IsCompleted);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        // StopAsync itself must complete without throwing, and the execute task must have ended
        // via the normal graceful-shutdown cancellation path — never faulted. (A task cancelled
        // by stoppingToken is expected and is not "successful" in Task terms; only IsFaulted
        // distinguishes the defect this test regresses from an ordinary graceful stop.)
        Assert.False(supervisor.ExecuteTask!.IsFaulted);

        Assert.Equal(1, faultInjector.RecordCommandAttemptCount);
        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);

        // No terminal result was invented: the attempt remains exactly as
        // MarkAgentAttemptDispatchedCommand left it before the failed recording attempt.
        Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
        Assert.NotNull(persistedAttempt.AgentDispatchedAtUtc);
        Assert.Null(persistedAttempt.AgentOutcome);

        // No partial review, findings, or collaboration events of any kind.
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Empty(dbContext.CheckpointReviews.Where(r => r.GitWorkspaceId == persistedAttempt.AgentGitWorkspaceId));
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attemptId));

        // The eligibility feed itself never re-selects a dispatched attempt (it filters on
        // AgentDispatchedAtUtc == null), so the provider is never invoked a second time on any
        // subsequent poll for this same attempt — already proven above by InvocationCount == 1
        // across the full 1.5-second polling window.
    }

    private ServiceProvider BuildServiceProvider(
        IGitWorkspaceEvidenceReader evidenceReader,
        ICodexImplementationReviewAdapter implementationReviewAdapter,
        RecordingFaultInjector? faultInjector = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(implementationReviewAdapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateCodeReviewAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateCodeReviewAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        if (faultInjector is not null)
        {
            var realMediatorDescriptor = services.Single(d => d.ServiceType == typeof(IApplicationMediator));
            services.Remove(realMediatorDescriptor);
            ((IList<ServiceDescriptor>)services).Add(ServiceDescriptor.Describe(
                typeof(IApplicationMediator),
                sp =>
                {
                    var real = realMediatorDescriptor.ImplementationFactory is { } factory
                        ? (IApplicationMediator)factory(sp)!
                        : (IApplicationMediator)ActivatorUtilities.CreateInstance(sp, realMediatorDescriptor.ImplementationType!);
                    return new FaultInjectingMediator(real, faultInjector);
                },
                realMediatorDescriptor.Lifetime));
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one fully eligible code-review attempt entirely through real production commands: a
    /// Ready workspace/checkpoint/lease, observed Codex and Claude capabilities, a completed Codex
    /// Proposal, a completed Accepted Claude critical review, a completed Claude implementation
    /// (producing the real result checkpoint the review must claim), one Passed verification
    /// execution against that exact result checkpoint, and finally the review attempt itself
    /// through <see cref="CreateCodeReviewAttemptCommand"/>.
    /// </summary>
    private async Task<(Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid ResultCheckpointId, Guid ExecutionId, Guid CommandId, Guid ExecutionReportId)>
        SeedEligibleCodeReviewAttemptAsync(ServiceProvider provider, IGitWorkspaceEvidenceReader evidenceReader)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var (runId, workspaceId, projectId) = await SeedRunWorkspaceCheckpointLeaseAsync(dbContext, "Implement and review the ledger proposal");
        await SeedClaudeCapabilityObservedAsync(dbContext);

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var createCodexResult = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
        Assert.True(createCodexResult.IsSuccess);
        var codexAttemptId = createCodexResult.Value.AttemptId;
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, codexAttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                runId, codexAttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ValidCodexProposalStructuredContentJson), null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        var proposalMessage = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == codexAttemptId && message.Type == CollaborationMessageType.Proposal);

        var createReviewResult = await mediator.SendAsync(
            new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessage.Id), CancellationToken.None);
        Assert.True(createReviewResult.IsSuccess);
        var reviewAttemptId = createReviewResult.Value.AttemptId;
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, reviewAttemptId), CancellationToken.None)).IsSuccess);

        var review = ClaudeCriticalReviewResponseParser.TryParse(AcceptFinalResponseJson);
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(runId, reviewAttemptId, AgentOutcome.Accepted, Fingerprint, [], review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        var createImplementationResult = await mediator.SendAsync(
            new CreateImplementationAttemptCommand(runId, proposalMessage.Id), CancellationToken.None);
        Assert.True(createImplementationResult.IsSuccess);
        var implementerAttemptId = createImplementationResult.Value.AttemptId;
        Assert.True(
            (await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, implementerAttemptId), CancellationToken.None)).IsSuccess);

        var report = ValidatedImplementationReport.Create(
            "Added the ledger table and its query.", [ChangedRelativePath], "Added table and query.", string.Empty, string.Empty, "dotnet test");
        var recordImplementationResult = await mediator.SendAsync(
            new RecordImplementationResultCommand(
                runId, implementerAttemptId, true, new string('a', 40), ResultFingerprint,
                [new GitWorkspaceChangedPath(ChangedRelativePath, null, "M", "M")], [], report, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);
        Assert.True(recordImplementationResult.IsSuccess);
        Assert.Equal(AgentOutcome.Implemented, recordImplementationResult.Value.Outcome);

        var resultCheckpoint = await dbContext.GitCheckpoints.SingleAsync(c => c.WorkspaceId == workspaceId && c.FingerprintSha256 == ResultFingerprint);
        var executionReport = await dbContext.CollaborationMessages.SingleAsync(
            m => m.AttemptId == implementerAttemptId && m.Type == CollaborationMessageType.ExecutionReport);

        var command = VerificationCommand.Configure(
            Guid.NewGuid(), projectId, 1, "Backend tests", @"C:\dotnet.exe", ["test"], 300, true, DateTimeOffset.UtcNow);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, 1, workspace, resultCheckpoint, command, DateTimeOffset.UtcNow);
        execution.MarkDispatched(DateTimeOffset.UtcNow);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, resultCheckpoint.FingerprintSha256, DateTimeOffset.UtcNow);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var createReviewAttemptResult = await mediator.SendAsync(
            new CreateCodeReviewAttemptCommand(runId, executionReport.Id), CancellationToken.None);
        Assert.True(createReviewAttemptResult.IsSuccess);

        return (
            runId, createReviewAttemptResult.Value.AttemptId, workspaceId, resultCheckpoint.Id, execution.Id, command.Id, executionReport.Id);
    }

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid ProjectId)> SeedRunWorkspaceCheckpointLeaseAsync(
        DevalCopilotDbContext dbContext, string objective)
    {
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), objective, now);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"{Path.GetTempPath()}devalcopilot-implementation-review-{Guid.NewGuid():N}",
            "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
        dbContext.RepositoryMutationLeases.Add(lease);

        if (!await dbContext.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            dbContext.HostCapabilitySnapshots.Add(codex);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run.Id, workspace.Id, project.Id);
    }

    private static async Task SeedClaudeCapabilityObservedAsync(DevalCopilotDbContext dbContext)
    {
        if (await dbContext.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.ClaudeCli))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
        claude.MarkDispatched(now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claude);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ImplementationReviewSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ICodexImplementationReviewAdapter>(),
        provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ImplementationReviewSupervisor>.Instance);

    private static async Task<AttemptStatus> PollForTerminalStatusAsync(ServiceProvider provider, Guid attemptId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(TerminalPollTimeout);
        AttemptStatus status;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await using var pollScope = provider.CreateAsyncScope();
            var dbContext = pollScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            status = (await dbContext.Attempts.FindAsync(attemptId))!.Status;
        }
        while (status == AttemptStatus.Running && DateTimeOffset.UtcNow < deadline);

        return status;
    }

    /// <summary>Mirrors <c>ChallengeResolutionSupervisorHostedTests.SequencedGitWorkspaceEvidenceReader</c>
    /// exactly.</summary>
    private sealed class SequencedGitWorkspaceEvidenceReader(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(resultForCall(Interlocked.Increment(ref _callCount)));

        public static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
            new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
    }

    /// <summary>
    /// Test-only seam reproducing "a competing code-review attempt commits a successful review of
    /// the exact same input identity strictly between the eligibility snapshot and
    /// <c>MarkAgentAttemptDispatchedCommand</c>'s own last-gate re-validation" with full
    /// determinism, no real threading. On the call number matching the supervisor's own
    /// pre-dispatch capture, it commits a second, already-ReviewApproved code-review attempt for
    /// the very same ExecutionReport-plus-verification-evidence identity via a fresh
    /// <see cref="DevalCopilotDbContext"/> opened directly against the same on-disk database file.
    /// Mirrors <c>ChallengeResolutionSupervisorHostedTests.CompetingResolutionCommittingPreDispatchEvidenceReader</c>.
    /// </summary>
    private sealed class CompetingReviewCommittingPreDispatchEvidenceReader(
        string databasePath, string startingFingerprintSha256, string fingerprintSha256)
        : IGitWorkspaceEvidenceReader
    {
        private int _callCount;
        private Guid _runId;
        private Guid _workspaceId;
        private Guid _checkpointId;
        private Guid _executionReportId;
        private Guid _verificationCommandId;
        private Guid _verificationExecutionId;
        private int _competingAttemptNumber;

        public void Configure(
            Guid runId, Guid workspaceId, Guid checkpointId, Guid executionReportId,
            Guid verificationCommandId, Guid verificationExecutionId, int competingAttemptNumber)
        {
            _runId = runId;
            _workspaceId = workspaceId;
            _checkpointId = checkpointId;
            _executionReportId = executionReportId;
            _verificationCommandId = verificationCommandId;
            _verificationExecutionId = verificationExecutionId;
            _competingAttemptNumber = competingAttemptNumber;
        }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);

            // Calls 1-3 (Codex-Proposal creation, Claude-review creation, implementation
            // creation) are still against the starting checkpoint; call 4 (code-review creation)
            // and every later call (the supervisor's own captures) are against the real result
            // checkpoint the implementation produced.
            if (call <= 3)
            {
                return new GitWorkspaceEvidenceResult(
                    GitWorkspaceEvidenceOutcome.Success, new string('a', 40), startingFingerprintSha256, [], null);
            }

            if (call == 5 && _competingAttemptNumber > 0)
            {
                var options = new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options;
                await using var freshDbContext = new DevalCopilotDbContext(options);

                var now = DateTimeOffset.UtcNow;
                var competing = Attempt.ClaimAgentCodeReview(
                    Guid.NewGuid(), _runId, _competingAttemptNumber, _workspaceId, _checkpointId, fingerprintSha256,
                    Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now);
                competing.MarkAgentDispatched(now);
                competing.CompleteAgent(AgentOutcome.ReviewApproved, fingerprintSha256, now, processEvidence: TestProcessEvidence.CleanExit);
                freshDbContext.Attempts.Add(competing);
                freshDbContext.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, _executionReportId, sequence: 0));
                freshDbContext.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                    Guid.NewGuid(), competing.Id, _verificationCommandId, _verificationExecutionId, sequence: 0));

                await freshDbContext.SaveChangesAsync(cancellationToken);
            }

            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
        }
    }

    /// <summary>Shared mutable fault-injection switch between a test and its
    /// <see cref="FaultInjectingMediator"/> decorator instance(s) — a test flips
    /// <see cref="ThrowOnNextRecordCommand"/> on before starting the supervisor, and the decorator
    /// consumes it exactly once.</summary>
    private sealed class RecordingFaultInjector
    {
        private int _recordCommandAttemptCount;

        public bool ThrowOnNextRecordCommand { get; set; }

        public int RecordCommandAttemptCount => Volatile.Read(ref _recordCommandAttemptCount);

        public bool TryConsumeFault()
        {
            if (!ThrowOnNextRecordCommand)
            {
                return false;
            }

            Interlocked.Increment(ref _recordCommandAttemptCount);
            ThrowOnNextRecordCommand = false;
            return true;
        }
    }

    /// <summary>
    /// Wraps the real <see cref="IApplicationMediator"/> so this suite can make exactly one
    /// <see cref="RecordImplementationReviewResultCommand"/> dispatch throw
    /// <see cref="OperationCanceledException"/> deterministically — reproducing the supervisor's
    /// own bounded recording-timeout token elapsing — without waiting out a real 15-second timer.
    /// Every other command and every query passes straight through to the real mediator.
    /// </summary>
    private sealed class FaultInjectingMediator(IApplicationMediator inner, RecordingFaultInjector faultInjector) : IApplicationMediator
    {
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
        {
            if (command is RecordImplementationReviewResultCommand && faultInjector.TryConsumeFault())
            {
                throw new OperationCanceledException("Simulated bounded recording-dispatch timeout.");
            }

            return inner.SendAsync(command, cancellationToken);
        }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken) =>
            inner.SendAsync(query, cancellationToken);
    }

    /// <summary>Mirrors <c>ChallengeResolutionSupervisorHostedTests.FakeChallengeResolutionAdapter</c>
    /// exactly, adapted for the Codex implementation-review port.</summary>
    private sealed class FakeImplementationReviewAdapter(IArtifactStore artifactStore) : ICodexImplementationReviewAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public string? FinalResponseJsonToWrite { get; set; }

        public ImplementationReviewInvocationResult ResultToReturn { get; set; } =
            new(ImplementationReviewInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);

        public async Task<ImplementationReviewInvocationResult> InvokeAsync(
            ImplementationReviewInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);

            if (FinalResponseJsonToWrite is { } content)
            {
                var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            var stdoutPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
            await File.WriteAllTextAsync(stdoutPath, string.Empty, cancellationToken);
            var stderrPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardError);
            Directory.CreateDirectory(Path.GetDirectoryName(stderrPath)!);
            await File.WriteAllTextAsync(stderrPath, string.Empty, cancellationToken);

            return ResultToReturn;
        }
    }
}
