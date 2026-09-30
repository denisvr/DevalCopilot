using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs;
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
/// Starts the real <see cref="ImplementationSupervisor"/> — the actual <c>BackgroundService</c>,
/// with its real scoped mediator and EF-transaction pipeline — faking only the two real external
/// boundaries: <see cref="IClaudeImplementationAdapter"/> (never a real Claude CLI call) and
/// <see cref="IGitWorkspaceEvidenceReader"/> (never a real Git invocation). Every implementation
/// attempt is seeded through the real production command chain — a completed Codex Proposal, a
/// completed Accepted Claude critical review, then the real
/// <see cref="CreateImplementationAttemptCommand"/> — rather than constructing
/// <see cref="Attempt"/> by hand. Mirrors <c>ChallengeResolutionSupervisorHostedTests</c>'s
/// hosting pattern exactly, scoped to the scenarios genuinely specific to this role: the
/// post-invocation evidence capture that always runs regardless of process outcome, and the
/// truthfulness rules unique to a role that can actually mutate the worktree.
/// </summary>
public sealed class ImplementationSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminalPollTimeout = TimeSpan.FromSeconds(10);

    private static readonly string StartingHeadSha = new('a', 40);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string DriftedFingerprint = new('b', 64);
    private static readonly string ChangedFingerprint = new('c', 64);

    private static readonly string ValidCodexProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static readonly string AcceptanceFinalResponseJson = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-implementation-supervisor-hosted-artifacts-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public ImplementationSupervisorHostedTests()
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

    private static string ValidImplementationReportJson(IReadOnlyList<string> changedRelativePaths) => JsonSerializer.Serialize(new
    {
        summary = "Implemented the ledger table and its query.",
        changedRelativePaths,
        implementationNotes = "Added the migration and the query handler.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the backend test suite.",
    });

    [Fact]
    public async Task The_dispatched_invocation_replays_the_claimed_attempts_alias_not_a_later_run_preference()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, "sonnet", "low");

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeModelRequest("opus", "high");
            await dbContext.SaveChangesAsync();
        }

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(AttemptStatus.Completed, await PollForTerminalStatusAsync(provider, attemptId));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(1, adapter.InvocationCount);
        Assert.Equal("sonnet", adapter.LastRequest!.RequestedClaudeModel);
        Assert.Equal("low", adapter.LastRequest!.RequestedClaudeEffort);
    }

    [Fact]
    public async Task A_run_without_a_request_dispatches_no_model_override()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(AttemptStatus.Completed, await PollForTerminalStatusAsync(provider, attemptId));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Null(adapter.LastRequest!.RequestedClaudeModel);
        Assert.Null(adapter.LastRequest!.RequestedClaudeEffort);
    }

    [Fact]
    public async Task A_successful_invocation_atomically_records_Implemented_one_checkpoint_changed_files_artifacts_and_one_execution_report()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]),
            StandardOutputToWrite = "claude stdout: implementation complete",
            StandardErrorToWrite = "claude stderr: no warnings",
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, proposalId) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

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

        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.Implemented, persistedAttempt!.AgentOutcome);
        Assert.NotNull(persistedAttempt.AgentResultGitCheckpointId);

        var newCheckpoint = await dbContext.GitCheckpoints.SingleAsync(c => c.Id == persistedAttempt.AgentResultGitCheckpointId);
        Assert.Equal(ChangedFingerprint, newCheckpoint.FingerprintSha256);
        var changedFile = Assert.Single(dbContext.GitChangedFiles.Where(f => f.CheckpointId == newCheckpoint.Id));
        Assert.Equal("src/Foo.cs", changedFile.Path);

        var executionReport = Assert.Single(
            dbContext.CollaborationMessages, m => m.AttemptId == attemptId && m.Type == CollaborationMessageType.ExecutionReport);
        Assert.Equal(proposalId, executionReport.InReplyToMessageId);
        Assert.Single(dbContext.Events.Where(e => e.AttemptId == attemptId && e.EventType == RunEventType.CollaborationMessageRecorded));

        // All three real artifact-store sinks the adapter wrote through are sealed and recorded
        // — exactly once each, under their own correct purpose — never duplicated, never
        // conflated with one another, and never merely asserted via unit-level artifact-store
        // coverage: this is the real FilesystemArtifactStore, driven end to end through the
        // hosted supervisor.
        var artifacts = dbContext.Artifacts.Where(a => a.AttemptId == attemptId).ToList();
        var finalResponseArtifact = Assert.Single(artifacts, a => a.Purpose == ArtifactPurpose.AgentFinalResponse);
        var standardOutputArtifact = Assert.Single(artifacts, a => a.Purpose == ArtifactPurpose.AgentStandardOutput);
        var standardErrorArtifact = Assert.Single(artifacts, a => a.Purpose == ArtifactPurpose.AgentStandardError);

        Assert.Equal(ArtifactCaptureOutcome.Captured, finalResponseArtifact.CaptureOutcome);
        Assert.Equal(ArtifactCaptureOutcome.Captured, standardOutputArtifact.CaptureOutcome);
        Assert.Equal(ArtifactCaptureOutcome.Captured, standardErrorArtifact.CaptureOutcome);

        var sealedFinalResponse = await _artifactStore.VerifyAndReadSealedAsync(
            finalResponseArtifact.RelativeStoragePath, finalResponseArtifact.ByteLength, finalResponseArtifact.ContentHash,
            fromOffset: 0, maxBytes: 4096, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, sealedFinalResponse.Status);
        Assert.Equal(ValidImplementationReportJson(["src/Foo.cs"]), sealedFinalResponse.Text);

        var sealedStandardOutput = await _artifactStore.VerifyAndReadSealedAsync(
            standardOutputArtifact.RelativeStoragePath, standardOutputArtifact.ByteLength, standardOutputArtifact.ContentHash,
            fromOffset: 0, maxBytes: 4096, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, sealedStandardOutput.Status);
        Assert.Equal("claude stdout: implementation complete", sealedStandardOutput.Text);

        var sealedStandardError = await _artifactStore.VerifyAndReadSealedAsync(
            standardErrorArtifact.RelativeStoragePath, standardErrorArtifact.ByteLength, standardErrorArtifact.ContentHash,
            fromOffset: 0, maxBytes: 4096, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, sealedStandardError.Status);
        Assert.Equal("claude stderr: no warnings", sealedStandardError.Text);
    }

    [Fact]
    public async Task Pre_dispatch_source_drift_never_invokes_the_provider_and_resolves_to_source_changed()
    {
        // Seeding performs exactly 3 evidence captures (Codex-Proposal creation, Claude-review
        // creation, implementation-attempt creation); call 4 is the supervisor's own pre-dispatch
        // capture — the exact call this test drifts.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call => call <= 3 ? Matching(Fingerprint) : Matching(DriftedFingerprint));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

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
    }

    [Fact]
    public async Task A_process_failure_after_a_worktree_mutation_flags_needs_attention_and_creates_no_result_checkpoint()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationInvocationResult(ImplementationInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

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

        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentResultGitCheckpointId);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task A_timed_out_implementation_process_records_its_evidence_and_still_flags_the_mutated_workspace()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationInvocationResult(
                ImplementationInvocationOutcome.Failed, false, false, null,
                ProcessEvidence: new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, TimeSpan.FromMinutes(20))),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

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

        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentResultGitCheckpointId);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal(ProcessOutcome.TimedOut, persistedAttempt.AgentProcessOutcome);
        Assert.Null(persistedAttempt.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromMinutes(20), persistedAttempt.AgentProcessDuration);
    }

    [Fact]
    public async Task Invalid_structured_output_after_a_worktree_mutation_flags_needs_attention_and_creates_no_result_checkpoint()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = "this is not valid json at all { [ garbled",
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

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

        Assert.Equal(1, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentResultGitCheckpointId);

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task A_recording_failure_leaves_the_attempt_running_and_never_causes_a_second_invocation_on_later_polls()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        // An overlong provider session id makes RecordImplementationResultCommandHandler itself
        // return Result.Failure ("agent_attempts.provider_session_id_too_long") before ever
        // completing the attempt — the attempt is left durably Running, exactly as a genuine
        // recording-command failure would leave it.
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]),
            ResultToReturn = new ImplementationInvocationResult(ImplementationInvocationOutcome.Exited, false, false, new string('s', 300), ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            // The eligibility feed only ever considers undispatched attempts, so once dispatch
            // succeeds this attempt is structurally invisible to every later poll regardless of
            // whether recording itself succeeds — several more ticks pass here to prove that in
            // practice, not merely by construction.
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
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
        Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
        Assert.NotNull(persistedAttempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task An_attempt_dispatched_but_never_recorded_becomes_interrupted_on_restart_and_the_provider_is_never_invoked()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => Matching(Fingerprint));

        Guid runId;
        Guid attemptId;
        Guid workspaceId;

        await using (var provider = BuildServiceProvider(evidenceReader, new FakeClaudeImplementationAdapter(_artifactStore)))
        {
            (runId, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var dispatchResult = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            Assert.True(dispatchResult.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeClaudeImplementationAdapter(_artifactStore);
        var reopenedEvidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => Matching(Fingerprint));
        await using var reopenedProvider = BuildServiceProvider(reopenedEvidenceReader, reopenedAdapter);
        await using (var migrateScope = reopenedProvider.CreateAsyncScope())
        {
            await migrateScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.MigrateAsync();
        }

        await using (var scope = reopenedProvider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var reconcileResult = await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);
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

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    private async Task RunSupervisorToTerminalAsync(ServiceProvider provider, Guid attemptId)
    {
        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await PollForTerminalStatusAsync(provider, attemptId);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task The_dispatched_invocation_replays_the_claimed_attempts_turn_limit_not_a_later_run_change()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]) };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 12);

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeMaxTurns(99);
            await dbContext.SaveChangesAsync();
        }

        await RunSupervisorToTerminalAsync(provider, attemptId);

        Assert.Equal(1, adapter.InvocationCount);
        Assert.Equal(12, adapter.LastRequest!.RequestedMaxTurns);
        Assert.Equal(ClaudeMutationAdapterContract.ImplementationV2, adapter.LastRequest.AdapterContractVersion);
    }

    [Fact]
    public async Task A_run_without_a_turn_limit_dispatches_none_under_the_v2_contract()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]) };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

        await RunSupervisorToTerminalAsync(provider, attemptId);

        Assert.Null(adapter.LastRequest!.RequestedMaxTurns);
        Assert.Equal(ClaudeMutationAdapterContract.ImplementationV2, adapter.LastRequest.AdapterContractVersion);
    }

    [Fact]
    public async Task An_undispatched_claimed_attempt_replays_its_turn_limit_after_a_restart_despite_a_later_run_change()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => Matching(Fingerprint));
        Guid runId;
        Guid attemptId;
        await using (var provider = BuildServiceProvider(evidenceReader, new FakeClaudeImplementationAdapter(_artifactStore)))
        {
            (runId, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 7);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeClaudeImplementationAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]) };
        var reopenedEvidence = new SequencedGitWorkspaceEvidenceReader(call =>
            call == 1
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        await using var reopenedProvider = BuildServiceProvider(reopenedEvidence, reopenedAdapter);
        await using (var scope = reopenedProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.MigrateAsync();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeMaxTurns(null);
            await dbContext.SaveChangesAsync();
        }

        await RunSupervisorToTerminalAsync(reopenedProvider, attemptId);

        Assert.Equal(1, reopenedAdapter.InvocationCount);
        Assert.Equal(7, reopenedAdapter.LastRequest!.RequestedMaxTurns);
    }

    [Fact]
    public async Task A_failed_invocation_with_a_turn_limit_and_an_unchanged_source_fails_without_success_retry_or_extra_budget()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => Matching(Fingerprint));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationInvocationResult(ImplementationInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 3);
        await using var seedScope = provider.CreateAsyncScope();
        var agentAttemptsBefore = await seedScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts
            .CountAsync(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent);

        await RunSupervisorToTerminalAsync(provider, attemptId);
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        Assert.Equal(1, adapter.InvocationCount);
        Assert.Equal(3, adapter.LastRequest!.RequestedMaxTurns);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == attemptId && message.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(WorkspaceStatus.Ready, (await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId)).Status);
        Assert.Equal(agentAttemptsBefore, await dbContext.Attempts.CountAsync(candidate => candidate.RunId == runId && candidate.Kind == AttemptKind.Agent));
        Assert.Equal(3, attempt.AgentRequestedMaxTurns);
    }

    [Fact]
    public async Task A_failed_invocation_with_a_turn_limit_and_a_changed_source_flags_needs_attention_with_no_success_checkpoint()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationInvocationResult(ImplementationInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 3);

        await RunSupervisorToTerminalAsync(provider, attemptId);

        Assert.Equal(1, adapter.InvocationCount);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == attemptId && message.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId)).Status);
    }

    [Fact]
    public async Task A_failed_invocation_with_a_turn_limit_and_unavailable_completion_evidence_is_never_trusted_as_unchanged()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore)
        {
            ResultToReturn = new ImplementationInvocationResult(ImplementationInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, workspaceId, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 3);

        await RunSupervisorToTerminalAsync(provider, attemptId);

        Assert.Equal(1, adapter.InvocationCount);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId)).Status);
    }

    public static IEnumerable<object[]> CapBoundaryTampering =>
    [
        ["UPDATE attempts SET AgentPermissionProfile = 'ReadOnly' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentPermissionProfile = 'Unknown' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentAdapterContractVersion = 'claude-implementation-v1' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentAdapterContractVersion = 'claude-review-correction-v2' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 3.5 WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = X'37' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 'blob:37' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 9e999 WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentProvider = 'Codex' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 4294967297 WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 'abc' WHERE Id = '{0}'"],
        ["UPDATE attempts SET AgentRequestedMaxTurns = 101 WHERE Id = '{0}'"],
    ];

    [Theory]
    [MemberData(nameof(CapBoundaryTampering))]
    public async Task A_cap_beside_incoherent_or_malformed_persisted_state_never_starts_the_provider(string tamperSqlTemplate)
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => Matching(Fingerprint));
        var adapter = new FakeClaudeImplementationAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidImplementationReportJson(["src/Foo.cs"]) };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader, requestedMaxTurns: 7);
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(string.Format(tamperSqlTemplate, attemptId.ToString().ToUpperInvariant()));
        }

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(0, adapter.InvocationCount);
        await using var verify = provider.CreateAsyncScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts
            .AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Null(persisted.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
    }

    private static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
        new(GitWorkspaceEvidenceOutcome.Success, StartingHeadSha, fingerprintSha256, [], null);

    private static GitWorkspaceEvidenceResult MatchingWithChangedPaths(string fingerprintSha256, IReadOnlyList<GitWorkspaceChangedPath> changedPaths) =>
        new(GitWorkspaceEvidenceOutcome.Success, StartingHeadSha, fingerprintSha256, changedPaths, null);

    private ServiceProvider BuildServiceProvider(
        IGitWorkspaceEvidenceReader evidenceReader,
        IClaudeImplementationAdapter implementationAdapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(implementationAdapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one fully eligible implementation attempt entirely through real production commands:
    /// a Ready workspace/checkpoint/lease, observed Codex and Claude capabilities, a completed
    /// Codex Proposal, a completed Accepted Claude critical review, and finally the
    /// implementation attempt itself through <see cref="CreateImplementationAttemptCommand"/>.
    /// </summary>
    private async Task<(Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid ProposalId)> SeedEligibleImplementationAttemptAsync(
        ServiceProvider provider, IGitWorkspaceEvidenceReader evidenceReader, string? requestedClaudeModel = null, string? requestedClaudeEffort = null,
        int? requestedMaxTurns = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var (runId, workspaceId, _) = await SeedRunWorkspaceCheckpointLeaseAsync(dbContext, "Implement the ledger table and its query");
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

        var review = ClaudeCriticalReviewResponseParser.TryParse(AcceptanceFinalResponseJson);
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(runId, reviewAttemptId, AgentOutcome.Accepted, Fingerprint, [], review, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        if (requestedClaudeModel is not null || requestedMaxTurns is not null)
        {
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeModelRequest(requestedClaudeModel, requestedClaudeEffort);
            run.SetRequestedClaudeMaxTurns(requestedMaxTurns);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var createImplementationResult = await mediator.SendAsync(
            new CreateImplementationAttemptCommand(runId, proposalMessage.Id), CancellationToken.None);
        Assert.True(createImplementationResult.IsSuccess);

        return (runId, createImplementationResult.Value.AttemptId, workspaceId, proposalMessage.Id);
    }

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid LeaseId)> SeedRunWorkspaceCheckpointLeaseAsync(
        DevalCopilotDbContext dbContext, string objective)
    {
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), objective, now);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"{Path.GetTempPath()}devalcopilot-implementation-{Guid.NewGuid():N}",
            "branch", StartingHeadSha, "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, StartingHeadSha, Fingerprint, []);
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

        return (run.Id, workspace.Id, lease.Id);
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
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\claude.exe", null, "2.1.276", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claude);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ImplementationSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IClaudeImplementationAdapter>(),
        provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ImplementationSupervisor>.Instance);

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
    }

    /// <summary>Mirrors <c>ChallengeResolutionSupervisorHostedTests.FakeChallengeResolutionAdapter</c>
    /// exactly, adapted for the Claude implementation port.</summary>
    private sealed class FakeClaudeImplementationAdapter(IArtifactStore artifactStore) : IClaudeImplementationAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public ImplementationInvocationRequest? LastRequest { get; private set; }

        public string? FinalResponseJsonToWrite { get; set; }

        public string? StandardOutputToWrite { get; set; }

        public string? StandardErrorToWrite { get; set; }

        public ImplementationInvocationResult ResultToReturn { get; set; } =
            new(ImplementationInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);

        public async Task<ImplementationInvocationResult> InvokeAsync(
            ImplementationInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            LastRequest = request;

            await WritePartialAsync(request, ArtifactPurpose.AgentFinalResponse, FinalResponseJsonToWrite, cancellationToken);
            await WritePartialAsync(request, ArtifactPurpose.AgentStandardOutput, StandardOutputToWrite, cancellationToken);
            await WritePartialAsync(request, ArtifactPurpose.AgentStandardError, StandardErrorToWrite, cancellationToken);

            return ResultToReturn;
        }

        private async Task WritePartialAsync(
            ImplementationInvocationRequest request, ArtifactPurpose purpose, string? content, CancellationToken cancellationToken)
        {
            if (content is null)
            {
                return;
            }

            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, purpose);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
    }

}
