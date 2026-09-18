using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.Startup;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Starts the real <see cref="AgentAttemptSupervisor"/> — the actual <c>BackgroundService</c>,
/// with its real scoped mediator and EF-transaction pipeline (the same composition
/// <c>Program.cs</c> registers) — and exercises the real startup <see cref="AgentAttemptOutputRecovery"/>
/// step, faking only the two real external boundaries: <see cref="ICodexPlanningAdapter"/> (never
/// a real Codex CLI, npm, npx, or network call) and <see cref="IGitWorkspaceEvidenceReader"/>
/// (never a real Git invocation). Every Agent attempt is seeded through the real
/// <see cref="CreateCodexPlanningAttemptCommand"/> — the same production path that creates one —
/// rather than constructing <see cref="Attempt"/> by hand, so eligibility is driven by the real
/// Domain/Application rules, not a test-only shortcut. Mirrors
/// <c>ProcessAttemptSupervisorHostedTests</c>'s hosting pattern exactly, adapted for the Agent
/// attempt feature.
/// </summary>
public sealed class AgentAttemptSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The supervisor's own poll interval is 500ms (see <c>AgentAttemptSupervisor.PollInterval</c>);
    /// this is a generous multiple of that, bounding how long a test waits for a terminal
    /// attempt status before giving up.</summary>
    private static readonly TimeSpan TerminalPollTimeout = TimeSpan.FromSeconds(10);

    private static readonly string Fingerprint = new('a', 64);

    private static readonly string ValidFinalResponseJson = JsonSerializer.Serialize(new
    {
        summary = "Add the ledger table and its query.",
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-supervisor-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-supervisor-hosted-workspace-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public AgentAttemptSupervisorHostedTests()
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

    /// <summary>
    /// Scenario 1: a successful, validated Proposal is recorded exactly once across repeated
    /// polls — the eligible attempt is picked up, dispatched, invoked once, and the resulting
    /// Proposal is never appended a second time even though several more poll ticks elapse after
    /// completion.
    /// </summary>
    [Fact]
    public async Task A_successful_validated_proposal_is_recorded_exactly_once_across_repeated_polls()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCodexPlanningAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidFinalResponseJson,
            StandardOutputToWrite = "codex stdout",
        };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Completed, status);

            // The supervisor polls every 500ms; several more ticks pass here while the attempt
            // sits in its terminal state, to prove it is never picked up again.
            await Task.Delay(TimeSpan.FromMilliseconds(1600));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Completed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.Proposed, persistedAttempt.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.RunId == runId).ToList();
        var message = Assert.Single(messages);
        Assert.Equal(attemptId, message.AttemptId);
        Assert.Equal(CollaborationMessageType.Proposal, message.Type);

        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// Scenario 2 (narrowed, as the task description anticipates): forcing the exact "recording
    /// fails after a successful invocation" race deterministically is hard, so this replaces the
    /// real <c>RecordAgentAttemptResultCommand</c> handler with one that captures its
    /// cancellation token and never completes on its own — a stand-in for a stalled/failed
    /// recording write. The real supervisor's dispatch marker is committed durably before the
    /// adapter is ever invoked, so even though recording the terminal result then hangs (and is
    /// eventually cancelled by the supervisor's own bounded <c>RecordingTimeout</c>) while many
    /// more 500ms poll ticks elapse, the provider is proven to be invoked at most once for this
    /// attempt — the same "recording failure never causes redispatch" property scenario 2 asks
    /// for, reproduced through the real hosted composition rather than a real induced write
    /// failure.
    /// </summary>
    [Fact]
    public async Task Recording_failure_does_not_cause_the_provider_to_be_invoked_a_second_time()
    {
        var capturedTokenSource = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recordingCancelledSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter, services =>
        {
            services.RemoveAll<IRequestHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>>();
            services.AddScoped<IRequestHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>>(
                _ => new StallingRecordAgentAttemptResultCommandHandler(capturedTokenSource, recordingCancelledSource));
        });

        var (_, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var recordingToken = await capturedTokenSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(recordingToken.IsCancellationRequested);

            // AgentAttemptSupervisor.RecordingTimeout is 10s; this bound is a safety net for the
            // test itself, not the assertion.
            var recordingWasCancelled = await recordingCancelledSource.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(recordingWasCancelled);

            // Several more poll ticks (every 500ms) pass here while the attempt sits
            // Running-but-dispatched, to prove it is never picked up — and the provider never
            // invoked — again.
            await Task.Delay(TimeSpan.FromMilliseconds(1600));

            Assert.Equal(1, adapter.InvocationCount);

            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
            Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
            Assert.NotNull(persistedAttempt.AgentDispatchedAtUtc);
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    /// <summary>
    /// Scenario 3: loss of the active workspace mutation lease strictly after claim but before
    /// the supervisor's poll tick ever runs invokes the provider zero times, and the attempt
    /// still reaches a terminal <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> outcome via
    /// the new <c>GetIneligibleAgentAttemptsQuery</c>/<c>RecordAgentAttemptWorkspaceIneligibleCommand</c>
    /// path rather than being left <c>Running</c> forever. Releasing the lease is one of three
    /// equivalent triggers (Ready workspace / active lease / current checkpoint) the real query
    /// watches for; the other two are already covered at the Application layer by
    /// <c>GetIneligibleAgentAttemptsQueryHandlerTests</c>, so this proves the same gate holds
    /// through the real hosted composition rather than repeating all three permutations here.
    /// </summary>
    [Fact]
    public async Task Loss_of_the_active_lease_before_dispatch_never_invokes_the_provider_and_reaches_workspace_no_longer_eligible()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, leaseId) = await SeedEligibleAgentAttemptAsync(provider);

        await using (var mutationScope = provider.CreateAsyncScope())
        {
            var dbContext = mutationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var lease = await dbContext.RepositoryMutationLeases.SingleAsync(candidate => candidate.Id == leaseId);
            lease.Release(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            // Several more poll ticks pass here to prove the provider is never invoked
            // afterward either.
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext2 = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext2.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.WorkspaceNoLongerEligible, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
        Assert.Equal(0, adapter.InvocationCount);
    }

    /// <summary>
    /// Correction A: <c>MarkAgentAttemptDispatchedCommandHandler</c> is the authoritative last
    /// gate, not <c>GetEligibleAgentAttemptsQuery</c>'s own snapshot. This reproduces eligibility
    /// lost strictly between the supervisor's pre-dispatch evidence capture and its own
    /// <c>MarkAgentAttemptDispatchedCommand</c> call — distinct from
    /// <see cref="Loss_of_the_active_lease_before_dispatch_never_invokes_the_provider_and_reaches_workspace_no_longer_eligible"/>
    /// above, which releases the lease before the supervisor's poll tick even begins (so the
    /// attempt is already excluded by the eligibility query itself and never reaches pre-dispatch
    /// capture at all). Here the attempt IS selected as eligible and evidence capture DOES run
    /// and succeed; only then, in the gap the correction targets, does the lease disappear —
    /// proving the handler's own re-validation, not the query snapshot, is what closes this
    /// window. Zero provider invocations, and the attempt is explicitly resolved rather than left
    /// to poll forever.
    /// </summary>
    [Fact]
    public async Task Loss_of_the_active_lease_between_the_pre_dispatch_evidence_capture_and_the_dispatch_marker_commit_never_invokes_the_provider()
    {
        var evidenceReader = new LeaseReleasingPreDispatchEvidenceReader(_databasePath, Fingerprint);
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, leaseId) = await SeedEligibleAgentAttemptAsync(provider);
        evidenceReader.LeaseId = leaseId;

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            // Several more poll ticks pass here to prove the provider is never invoked afterward
            // either.
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
        Assert.Equal(AgentOutcome.WorkspaceNoLongerEligible, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// Scenario 4: a successful process-level exit whose post-invocation Git evidence capture
    /// fails (no fresh fingerprint to confirm the checkpoint is still current) must never be
    /// recorded as <see cref="AgentOutcome.Proposed"/> — it downgrades to
    /// <see cref="AgentOutcome.CheckpointEvidenceUnavailable"/> instead, and no
    /// <see cref="CollaborationMessage"/> is ever appended.
    /// </summary>
    [Fact]
    public async Task A_failed_post_invocation_evidence_capture_never_produces_a_proposed_outcome()
    {
        // Call 1 is CreateCodexPlanningAttemptCommand's own claim-time capture, call 2 is the
        // supervisor's pre-dispatch capture — both must match for the attempt to ever be
        // dispatched — and call 3 is the supervisor's post-invocation capture, which is made to
        // fail here.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 2
                ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
                : SequencedGitWorkspaceEvidenceReader.Failure);
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

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
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, persistedAttempt!.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == runId));
        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// Correction B (case 1): a pre-dispatch <see cref="IGitWorkspaceEvidenceReader.CaptureAsync"/>
    /// call that RETURNS a failure outcome must never be silently retried on the next 500ms poll
    /// tick — it resolves the attempt to a truthful terminal outcome instead, via the new
    /// <c>RecordAgentAttemptCheckpointEvidenceUnavailableCommand</c>, so it is never eligible
    /// again. Call 1 is the seeding command's own claim-time capture (which must succeed for the
    /// attempt to exist at all); every call from 2 onward — starting with the supervisor's own
    /// pre-dispatch capture — is made to fail. Several poll ticks elapse after the attempt
    /// terminates to prove the capture is invoked only once on the supervisor's behalf, never once
    /// per tick.
    /// </summary>
    [Fact]
    public async Task A_returned_pre_dispatch_evidence_failure_is_never_retried_and_resolves_to_checkpoint_evidence_unavailable()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call == 1 ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint) : SequencedGitWorkspaceEvidenceReader.Failure);
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            // The supervisor polls every 500ms; several more ticks pass here while the attempt
            // sits terminal, to prove the failing capture is never retried.
            await Task.Delay(TimeSpan.FromMilliseconds(2000));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        // Call 1 was seeding's own claim-time capture; exactly one further call was made on the
        // supervisor's behalf (its pre-dispatch capture) — never once per poll tick.
        Assert.Equal(2, evidenceReader.CallCount);
        Assert.Equal(0, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// Correction B (case 2): a pre-dispatch <see cref="IGitWorkspaceEvidenceReader.CaptureAsync"/>
    /// call that THROWS a plain <see cref="Exception"/> (not <see cref="OperationCanceledException"/>)
    /// must be classified exactly like a returned failure by
    /// <c>AgentAttemptSupervisor.CaptureEvidenceSafelyAsync</c> — never left to propagate out and
    /// abort the whole poll iteration, and never silently retried. Proves the same three outcomes
    /// as the returned-failure case above, reached through the thrown-exception path instead.
    /// </summary>
    [Fact]
    public async Task A_thrown_pre_dispatch_evidence_exception_is_classified_like_a_failure_and_never_retried()
    {
        var evidenceReader = new ThrowingAfterFirstCaptureGitWorkspaceEvidenceReader(Fingerprint);
        var adapter = new FakeCodexPlanningAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            // Several more poll ticks pass here to prove a thrown exception is never left to
            // abort the iteration and silently retry on the next tick.
            await Task.Delay(TimeSpan.FromMilliseconds(2000));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        Assert.Equal(2, evidenceReader.CallCount);
        Assert.Equal(0, adapter.InvocationCount);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// Scenario 5: an attempt already durably marked dispatched (the real execution-start claim)
    /// but with no terminal result ever recorded — simulating a host that dispatched then
    /// crashed before recording anything, without ever starting the real supervisor for it in
    /// this provider — becomes <see cref="AttemptStatus.Interrupted"/> once
    /// <c>ReconcileInterruptedAgentAttemptsCommand</c> runs against a fresh provider, and the real
    /// supervisor started against that same fresh provider never invokes the provider for it
    /// (it is no longer <c>Running</c>, so it is never eligible again).
    /// </summary>
    [Fact]
    public async Task An_attempt_marked_dispatched_but_never_recorded_becomes_interrupted_on_restart_and_the_provider_is_never_invoked()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));

        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider(evidenceReader, new FakeCodexPlanningAdapter(_artifactStore)))
        {
            (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

            // Simulates the host's own execution-start claim, durably committed just as the
            // real supervisor would before ever invoking the provider — then a crash before the
            // provider was invoked or any result recorded. The real supervisor is never started
            // in this provider at all, so this is deterministic rather than racing a real
            // invocation mid-flight.
            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var dispatchResult = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            Assert.True(dispatchResult.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeCodexPlanningAdapter(_artifactStore);
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
            // Several poll ticks (every 500ms) pass here to prove the real supervisor never
            // invokes the provider for an attempt that is no longer Running.
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
    /// Scenarios 6 and 7 together: a sealed-but-unrecorded Agent artifact — left behind exactly
    /// as scenario 2 leaves one, since sealing happens unconditionally before the (here, stalled)
    /// recording dispatch — is imported by the real <see cref="AgentAttemptOutputRecovery"/>
    /// startup step. Running recovery twice proves idempotency (scenario 6): exactly one
    /// <see cref="Artifact"/> row exists afterward, with <c>Truncated == null</c> (genuinely
    /// unknown, never invented) and <see cref="ArtifactCaptureOutcome.PartialHostInterrupted"/>.
    /// It also proves scenario 7: the recorded event is <see cref="RunEventType.AgentOutputRecovered"/>,
    /// never <see cref="RunEventType.AgentAttemptCompleted"/>, and the owning attempt's
    /// <see cref="AttemptStatus"/> is still <c>Running</c> afterward — recovering one artifact is
    /// never itself a terminal transition.
    /// </summary>
    [Fact]
    public async Task Recovery_imports_a_sealed_but_unrecorded_agent_artifact_exactly_once_reports_unknown_truncation_and_never_a_false_completion()
    {
        var capturedTokenSource = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recordingCancelledSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCodexPlanningAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidFinalResponseJson,
            StandardOutputToWrite = "recovery-test-stdout-content",
        };

        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider(evidenceReader, adapter, services =>
        {
            services.RemoveAll<IRequestHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>>();
            services.AddScoped<IRequestHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>>(
                _ => new StallingRecordAgentAttemptResultCommandHandler(capturedTokenSource, recordingCancelledSource));
        }))
        {
            (runId, attemptId, _, _) = await SeedEligibleAgentAttemptAsync(provider);

            var supervisor = CreateSupervisor(provider);
            await supervisor.StartAsync(CancellationToken.None);

            // By the time the stalled recording handler is invoked, the adapter has already
            // finished and the supervisor has already sealed the output artifacts — sealing
            // happens unconditionally before the recording dispatch, so a stalled recording
            // still leaves genuinely sealed, hashed files behind, simply unreferenced by any
            // Artifact row.
            await capturedTokenSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await recordingCancelledSource.Task.WaitAsync(TimeSpan.FromSeconds(15));

            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        await using var reopenedProvider = BuildServiceProvider(evidenceReader, new FakeCodexPlanningAdapter(_artifactStore));
        await using (var migrateScope = reopenedProvider.CreateAsyncScope())
        {
            await migrateScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.MigrateAsync();
        }

        // Runs recovery twice in a row, exactly as a host that crashed again mid-recovery would
        // on its next restart — idempotency is the only thing that makes that safe.
        await AgentAttemptOutputRecovery.RunAsync(reopenedProvider, NullLogger.Instance, CancellationToken.None);
        await AgentAttemptOutputRecovery.RunAsync(reopenedProvider, NullLogger.Instance, CancellationToken.None);

        await using var verificationScope = reopenedProvider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();

        var stdoutArtifacts = dbContext.Artifacts
            .Where(a => a.AttemptId == attemptId && a.Purpose == ArtifactPurpose.AgentStandardOutput)
            .ToList();
        var stdoutArtifact = Assert.Single(stdoutArtifacts);
        Assert.Null(stdoutArtifact.Truncated);
        Assert.Equal(ArtifactCaptureOutcome.PartialHostInterrupted, stdoutArtifact.CaptureOutcome);

        var events = dbContext.Events.Where(e => e.AttemptId == attemptId).ToList();
        Assert.Contains(events, e => e.EventType == RunEventType.AgentOutputRecovered);
        Assert.DoesNotContain(events, e => e.EventType == RunEventType.AgentAttemptCompleted);

        var persistedAttempt = await dbContext.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Running, persistedAttempt!.Status);
    }

    private ServiceProvider BuildServiceProvider(
        IGitWorkspaceEvidenceReader evidenceReader,
        ICodexPlanningAdapter codexAdapter,
        Action<ServiceCollection>? configureAdditionalServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(codexAdapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        // Applied last so a test can replace a specific handler the assembly scan above already
        // registered (removing it first — see the callers that do).
        configureAdditionalServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one fully eligible Agent attempt through the real
    /// <see cref="CreateCodexPlanningAttemptCommand"/> — a Ready workspace, its current
    /// checkpoint, an active mutation lease, and an observed Codex capability snapshot — exactly
    /// the same production path that creates one, so eligibility is governed by the real
    /// Domain/Application rules rather than a hand-built shortcut.
    /// </summary>
    private async Task<(Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid LeaseId)> SeedEligibleAgentAttemptAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Plan through the real supervisor", now);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, _workspacePath, "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], now);
        dbContext.RepositoryMutationLeases.Add(lease);

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(codex);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var createResult = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(createResult.IsSuccess);

        return (run.Id, createResult.Value.AttemptId, workspace.Id, lease.Id);
    }

    private static AgentAttemptSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ICodexPlanningAdapter>(),
        provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        provider.GetRequiredService<IArtifactStore>(),
        NullLogger<AgentAttemptSupervisor>.Instance);

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

    /// <summary>Test-only seam returning a caller-chosen result per call, numbered from 1 — used
    /// to make the claim-time capture, the supervisor's pre-dispatch capture, and its
    /// post-invocation capture independently controllable within one test.</summary>
    private sealed class SequencedGitWorkspaceEvidenceReader(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        /// <summary>Exposed so a test can prove exactly how many times the supervisor itself
        /// invoked the capture, distinguishing "invoked once on the supervisor's behalf" from
        /// "invoked once per poll tick."</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(resultForCall(Interlocked.Increment(ref _callCount)));

        public static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
            new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);

        public static readonly GitWorkspaceEvidenceResult Failure =
            new(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
    }

    /// <summary>
    /// Test-only seam reproducing "eligibility lost strictly between the pre-dispatch Git
    /// evidence snapshot and <c>MarkAgentAttemptDispatchedCommand</c>'s own last-gate
    /// re-validation" with full determinism, no real threading. Call 1 is
    /// <c>CreateCodexPlanningAttemptCommand</c>'s own claim-time capture during seeding; call 2 is
    /// the supervisor's pre-dispatch capture — exactly the race window Correction A's description
    /// targets. On call 2, before returning its otherwise normal successful result, it releases
    /// the seeded lease via a fresh <see cref="DevalCopilotDbContext"/> opened directly against
    /// the same on-disk database file, so the release is durably committed and visible to the
    /// supervisor's own subsequent <c>MarkAgentAttemptDispatchedCommand</c> call without ever
    /// touching the test's own DbContext instances or any real timing/threading.
    /// </summary>
    private sealed class LeaseReleasingPreDispatchEvidenceReader(string databasePath, string fingerprintSha256) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        public Guid LeaseId { get; set; }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 2)
            {
                var options = new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options;
                await using var freshDbContext = new DevalCopilotDbContext(options);
                var lease = await freshDbContext.RepositoryMutationLeases.SingleAsync(candidate => candidate.Id == LeaseId, cancellationToken);
                lease.Release(DateTimeOffset.UtcNow);
                await freshDbContext.SaveChangesAsync(cancellationToken);
            }

            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
        }
    }

    /// <summary>
    /// Test-only seam for Correction B (case 2): succeeds on call 1 (seeding's own claim-time
    /// capture) and throws a plain <see cref="InvalidOperationException"/> — never
    /// <see cref="OperationCanceledException"/> — on every call from 2 onward, starting with the
    /// supervisor's own pre-dispatch capture.
    /// </summary>
    private sealed class ThrowingAfterFirstCaptureGitWorkspaceEvidenceReader(string fingerprintSha256) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null));
            }

            throw new InvalidOperationException("Simulated Git invocation failure — never a cancellation.");
        }
    }

    /// <summary>
    /// Test-only stand-in for the real Codex CLI invocation: never starts a real process. Writes
    /// whatever content is configured to the exact partial-file paths the real adapter would have
    /// written to (via the same real <see cref="IArtifactStore"/> the supervisor uses), so the
    /// supervisor's own unconditional sealing step has genuine bytes to seal.
    /// </summary>
    private sealed class FakeCodexPlanningAdapter(IArtifactStore artifactStore) : ICodexPlanningAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public string? StandardOutputToWrite { get; set; }

        public string? StandardErrorToWrite { get; set; }

        public string? FinalResponseJsonToWrite { get; set; }

        public CodexPlanningInvocationResult ResultToReturn { get; set; } =
            new(CodexPlanningInvocationOutcome.Exited, false, false, null);

        public async Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);

            await WriteIfPresentAsync(
                artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardOutput),
                StandardOutputToWrite,
                cancellationToken);
            await WriteIfPresentAsync(
                artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardError),
                StandardErrorToWrite,
                cancellationToken);
            await WriteIfPresentAsync(
                artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse),
                FinalResponseJsonToWrite,
                cancellationToken);

            return ResultToReturn;
        }

        private static async Task WriteIfPresentAsync(string path, string? content, CancellationToken cancellationToken)
        {
            if (content is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
    }

    /// <summary>
    /// Test-only stand-in for a recording transaction that never completes on its own — proves
    /// the supervisor's recording dispatch is bounded by its own token rather than hanging
    /// forever, and that the provider is never invoked again while it stalls. Mirrors
    /// <c>ProcessAttemptSupervisorHostedTests.StallingRecordProcessAttemptResultCommandHandler</c>
    /// exactly.
    /// </summary>
    private sealed class StallingRecordAgentAttemptResultCommandHandler(
        TaskCompletionSource<CancellationToken> capturedTokenSource,
        TaskCompletionSource<bool> cancelledSource)
        : ICommandHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>
    {
        public async Task<Result<RecordAgentAttemptResultCommandResult>> HandleAsync(
            RecordAgentAttemptResultCommand command, CancellationToken cancellationToken)
        {
            capturedTokenSource.TrySetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelledSource.TrySetResult(true);
                throw;
            }

            throw new InvalidOperationException("Unreachable: the delay above never completes without cancellation.");
        }
    }
}
