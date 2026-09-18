using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;
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
/// Starts the real <see cref="ClaudeCriticalReviewSupervisor"/> — the actual
/// <c>BackgroundService</c>, with its real scoped mediator and EF-transaction pipeline (the same
/// composition <c>Program.cs</c> registers) — faking only the two real external boundaries:
/// <see cref="ICriticalReviewAdapter"/> (never a real Claude CLI or network call) and
/// <see cref="IGitWorkspaceEvidenceReader"/> (never a real Git invocation). Every Claude
/// critical-review attempt is seeded through the real production command chain — a completed
/// Codex Proposal produced via <see cref="CreateCodexPlanningAttemptCommand"/>,
/// <see cref="MarkAgentAttemptDispatchedCommand"/>, and <see cref="RecordAgentAttemptResultCommand"/>,
/// then reviewed via the real <see cref="CreateClaudeCriticalReviewAttemptCommand"/> — rather than
/// constructing <see cref="Attempt"/> by hand, so eligibility is driven by the real
/// Domain/Application rules, not a test-only shortcut. Mirrors
/// <c>AgentAttemptSupervisorHostedTests</c>'s hosting pattern exactly, adapted for the Claude
/// critical-review attempt feature.
/// </summary>
public sealed class ClaudeCriticalReviewSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The supervisor's own poll interval is 500ms (see
    /// <c>ClaudeCriticalReviewSupervisor.PollInterval</c>); this is a generous multiple of that,
    /// bounding how long a test waits for a terminal attempt status before giving up.</summary>
    private static readonly TimeSpan TerminalPollTimeout = TimeSpan.FromSeconds(10);

    private static readonly string Fingerprint = new('a', 64);
    private static readonly string DriftedFingerprint = new('b', 64);

    private static readonly string ValidCodexProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static readonly string ValidAcceptanceFinalResponseJson = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "The proposal is sound and matches the run objective.",
        rationale = "The scope, steps, and verification plan are all consistent with the objective.",
    });

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-review-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-review-supervisor-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-review-supervisor-hosted-workspace-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public ClaudeCriticalReviewSupervisorHostedTests()
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
    /// Happy path 1: a Claude critical-review attempt that accepts the reviewed Proposal is
    /// recorded exactly once across repeated polls — exactly one Acceptance
    /// <see cref="CollaborationMessage"/> is appended, in reply to the reviewed Proposal.
    /// </summary>
    [Fact]
    public async Task An_accepted_review_is_recorded_exactly_once_with_one_acceptance_message()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson,
            StandardOutputToWrite = "claude stdout",
        };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _, proposalMessageId) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Completed, status);

            // Several more poll ticks pass here while the attempt sits terminal, to prove it is
            // never picked up again.
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
        Assert.Equal(AgentOutcome.Accepted, persistedAttempt.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId).ToList();
        var message = Assert.Single(messages);
        Assert.Equal(CollaborationMessageType.Acceptance, message.Type);
        Assert.Equal(proposalMessageId, message.InReplyToMessageId);

        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// Happy path 2: a Claude critical-review attempt that challenges the reviewed Proposal
    /// appends every Challenge message atomically. Uses four challenges — inside the protocol's
    /// 1-5 bound, and more than one, so it also proves several messages committed together.
    /// </summary>
    [Fact]
    public async Task A_challenged_review_atomically_records_every_challenge_message_in_the_2_to_5_range()
    {
        var challengeJson = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "Several material issues were found in the proposal.",
            challenges = Enumerable.Range(1, 4).Select(index => new
            {
                summary = $"Challenge {index} raises a material concern.",
                disputedItem = $"Step {index}",
                materialImpact = "Could cause data loss",
                reasoning = "The step does not account for concurrent writers",
                alternativeOrQuestion = "Consider a serialized write path instead",
            }),
        });

        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = challengeJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, _, _, proposalMessageId) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

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
        Assert.Equal(AgentOutcome.Challenged, persistedAttempt!.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId).ToList();
        Assert.Equal(4, messages.Count);
        Assert.InRange(messages.Count, 2, 5);
        Assert.All(messages, message =>
        {
            Assert.Equal(CollaborationMessageType.Challenge, message.Type);
            Assert.Equal(proposalMessageId, message.InReplyToMessageId);
        });

        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// Source drift detected strictly before dispatch (the workspace's fresh fingerprint no
    /// longer matches the checkpoint this attempt claimed) invokes the provider zero times and
    /// resolves the attempt to <see cref="AgentOutcome.SourceChanged"/> instead of leaving it
    /// Running forever. Mirrors <c>AgentAttemptSupervisorHostedTests</c>'s own pre-dispatch
    /// evidence tests.
    /// </summary>
    [Fact]
    public async Task Pre_dispatch_source_drift_never_invokes_the_provider_and_resolves_to_source_changed()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 2
                ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
                : SequencedGitWorkspaceEvidenceReader.Matching(DriftedFingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

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
        Assert.Equal(AgentOutcome.SourceChanged, persistedAttempt!.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
    }

    /// <summary>
    /// A successful process-level exit whose post-invocation Git evidence capture fails (no
    /// fresh fingerprint to confirm the checkpoint is still current) must never be recorded as
    /// <see cref="AgentOutcome.Accepted"/> or <see cref="AgentOutcome.Challenged"/> — it
    /// downgrades to <see cref="AgentOutcome.CheckpointEvidenceUnavailable"/>, and no
    /// <see cref="CollaborationMessage"/> is ever appended. Mirrors
    /// <c>AgentAttemptSupervisorHostedTests.A_failed_post_invocation_evidence_capture_never_produces_a_proposed_outcome</c>.
    /// </summary>
    [Fact]
    public async Task A_failed_post_invocation_evidence_capture_never_produces_an_accepted_or_challenged_outcome()
    {
        // Calls 1-3 are the two seeding-time captures (Codex creation, Claude creation) and the
        // supervisor's own pre-dispatch capture — all of which must match for the attempt to ever
        // be dispatched. Call 4 is the supervisor's post-invocation capture, made to fail here.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 3
                ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
                : SequencedGitWorkspaceEvidenceReader.Failure);
        var adapter = new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

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
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// A provider invocation that never exits usefully (a launch/process failure, never a raw
    /// exception or credential) is recorded as <see cref="AgentOutcome.ProviderInvocationFailed"/>,
    /// and no <see cref="CollaborationMessage"/> is ever appended.
    /// </summary>
    [Fact]
    public async Task A_failed_provider_invocation_is_recorded_as_provider_invocation_failed()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore)
        {
            FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson,
            ResultToReturn = new CriticalReviewInvocationResult(CriticalReviewInvocationOutcome.Failed, false, false, null),
        };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

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
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedAttempt!.AgentOutcome);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId));
        Assert.Equal(1, adapter.InvocationCount);
    }

    /// <summary>
    /// A final response that does not conform to the Acceptance/Challenge union (here, an
    /// "accept" decision missing its required <c>rationale</c> field) is recorded as
    /// <see cref="AgentOutcome.InvalidStructuredOutput"/>, never trusted or repaired.
    /// </summary>
    [Fact]
    public async Task A_malformed_final_response_is_recorded_as_invalid_structured_output()
    {
        var malformedJson = JsonSerializer.Serialize(new { decision = "accept", summary = "Missing rationale field entirely." });

        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = malformedJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

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

    /// <summary>
    /// Dispatch-time eligibility loss: a competing critical-review attempt commits a successful
    /// (Accepted) review for the exact same reviewed Proposal strictly between the supervisor's
    /// eligibility snapshot and its own <c>MarkAgentAttemptDispatchedCommand</c> call.
    /// <c>MarkAgentAttemptDispatchedCommandHandler</c>'s own CriticalReviewer-specific
    /// revalidation — not the eligibility query's snapshot — is what closes this window: zero
    /// provider invocations either way. The run, workspace, lease, and checkpoint all remain
    /// fully eligible here, so the attempt is explicitly resolved to
    /// <see cref="AgentOutcome.InputAlreadyReviewed"/> via
    /// <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommand</c> — never
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/>, which this exact test used to assert
    /// before that correction (see the primary-review report's empirical before/after evidence
    /// for this test) and which now stays reserved exclusively for genuine workspace/lease/
    /// checkpoint loss. Mirrors <c>AgentAttemptSupervisorHostedTests</c>'s own "Correction A"
    /// lease-race test, reproducing the critical-review-specific half of the same gate instead.
    /// </summary>
    [Fact]
    public async Task A_competing_review_committed_between_the_eligibility_snapshot_and_dispatch_never_invokes_the_provider()
    {
        var evidenceReader = new CompetingReviewCommittingPreDispatchEvidenceReader(_databasePath, Fingerprint);
        var adapter = new FakeCriticalReviewAdapter(_artifactStore) { FinalResponseJsonToWrite = ValidAcceptanceFinalResponseJson };

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, workspaceId, _, proposalMessageId) =
            await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

        await using (var checkpointScope = provider.CreateAsyncScope())
        {
            var dbContext = checkpointScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.WorkspaceId == workspaceId);
            evidenceReader.Configure(runId, workspaceId, checkpoint.Id, proposalMessageId, competingAttemptNumber: 3);
        }

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
        var dbContext2 = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedAttempt = await dbContext2.Attempts.FindAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, persistedAttempt!.Status);
        Assert.Equal(AgentOutcome.InputAlreadyReviewed, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// An attempt already durably marked dispatched (the real execution-start claim) but with no
    /// terminal result ever recorded — simulating a host that dispatched then crashed before
    /// recording anything, without ever starting the real supervisor for it in this provider —
    /// becomes <see cref="AttemptStatus.Interrupted"/> once
    /// <c>ReconcileInterruptedAgentAttemptsCommand</c> runs against a fresh provider (the same
    /// command both supervisors share — it is provider-agnostic), and the real supervisor started
    /// against that same fresh provider never invokes the provider for it.
    /// </summary>
    [Fact]
    public async Task An_attempt_marked_dispatched_but_never_recorded_becomes_interrupted_on_restart_and_the_provider_is_never_invoked()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));

        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider(evidenceReader, new FakeCriticalReviewAdapter(_artifactStore)))
        {
            (runId, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var dispatchResult = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            Assert.True(dispatchResult.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeCriticalReviewAdapter(_artifactStore);
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
    /// Regression test for a real bug fixed this session in <c>GetEligibleAgentAttemptsQueryHandler</c>:
    /// previously it matched any <see cref="AttemptKind.Agent"/> attempt regardless of
    /// <see cref="AgentProvider"/>, so a claimed Claude critical-review attempt could have been
    /// handed to <see cref="AgentAttemptSupervisor"/>, which only ever knows how to invoke
    /// <c>ICodexPlanningAdapter</c>. Seeds a fully eligible Running Codex planning attempt and a
    /// fully eligible Running Claude critical-review attempt and proves each eligibility query
    /// returns only its own provider's attempt. The two attempts are seeded on two distinct runs
    /// rather than one: the database's own filtered unique index allows at most one Running
    /// attempt per run at a time (<c>ix_attempts_run_id_one_running</c>), so two Running Agent
    /// attempts can never coexist on the same run regardless of provider — the cross-provider
    /// leak this regression test guards against is about the eligibility queries' own row
    /// filters, not about same-run coexistence, so it is exercised just as meaningfully across
    /// two runs.
    /// </summary>
    [Fact]
    public async Task Eligible_attempt_feeds_never_cross_the_codex_claude_provider_boundary()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeCriticalReviewAdapter(_artifactStore);

        await using var provider = BuildServiceProvider(evidenceReader, adapter);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        // Run A: a fully eligible, Running Codex planning attempt — never dispatched.
        var (codexRunId, _, _) = await SeedRunWorkspaceCheckpointLeaseAsync(dbContext, "Codex-only run");
        await SeedClaudeCapabilityObservedAsync(dbContext);
        var codexCreateResult = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(codexRunId), CancellationToken.None);
        Assert.True(codexCreateResult.IsSuccess);
        var codexAttemptId = codexCreateResult.Value.AttemptId;

        // Run B: a fully eligible, Running Claude critical-review attempt, reviewing a completed
        // Codex Proposal recorded on the very same run — never dispatched.
        var (claudeRunId, claudeAttemptId, _, _, _) =
            await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

        var eligibleAgentAttempts = await mediator.SendAsync(new GetEligibleAgentAttemptsQuery(), CancellationToken.None);
        var eligibleClaudeAttempts = await mediator.SendAsync(new GetEligibleClaudeCriticalReviewAttemptsQuery(), CancellationToken.None);

        Assert.Contains(eligibleAgentAttempts, attempt => attempt.AttemptId == codexAttemptId);
        Assert.DoesNotContain(eligibleAgentAttempts, attempt => attempt.AttemptId == claudeAttemptId);

        Assert.Contains(eligibleClaudeAttempts, attempt => attempt.AttemptId == claudeAttemptId);
        Assert.DoesNotContain(eligibleClaudeAttempts, attempt => attempt.AttemptId == codexAttemptId);

        Assert.Equal(0, adapter.InvocationCount);
    }

    private ServiceProvider BuildServiceProvider(
        IGitWorkspaceEvidenceReader evidenceReader,
        ICriticalReviewAdapter criticalReviewAdapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(criticalReviewAdapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateClaudeCriticalReviewAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateClaudeCriticalReviewAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one fully eligible Claude critical-review attempt entirely through real production
    /// commands: a Ready workspace, its current checkpoint, an active mutation lease, an observed
    /// Claude capability snapshot, a completed Codex planning attempt whose Proposal is recorded
    /// through <see cref="RecordAgentAttemptResultCommand"/>, and finally the critical-review
    /// attempt itself through <see cref="CreateClaudeCriticalReviewAttemptCommand"/> — never a
    /// hand-built <see cref="Attempt"/> shortcut.
    /// </summary>
    private async Task<(Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid LeaseId, Guid ProposalMessageId)>
        SeedEligibleClaudeCriticalReviewAttemptAsync(ServiceProvider provider, IGitWorkspaceEvidenceReader evidenceReader)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var (runId, workspaceId, leaseId) = await SeedRunWorkspaceCheckpointLeaseAsync(dbContext, "Review the ledger proposal");
        await SeedClaudeCapabilityObservedAsync(dbContext);

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var createCodexResult = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
        Assert.True(createCodexResult.IsSuccess);
        var codexAttemptId = createCodexResult.Value.AttemptId;

        var dispatchResult =
            await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, codexAttemptId), CancellationToken.None);
        Assert.True(dispatchResult.IsSuccess);

        var recordResult = await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                runId, codexAttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ValidCodexProposalStructuredContentJson), null),
            CancellationToken.None);
        Assert.True(recordResult.IsSuccess);

        var proposalMessage = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == codexAttemptId && message.Type == CollaborationMessageType.Proposal);

        var createReviewResult = await mediator.SendAsync(
            new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessage.Id), CancellationToken.None);
        Assert.True(createReviewResult.IsSuccess);

        return (runId, createReviewResult.Value.AttemptId, workspaceId, leaseId, proposalMessage.Id);
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
            Guid.NewGuid(), project.Id, 1, $@"{Path.GetTempPath()}devalcopilot-claude-review-{Guid.NewGuid():N}",
            "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);

        // A fresh, random physical file identifier per call — never a fixed dummy value — so
        // two runs seeded against the same underlying database (as the mutual-exclusivity
        // regression test's two runs are) never collide on the partial unique index over
        // (PhysicalVolumeSerialNumber, PhysicalFileId) filtered to Active leases.
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
        dbContext.RepositoryMutationLeases.Add(lease);

        // Idempotent: HostCapabilitySnapshot is one row per Capability for the whole host
        // (a unique index enforces it), so a second seed call sharing the same underlying
        // database — as the mutual-exclusivity regression test's two runs do — must never try
        // to insert it again.
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
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claude);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ClaudeCriticalReviewSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ICriticalReviewAdapter>(),
        provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ClaudeCriticalReviewSupervisor>.Instance);

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
    /// to make each successive evidence capture (both seeding-time claims, the supervisor's
    /// pre-dispatch capture, and its post-invocation capture) independently controllable within
    /// one test. Mirrors <c>AgentAttemptSupervisorHostedTests.SequencedGitWorkspaceEvidenceReader</c>
    /// exactly.</summary>
    private sealed class SequencedGitWorkspaceEvidenceReader(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(resultForCall(Interlocked.Increment(ref _callCount)));

        public static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
            new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);

        public static readonly GitWorkspaceEvidenceResult Failure =
            new(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
    }

    /// <summary>
    /// Test-only seam reproducing "a competing critical-review attempt commits a successful
    /// review for the exact same reviewed Proposal strictly between the eligibility snapshot and
    /// <c>MarkAgentAttemptDispatchedCommand</c>'s own last-gate re-validation" with full
    /// determinism, no real threading. Calls 1-2 are the two seeding-time claims (Codex creation,
    /// then Claude creation); call 3 is the supervisor's own pre-dispatch capture — exactly the
    /// race window this test targets. On call 3, before returning its otherwise normal successful
    /// result, it commits a second, already-Accepted critical-review attempt for the very same
    /// reviewed Proposal via a fresh <see cref="DevalCopilotDbContext"/> opened directly against
    /// the same on-disk database file, so the commit is durably visible to the supervisor's own
    /// subsequent <c>MarkAgentAttemptDispatchedCommand</c> call without ever touching the test's
    /// own DbContext instances or any real timing/threading. Mirrors
    /// <c>AgentAttemptSupervisorHostedTests.LeaseReleasingPreDispatchEvidenceReader</c>.
    /// </summary>
    private sealed class CompetingReviewCommittingPreDispatchEvidenceReader(string databasePath, string fingerprintSha256)
        : IGitWorkspaceEvidenceReader
    {
        private int _callCount;
        private Guid _runId;
        private Guid _workspaceId;
        private Guid _checkpointId;
        private Guid _proposalMessageId;
        private int _competingAttemptNumber;

        public void Configure(Guid runId, Guid workspaceId, Guid checkpointId, Guid proposalMessageId, int competingAttemptNumber)
        {
            _runId = runId;
            _workspaceId = workspaceId;
            _checkpointId = checkpointId;
            _proposalMessageId = proposalMessageId;
            _competingAttemptNumber = competingAttemptNumber;
        }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 3)
            {
                var options = new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options;
                await using var freshDbContext = new DevalCopilotDbContext(options);

                var now = DateTimeOffset.UtcNow;
                var competing = Attempt.ClaimAgentCriticalReview(
                    Guid.NewGuid(), _runId, _competingAttemptNumber, _workspaceId, _checkpointId, fingerprintSha256,
                    Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now);
                competing.MarkAgentDispatched(now);
                competing.CompleteAgent(AgentOutcome.Accepted, fingerprintSha256, now);
                freshDbContext.Attempts.Add(competing);
                freshDbContext.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, _proposalMessageId, sequence: 0));
                await freshDbContext.SaveChangesAsync(cancellationToken);
            }

            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
        }
    }

    /// <summary>
    /// Test-only stand-in for the real Claude CLI invocation: never starts a real process. Writes
    /// whatever content is configured to the exact partial-file paths the real adapter would have
    /// written to (via the same real <see cref="IArtifactStore"/> the supervisor uses), so the
    /// supervisor's own unconditional sealing step has genuine bytes to seal. Mirrors
    /// <c>AgentAttemptSupervisorHostedTests.FakeCodexPlanningAdapter</c>.
    /// </summary>
    private sealed class FakeCriticalReviewAdapter(IArtifactStore artifactStore) : ICriticalReviewAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public string? StandardOutputToWrite { get; set; }

        public string? StandardErrorToWrite { get; set; }

        public string? FinalResponseJsonToWrite { get; set; }

        public CriticalReviewInvocationResult ResultToReturn { get; set; } =
            new(CriticalReviewInvocationOutcome.Exited, false, false, null);

        public async Task<CriticalReviewInvocationResult> InvokeAsync(CriticalReviewInvocationRequest request, CancellationToken cancellationToken)
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
}
