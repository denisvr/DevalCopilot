using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
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
/// Starts the real <see cref="ChallengeResolutionSupervisor"/> — the actual
/// <c>BackgroundService</c>, with its real scoped mediator and EF-transaction pipeline — faking
/// only the two real external boundaries: <see cref="ICodexChallengeResolutionAdapter"/> (never a
/// real Codex CLI call) and <see cref="IGitWorkspaceEvidenceReader"/> (never a real Git
/// invocation). Every challenge-resolution attempt is seeded through the real production command
/// chain — a completed Codex Proposal, a completed Challenged Claude critical review, then the
/// real <see cref="CreateChallengeResolutionAttemptCommand"/> — rather than constructing
/// <see cref="Attempt"/> by hand. Mirrors <c>ClaudeCriticalReviewSupervisorHostedTests</c>'s
/// hosting pattern, scoped down to the scenarios genuinely specific to this new stage, including
/// its own dedicated competing-resolution dispatch-time race test — mirroring
/// <c>ClaudeCriticalReviewSupervisorHostedTests</c>'s own race test exactly, one level further
/// down the collaboration protocol.
/// </summary>
public sealed class ChallengeResolutionSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
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

    private static readonly string ChallengeFinalResponseJson = JsonSerializer.Serialize(new
    {
        decision = "challenge",
        summary = "Two material issues were found.",
        challenges = Enumerable.Range(1, 2).Select(index => new
        {
            summary = $"Challenge {index} raises a material concern.",
            disputedItem = $"Step {index}",
            materialImpact = "Could cause data loss",
            reasoning = "The step does not account for concurrent writers",
            alternativeOrQuestion = "Consider a serialized write path instead",
        }),
    });

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-challenge-resolution-supervisor-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-challenge-resolution-supervisor-hosted-artifacts-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public ChallengeResolutionSupervisorHostedTests()
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

    private static string ResolvedFinalResponseJson(IReadOnlyList<Guid> challengeMessageIds) => JsonSerializer.Serialize(new
    {
        summary = "Every challenge has been resolved.",
        decisions = challengeMessageIds.Select((id, index) => new
        {
            challengeMessageId = id.ToString(),
            summary = $"Decision {index + 1} accepts the challenge.",
            resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
            rationale = $"Rationale {index + 1}",
            resultingPlanChanges = $"Plan changes {index + 1}",
            nextAction = $"Next action {index + 1}",
        }),
        revisedProposal = new
        {
            summary = "Revised proposal addressing every challenge.",
            scope = "Revised ledger scope",
            implementationSteps = "Revised steps",
            risks = "Revised risks",
            verificationPlan = "Revised verification",
            escalationPoints = "Revised escalation",
        },
    });

    [Fact]
    public async Task A_resolved_attempt_atomically_records_one_decision_per_challenge_and_one_revised_proposal()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        await using var provider = BuildServiceProvider(evidenceReader, new FakeChallengeResolutionAdapter(_artifactStore));
        var (_, attemptId, _, originalProposalId, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);

        var adapter = (FakeChallengeResolutionAdapter)provider.GetRequiredService<ICodexChallengeResolutionAdapter>();
        adapter.FinalResponseJsonToWrite = ResolvedFinalResponseJson(challengeIds);

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
        Assert.Equal(AgentOutcome.Resolved, persistedAttempt!.AgentOutcome);

        var messages = dbContext.CollaborationMessages.Where(m => m.AttemptId == attemptId).ToList();
        Assert.Equal(challengeIds.Count + 1, messages.Count);
        var decisions = messages.Where(m => m.Type == CollaborationMessageType.Decision).ToList();
        Assert.Equal(challengeIds.Count, decisions.Count);
        Assert.Equal(challengeIds.ToHashSet(), decisions.Select(m => m.InReplyToMessageId!.Value).ToHashSet());
        var revisedProposal = Assert.Single(messages, m => m.Type == CollaborationMessageType.Proposal);
        Assert.Equal(originalProposalId, revisedProposal.InReplyToMessageId);

        Assert.Equal(1, adapter.InvocationCount);
    }

    [Fact]
    public async Task Pre_dispatch_source_drift_never_invokes_the_provider_and_resolves_to_source_changed()
    {
        // Seeding performs exactly 3 evidence captures (Codex-Proposal creation, Claude-review
        // creation, resolution-attempt creation); call 4 is the supervisor's own pre-dispatch
        // capture — the exact call this test drifts.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 3
                ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
                : SequencedGitWorkspaceEvidenceReader.Matching(DriftedFingerprint));
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);

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
    }

    [Fact]
    public async Task A_failed_post_invocation_evidence_capture_never_produces_a_resolved_outcome()
    {
        // Calls 1-4 are the seeding-time captures (Codex creation, Claude review creation,
        // resolution creation) plus the supervisor's own pre-dispatch capture; call 5 is the
        // supervisor's post-invocation capture, made to fail here.
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint)
                : SequencedGitWorkspaceEvidenceReader.Failure);
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);
        adapter.FinalResponseJsonToWrite = ResolvedFinalResponseJson(challengeIds);

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

    [Fact]
    public async Task A_failed_provider_invocation_is_recorded_as_provider_invocation_failed_without_retry()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore)
        {
            ResultToReturn = new ChallengeResolutionInvocationResult(ChallengeResolutionInvocationOutcome.Failed, false, false, null),
        };
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, _) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollForTerminalStatusAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Failed, status);

            // Several more poll ticks pass here while the attempt sits terminal, to prove it is
            // never retried.
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
    public async Task A_final_response_that_omits_a_challenge_is_recorded_as_invalid_structured_output()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (_, attemptId, _, _, challengeIds) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);
        // Only resolves the first of the two claimed challenges — never a valid resolution.
        adapter.FinalResponseJsonToWrite = ResolvedFinalResponseJson([challengeIds[0]]);

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
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));

        Guid runId;
        Guid attemptId;

        await using (var provider = BuildServiceProvider(evidenceReader, new FakeChallengeResolutionAdapter(_artifactStore)))
        {
            (runId, attemptId, _, _, _) = await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);

            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var dispatchResult = await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None);
            Assert.True(dispatchResult.IsSuccess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        var reopenedAdapter = new FakeChallengeResolutionAdapter(_artifactStore);
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
    /// Dispatch-time eligibility loss, one level further down the collaboration protocol than
    /// <c>ClaudeCriticalReviewSupervisorHostedTests</c>'s own competing-review race test: a
    /// competing challenge-resolution attempt commits a successful (Resolved) resolution of the
    /// exact same ordered input set strictly between the supervisor's eligibility snapshot and
    /// its own <c>MarkAgentAttemptDispatchedCommand</c> call. That command's own Resolver-specific
    /// revalidation — not the eligibility query's snapshot — is what closes this window: zero
    /// provider invocations, and the attempt is explicitly resolved to
    /// <see cref="AgentOutcome.InputAlreadyResolved"/> via
    /// <c>RecordChallengeResolutionInputAlreadyResolvedCommand</c>, exactly once.
    /// </summary>
    [Fact]
    public async Task A_competing_resolution_committed_between_the_eligibility_snapshot_and_dispatch_never_invokes_the_provider()
    {
        var evidenceReader = new CompetingResolutionCommittingPreDispatchEvidenceReader(_databasePath, Fingerprint);
        var adapter = new FakeChallengeResolutionAdapter(_artifactStore);

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        var (runId, attemptId, workspaceId, originalProposalId, challengeIds) =
            await SeedEligibleChallengeResolutionAttemptAsync(provider, evidenceReader);

        await using (var checkpointScope = provider.CreateAsyncScope())
        {
            var dbContext = checkpointScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.WorkspaceId == workspaceId);
            List<Guid> orderedInputIds = [originalProposalId, .. challengeIds];
            // The real resolver attempt seeded above is itself #3 (Codex creation = 1, Claude
            // review creation = 2, resolution creation = 3) — the competing attempt must use a
            // free attempt number, or the unique (RunId, AttemptNumber) constraint throws on
            // save, which this evidence reader's own try/catch would otherwise silently convert
            // into a misleading GitInvocationFailed/CheckpointEvidenceUnavailable outcome.
            evidenceReader.Configure(runId, workspaceId, checkpoint.Id, orderedInputIds, competingAttemptNumber: 4);
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
        Assert.Equal(AgentOutcome.InputAlreadyResolved, persistedAttempt.AgentOutcome);
        Assert.Null(persistedAttempt.AgentDispatchedAtUtc);

        // Exactly once: only the single AgentAttemptCompleted event this command itself appends.
        var events = dbContext2.Events.Where(e => e.AttemptId == attemptId).ToList();
        Assert.Single(events, e => e.EventType == RunEventType.AgentAttemptCompleted);
    }

    private ServiceProvider BuildServiceProvider(
        IGitWorkspaceEvidenceReader evidenceReader, ICodexChallengeResolutionAdapter challengeResolutionAdapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(challengeResolutionAdapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateChallengeResolutionAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateChallengeResolutionAttemptCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one fully eligible challenge-resolution attempt entirely through real production
    /// commands: a Ready workspace/checkpoint/lease, observed Codex and Claude capabilities, a
    /// completed Codex Proposal, a completed Challenged Claude critical review (two Challenges),
    /// and finally the resolution attempt itself through
    /// <see cref="CreateChallengeResolutionAttemptCommand"/>.
    /// </summary>
    private async Task<(Guid RunId, Guid AttemptId, Guid WorkspaceId, Guid OriginalProposalId, List<Guid> ChallengeIds)>
        SeedEligibleChallengeResolutionAttemptAsync(ServiceProvider provider, IGitWorkspaceEvidenceReader evidenceReader)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();

        var (runId, workspaceId, _) = await SeedRunWorkspaceCheckpointLeaseAsync(dbContext, "Resolve the challenged ledger proposal");
        await SeedClaudeCapabilityObservedAsync(dbContext);

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var createCodexResult = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
        Assert.True(createCodexResult.IsSuccess);
        var codexAttemptId = createCodexResult.Value.AttemptId;
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, codexAttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                runId, codexAttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ValidCodexProposalStructuredContentJson), null),
            CancellationToken.None)).IsSuccess);

        var proposalMessage = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == codexAttemptId && message.Type == CollaborationMessageType.Proposal);

        var createReviewResult = await mediator.SendAsync(
            new CreateClaudeCriticalReviewAttemptCommand(runId, proposalMessage.Id), CancellationToken.None);
        Assert.True(createReviewResult.IsSuccess);
        var reviewAttemptId = createReviewResult.Value.AttemptId;
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, reviewAttemptId), CancellationToken.None)).IsSuccess);

        var review = ClaudeCriticalReviewResponseParser.TryParse(ChallengeFinalResponseJson);
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(runId, reviewAttemptId, AgentOutcome.Challenged, Fingerprint, [], review, null),
            CancellationToken.None)).IsSuccess);

        var challengeIds = dbContext.CollaborationMessages
            .Where(m => m.AttemptId == reviewAttemptId && m.Type == CollaborationMessageType.Challenge)
            .OrderBy(m => m.Sequence)
            .Select(m => m.Id)
            .ToList();

        var createResolutionResult = await mediator.SendAsync(
            new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), CancellationToken.None);
        Assert.True(createResolutionResult.IsSuccess);

        return (runId, createResolutionResult.Value.AttemptId, workspaceId, proposalMessage.Id, challengeIds);
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
            Guid.NewGuid(), project.Id, 1, $@"{Path.GetTempPath()}devalcopilot-challenge-resolution-{Guid.NewGuid():N}",
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

    private static ChallengeResolutionSupervisor CreateSupervisor(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ICodexChallengeResolutionAdapter>(),
        provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ChallengeResolutionSupervisor>.Instance);

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

    /// <summary>Mirrors <c>ClaudeCriticalReviewSupervisorHostedTests.SequencedGitWorkspaceEvidenceReader</c>
    /// exactly.</summary>
    private sealed class SequencedGitWorkspaceEvidenceReader(Func<int, GitWorkspaceEvidenceResult> resultForCall) : IGitWorkspaceEvidenceReader
    {
        private int _callCount;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(resultForCall(Interlocked.Increment(ref _callCount)));

        public static GitWorkspaceEvidenceResult Matching(string fingerprintSha256) =>
            new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);

        public static readonly GitWorkspaceEvidenceResult Failure =
            new(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
    }

    /// <summary>
    /// Test-only seam reproducing "a competing challenge-resolution attempt commits a successful
    /// resolution of the exact same ordered input set strictly between the eligibility snapshot
    /// and <c>MarkAgentAttemptDispatchedCommand</c>'s own last-gate re-validation" with full
    /// determinism, no real threading. Calls 1-3 are the three seeding-time claims (Codex
    /// creation, Claude review creation, resolution creation); call 4 is the supervisor's own
    /// pre-dispatch capture — exactly the race window this test targets. On call 4, before
    /// returning its otherwise normal successful result, it commits a second, already-Resolved
    /// challenge-resolution attempt for the very same ordered input set via a fresh
    /// <see cref="DevalCopilotDbContext"/> opened directly against the same on-disk database
    /// file, so the commit is durably visible to the supervisor's own subsequent
    /// <c>MarkAgentAttemptDispatchedCommand</c> call. Mirrors
    /// <c>ClaudeCriticalReviewSupervisorHostedTests.CompetingReviewCommittingPreDispatchEvidenceReader</c>.
    /// </summary>
    private sealed class CompetingResolutionCommittingPreDispatchEvidenceReader(string databasePath, string fingerprintSha256)
        : IGitWorkspaceEvidenceReader
    {
        private int _callCount;
        private Guid _runId;
        private Guid _workspaceId;
        private Guid _checkpointId;
        private List<Guid>? _orderedInputMessageIds;
        private int _competingAttemptNumber;

        public void Configure(
            Guid runId, Guid workspaceId, Guid checkpointId, IReadOnlyList<Guid> orderedInputMessageIds, int competingAttemptNumber)
        {
            _runId = runId;
            _workspaceId = workspaceId;
            _checkpointId = checkpointId;
            _orderedInputMessageIds = orderedInputMessageIds.ToList();
            _competingAttemptNumber = competingAttemptNumber;
        }

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 4 && _orderedInputMessageIds is { } orderedInputMessageIds)
            {
                var options = new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options;
                await using var freshDbContext = new DevalCopilotDbContext(options);

                var now = DateTimeOffset.UtcNow;
                var competing = Attempt.ClaimAgentChallengeResolution(
                    Guid.NewGuid(), _runId, _competingAttemptNumber, _workspaceId, _checkpointId, fingerprintSha256,
                    Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now);
                competing.MarkAgentDispatched(now);
                competing.CompleteAgent(AgentOutcome.Resolved, fingerprintSha256, now);
                freshDbContext.Attempts.Add(competing);
                for (var index = 0; index < orderedInputMessageIds.Count; index++)
                {
                    freshDbContext.AttemptInputMessages.Add(
                        AttemptInputMessage.Record(Guid.NewGuid(), competing.Id, orderedInputMessageIds[index], sequence: index));
                }

                await freshDbContext.SaveChangesAsync(cancellationToken);
            }

            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null);
        }
    }

    /// <summary>Mirrors <c>ClaudeCriticalReviewSupervisorHostedTests.FakeCriticalReviewAdapter</c>
    /// exactly, adapted for the Codex challenge-resolution port.</summary>
    private sealed class FakeChallengeResolutionAdapter(IArtifactStore artifactStore) : ICodexChallengeResolutionAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public string? FinalResponseJsonToWrite { get; set; }

        public ChallengeResolutionInvocationResult ResultToReturn { get; set; } =
            new(ChallengeResolutionInvocationOutcome.Exited, false, false, null);

        public async Task<ChallengeResolutionInvocationResult> InvokeAsync(
            ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);

            if (FinalResponseJsonToWrite is { } content)
            {
                var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            return ResultToReturn;
        }
    }
}
