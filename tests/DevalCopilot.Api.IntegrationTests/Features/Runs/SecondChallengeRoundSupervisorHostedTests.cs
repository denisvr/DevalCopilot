using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
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
/// The second challenge-resolution round through the real <see cref="ChallengeResolutionSupervisor"/>,
/// real mediator and EF pipeline, faking only the Codex adapter and Git reader. Every claim and result
/// of the first round and of the second review is seeded through the real production commands, so the
/// lineage the supervisor's second resolution runs against is exactly what production writes.
/// </summary>
public sealed partial class SecondChallengeRoundSupervisorHostedTests : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminalPollTimeout = TimeSpan.FromSeconds(10);
    private const string ExhaustedCode = "agent_attempts.proposal_lineage_exhausted";
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-second-round-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-second-round-hosted-artifacts-{Guid.NewGuid():N}");

    private readonly FilesystemArtifactStore _artifactStore;

    public SecondChallengeRoundSupervisorHostedTests()
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

    private sealed record Seeded(
        Guid RunId, Guid SecondResolverAttemptId, Guid RootProposalId, Guid FirstRevisionId, Guid SecondReviewAttemptId, List<Guid> SecondChallengeIds);

    private static readonly string ProposalJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static string ChallengeResponseJson(int count) => JsonSerializer.Serialize(new
    {
        decision = "challenge",
        summary = "Material issues were found.",
        challenges = Enumerable.Range(1, count).Select(index => new
        {
            summary = $"Challenge {index} raises a material concern.",
            disputedItem = $"Step {index}",
            materialImpact = "Could cause data loss",
            reasoning = "The step does not account for concurrent writers",
            alternativeOrQuestion = "Consider a serialized write path instead",
        }),
    });

    private static string ResolvedResponseJson(IReadOnlyList<Guid> challengeIds, string scope) => JsonSerializer.Serialize(new
    {
        summary = "Every challenge has been resolved.",
        decisions = challengeIds.Select((id, index) => new
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
            scope,
            implementationSteps = "Revised steps",
            risks = "Revised risks",
            verificationPlan = "Revised verification",
            escalationPoints = "Revised escalation",
        },
    });

    private ServiceProvider BuildServiceProvider(IGitWorkspaceEvidenceReader evidenceReader, ICodexChallengeResolutionAdapter adapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(evidenceReader);
        services.AddSingleton(adapter);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateChallengeResolutionAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateChallengeResolutionAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    /// <summary>Seeds through production commands only: planning → Challenged review → first
    /// resolution → review of the first revision (Challenged) → the claimed, undispatched second
    /// resolver attempt.</summary>
    private async Task<Seeded> SeedClaimedSecondResolutionAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var runId = await SeedRunAsync(dbContext);

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                runId, planning.Value.AttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ProposalJson), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var root = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var firstReview = await ReviewAsync(mediator, dbContext, runId, root.Id, challengeCount: 2);
        var firstResolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(runId, firstReview.AttemptId), CancellationToken.None);
        Assert.True(firstResolver.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, firstResolver.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var firstResolution = ChallengeResolutionResponseParser.TryParse(
            ResolvedResponseJson(firstReview.ChallengeIds, "First revised scope"), firstReview.ChallengeIds.ToHashSet());
        Assert.NotNull(firstResolution);
        Assert.True((await mediator.SendAsync(
            new RecordChallengeResolutionResultCommand(
                runId, firstResolver.Value.AttemptId, AgentOutcome.Resolved, Fingerprint, [], firstResolution, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var firstRevision = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == firstResolver.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == runId && message.Type == CollaborationMessageType.Escalation));

        var secondReview = await ReviewAsync(mediator, dbContext, runId, firstRevision.Id, challengeCount: 2);
        var secondResolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(runId, secondReview.AttemptId), CancellationToken.None);
        Assert.True(secondResolver.IsSuccess);

        return new Seeded(runId, secondResolver.Value.AttemptId, root.Id, firstRevision.Id, secondReview.AttemptId, secondReview.ChallengeIds);
    }

    private static async Task<(Guid AttemptId, List<Guid> ChallengeIds)> ReviewAsync(
        IApplicationMediator mediator, DevalCopilotDbContext dbContext, Guid runId, Guid proposalId, int challengeCount)
    {
        var claim = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, claim.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var review = ClaudeCriticalReviewResponseParser.TryParse(ChallengeResponseJson(challengeCount));
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, claim.Value.AttemptId, AgentOutcome.Challenged, Fingerprint, [], review, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var challengeIds = await dbContext.CollaborationMessages
            .Where(message => message.AttemptId == claim.Value.AttemptId && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence)
            .Select(message => message.Id)
            .ToListAsync();
        return (claim.Value.AttemptId, challengeIds);
    }

    private static async Task<Guid> SeedRunAsync(DevalCopilotDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Plan the ledger", now);
        dbContext.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"{Path.GetTempPath()}devalcopilot-second-round-{Guid.NewGuid():N}",
            "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []));
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        foreach (var (capability, path) in new[] { (Capability.CodexCli, @"C:\fake\codex.exe"), (Capability.ClaudeCli, @"C:\fake\claude.exe") })
        {
            var snapshot = HostCapabilitySnapshot.Seed(capability, now);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, path, null, "1.0.0", now, now.AddMinutes(5));
            dbContext.HostCapabilitySnapshots.Add(snapshot);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        return run.Id;
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

    private static async Task RunSupervisorAsync(ServiceProvider provider, Func<Task> whileRunning)
    {
        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await whileRunning();
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }
    }

    [Fact]
    public async Task A_claimed_second_resolution_runs_from_its_sealed_manifest_and_records_one_escalation()
    {
        var adapter = new FakeAdapter(_artifactStore);
        await using var provider = BuildServiceProvider(new MatchingEvidenceReader(), adapter);
        var seeded = await SeedClaimedSecondResolutionAsync(provider);
        adapter.FinalResponseJsonToWrite = ResolvedResponseJson(seeded.SecondChallengeIds, "Second revised scope");

        await RunSupervisorAsync(provider, async () =>
            Assert.Equal(AttemptStatus.Completed, await PollForTerminalStatusAsync(provider, seeded.SecondResolverAttemptId)));

        Assert.Equal(1, adapter.InvocationCount);
        await AssertResolvedWithOneEscalationAsync(provider, adapter, seeded, seeded.SecondResolverAttemptId);
    }

    [Fact]
    public async Task A_restart_interrupts_the_claimed_second_resolution_without_invoking_the_provider_and_the_interrupted_run_accepts_no_further_lineage_claim()
    {
        var evidence = new MatchingEvidenceReader();
        Seeded seeded;
        await using (var firstProcess = BuildServiceProvider(evidence, new FakeAdapter(_artifactStore)))
        {
            seeded = await SeedClaimedSecondResolutionAsync(firstProcess);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        var adapter = new FakeAdapter(_artifactStore);
        await using var restarted = BuildServiceProvider(evidence, adapter);
        await using (var scope = restarted.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.MigrateAsync();
            var reconciled = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
            Assert.Equal(1, reconciled.Value);
        }

        await RunSupervisorAsync(restarted, () => Task.Delay(TimeSpan.FromMilliseconds(1200)));
        Assert.Equal(0, adapter.InvocationCount);

        await using var verificationScope = restarted.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var mediator = verificationScope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        Assert.Equal(AttemptStatus.Interrupted, (await dbContext.Attempts.FindAsync(seeded.SecondResolverAttemptId))!.Status);
        Assert.Equal(RunLifecycle.Interrupted, (await dbContext.Runs.SingleAsync(run => run.Id == seeded.RunId)).Lifecycle);
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.AttemptId == seeded.SecondResolverAttemptId));
        Assert.Empty(dbContext.CollaborationMessages.Where(message => message.RunId == seeded.RunId && message.Type == CollaborationMessageType.Escalation));

        // Nothing can be claimed for the interrupted run, so the second round is not silently revived.
        var claim = await mediator.SendAsync(
            new CreateChallengeResolutionAttemptCommand(seeded.RunId, seeded.SecondReviewAttemptId), CancellationToken.None);
        Assert.Equal("runs.not_running", Assert.Single(claim.Errors).Code);
        var implementation = await mediator.SendAsync(
            new CreateImplementationAttemptCommand(seeded.RunId, seeded.FirstRevisionId), CancellationToken.None);
        Assert.Equal("runs.not_running", Assert.Single(implementation.Errors).Code);
    }

    private static async Task AssertResolvedWithOneEscalationAsync(
        ServiceProvider provider, FakeAdapter adapter, Seeded seeded, Guid resolverAttemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var manifest = await dbContext.Artifacts.SingleAsync(
            artifact => artifact.AttemptId == resolverAttemptId && artifact.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(resolverAttemptId, adapter.LastRequest!.AttemptId);
        Assert.Equal(manifest.RelativeStoragePath, adapter.LastRequest.ContextManifestRelativeStoragePath);
        Assert.Equal(manifest.ContentHash, adapter.LastRequest.ContextManifestContentHash);

        var messages = dbContext.CollaborationMessages.Where(message => message.AttemptId == resolverAttemptId).ToList();
        Assert.Equal(seeded.SecondChallengeIds.Count, messages.Count(message => message.Type == CollaborationMessageType.Decision));
        var revised = Assert.Single(messages, message => message.Type == CollaborationMessageType.Proposal);
        Assert.Equal(seeded.FirstRevisionId, revised.InReplyToMessageId);
        var escalation = Assert.Single(dbContext.CollaborationMessages.Where(
            message => message.RunId == seeded.RunId && message.Type == CollaborationMessageType.Escalation));
        Assert.Equal(revised.Id, escalation.InReplyToMessageId);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, escalation.Provenance);
    }

    [Fact]
    public async Task An_exhausted_lineage_is_never_revived_by_a_restart_a_new_claim_or_an_implementation_request()
    {
        var evidence = new MatchingEvidenceReader();
        var firstAdapter = new FakeAdapter(_artifactStore);
        Seeded seeded;
        await using (var firstProcess = BuildServiceProvider(evidence, firstAdapter))
        {
            seeded = await SeedClaimedSecondResolutionAsync(firstProcess);
            firstAdapter.FinalResponseJsonToWrite = ResolvedResponseJson(seeded.SecondChallengeIds, "Second revised scope");
            await RunSupervisorAsync(firstProcess, async () =>
                Assert.Equal(AttemptStatus.Completed, await PollForTerminalStatusAsync(firstProcess, seeded.SecondResolverAttemptId)));
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        var restartedAdapter = new FakeAdapter(_artifactStore);
        await using var restarted = BuildServiceProvider(evidence, restartedAdapter);
        Guid depthTwoId;
        int attemptsBefore;
        await using (var scope = restarted.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.MigrateAsync();
            depthTwoId = (await dbContext.CollaborationMessages.SingleAsync(message =>
                message.AttemptId == seeded.SecondResolverAttemptId && message.Type == CollaborationMessageType.Proposal)).Id;
            attemptsBefore = await dbContext.Attempts.CountAsync(attempt => attempt.RunId == seeded.RunId);
            var reconciled = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
            Assert.Equal(0, reconciled.Value);
        }

        await RunSupervisorAsync(restarted, () => Task.Delay(TimeSpan.FromMilliseconds(1200)));

        await using var verificationScope = restarted.CreateAsyncScope();
        var mediator = verificationScope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var verification = verificationScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(0, restartedAdapter.InvocationCount);
        Assert.Equal(attemptsBefore, await verification.Attempts.CountAsync(attempt => attempt.RunId == seeded.RunId));

        var review = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(seeded.RunId, depthTwoId), CancellationToken.None);
        Assert.Equal(ExhaustedCode, Assert.Single(review.Errors).Code);
        var resolution = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(seeded.RunId, seeded.SecondReviewAttemptId), CancellationToken.None);
        Assert.Equal("agent_attempts.already_resolved", Assert.Single(resolution.Errors).Code);
        var implementationOfDepthTwo = await mediator.SendAsync(new CreateImplementationAttemptCommand(seeded.RunId, depthTwoId), CancellationToken.None);
        Assert.Equal(ExhaustedCode, Assert.Single(implementationOfDepthTwo.Errors).Code);
        var implementationOfFirstRevision = await mediator.SendAsync(
            new CreateImplementationAttemptCommand(seeded.RunId, seeded.FirstRevisionId), CancellationToken.None);
        Assert.Equal("agent_attempts.plan_challenged", Assert.Single(implementationOfFirstRevision.Errors).Code);

        Assert.Equal(attemptsBefore, await verification.Attempts.CountAsync(attempt => attempt.RunId == seeded.RunId));
        Assert.Single(verification.CollaborationMessages.Where(
            message => message.RunId == seeded.RunId && message.Type == CollaborationMessageType.Escalation));
    }

    private sealed class MatchingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
    }

    private sealed class FakeAdapter(IArtifactStore artifactStore) : ICodexChallengeResolutionAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public string? FinalResponseJsonToWrite { get; set; }

        public ChallengeResolutionInvocationRequest? LastRequest { get; private set; }

        public async Task<ChallengeResolutionInvocationResult> InvokeAsync(
            ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            LastRequest = request;
            if (FinalResponseJsonToWrite is { } content)
            {
                var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            return new ChallengeResolutionInvocationResult(
                ChallengeResolutionInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }
}
