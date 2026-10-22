using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Api.Startup;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The hosted composition of one local execution sequence test (ADR-0060): the real coordinator and the real implementation, review,
/// diagnosis, correction and verification supervisors, with their real scoped mediator, validation, EF-transaction pipeline, step probe,
/// the REAL Claude adapters and the filesystem artifact store over a real file-backed SQLite database with production migrations. Only the
/// process boundary (one double for the Claude CLI and every verification recipe), the two Codex adapters and the Git evidence reader
/// are deterministic doubles; every double counts its invocations so a test proves exact external-call counts. A restart is a stop of
/// every hosted service, the startup closure the host runs, and a brand-new provider over the same database and artifacts.
/// </summary>
internal sealed class LocalExecutionSequenceHostedHarness : IAsyncDisposable
{
    public const string SentinelExecutable = @"C:\sentinel-tools\build-tool.exe";

    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(90);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-sequence-{Guid.NewGuid():N}.db");
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-sequence-artifacts-{Guid.NewGuid():N}");
    private readonly string toolRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-sequence-tool-{Guid.NewGuid():N}");
    private readonly string claudeExecutable;
    private readonly List<IHostedService> started = [];
    private readonly List<ServiceProvider> retired = [];

    public LocalExecutionSequenceHostedHarness(OffsetClock? clock = null)
    {
        Clock = clock;
        ArtifactStore = new FilesystemArtifactStore(artifactRoot);
        Directory.CreateDirectory(toolRoot);
        claudeExecutable = Path.Combine(toolRoot, "claude.exe");
        File.WriteAllText(claudeExecutable, string.Empty);
        Evidence = new LocalExecutionSequenceStagedEvidence();
        Process = new LocalExecutionSequenceProcessDouble(Evidence, OpenContext);
        Review = new LocalExecutionSequenceReviewAdapter(ArtifactStore);
        Diagnosis = new LocalExecutionSequenceDiagnosisAdapter(ArtifactStore);
        Provider = BuildProvider();
    }

    public OffsetClock? Clock { get; }

    public FilesystemArtifactStore ArtifactStore { get; }

    public LocalExecutionSequenceStagedEvidence Evidence { get; }

    public LocalExecutionSequenceProcessDouble Process { get; }

    public LocalExecutionSequenceReviewAdapter Review { get; }

    public LocalExecutionSequenceDiagnosisAdapter Diagnosis { get; }

    public ServiceProvider Provider { get; private set; }

    public string DatabasePath => databasePath;

    /// <summary>The disposable root of the real filesystem artifact store, for a test that deletes or tampers with a sealed file.</summary>
    public string ArtifactRoot => artifactRoot;

    public DevalCopilotDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton<TimeProvider>(Clock ?? TimeProvider.System);
        AccountUsageGuardTestServices.Register(services);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(Evidence);
        services.AddSingleton<IArtifactStore>(ArtifactStore);
        services.AddSingleton<IVerificationOutputArtifactStore>(ArtifactStore);
        services.AddSingleton<IProcessExecutionAdapter>(Process);
        services.AddSingleton<IClaudeImplementationAdapter>(_ => new ClaudeImplementationAdapter(Process, ArtifactStore));
        services.AddSingleton<IClaudeReviewCorrectionAdapter>(_ => new ClaudeReviewCorrectionAdapter(Process, ArtifactStore));
        services.AddSingleton<ICodexImplementationReviewAdapter>(Review);
        services.AddSingleton<ICodexVerificationDiagnosisAdapter>(Diagnosis);
        services.AddSingleton<IRunEventNotifier>(new NoopNotifier());
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(provider =>
            new AttemptDurabilityProbe(provider.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddScoped<ILocalExecutionStepProbe>(provider =>
            new LocalExecutionStepProbe(provider.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    // ---- composition ---------------------------------------------------------------------------------------------------

    /// <summary>Starts the real coordinator and the five real supervisors, like the host does.</summary>
    public async Task StartHostsAsync(bool coordinator = true, bool supervisors = true, bool verification = true)
    {
        var scopeFactory = Provider.GetRequiredService<IServiceScopeFactory>();
        IHostedService[] services =
        [
            new ImplementationSupervisor(
                scopeFactory, Provider.GetRequiredService<IClaudeImplementationAdapter>(), Evidence, ArtifactStore,
                NullLogger<ImplementationSupervisor>.Instance),
            new ImplementationReviewSupervisor(
                scopeFactory, Review, Evidence, ArtifactStore, NullLogger<ImplementationReviewSupervisor>.Instance),
            new VerificationDiagnosisSupervisor(
                scopeFactory, Diagnosis, Evidence, ArtifactStore, NullLogger<VerificationDiagnosisSupervisor>.Instance),
            new ReviewCorrectionSupervisor(
                scopeFactory, Provider.GetRequiredService<IClaudeReviewCorrectionAdapter>(), Evidence, ArtifactStore,
                NullLogger<ReviewCorrectionSupervisor>.Instance),
            .. verification
                ? new IHostedService[]
                {
                    new VerificationExecutionSupervisor(
                        scopeFactory, Process, Evidence, ArtifactStore, NullLogger<VerificationExecutionSupervisor>.Instance),
                }
                : [],
        ];
        foreach (var service in supervisors ? services : Array.Empty<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
            started.Add(service);
        }

        if (coordinator)
        {
            var sequenceCoordinator = new LocalExecutionSequenceCoordinator(scopeFactory, NullLogger<LocalExecutionSequenceCoordinator>.Instance);
            await sequenceCoordinator.StartAsync(CancellationToken.None);
            started.Add(sequenceCoordinator);
        }
    }

    /// <summary>Starts only the verification supervisor, for a test that held it back while a claim was tampered with.</summary>
    public async Task StartVerificationSupervisorAsync()
    {
        var service = new VerificationExecutionSupervisor(
            Provider.GetRequiredService<IServiceScopeFactory>(), Process, Evidence, ArtifactStore,
            NullLogger<VerificationExecutionSupervisor>.Instance);
        await service.StartAsync(CancellationToken.None);
        started.Add(service);
    }

    /// <summary>Stops every hosted service. A bounded stop returns even while a held provider call that ignores cancellation is
    /// still running, exactly like a host that lost a child process it can no longer reach.</summary>
    public async Task StopHostsAsync(TimeSpan? bound = null)
    {
        using var cancellation = new CancellationTokenSource(bound ?? TimeSpan.FromSeconds(30));
        foreach (var service in started.AsEnumerable().Reverse())
        {
            try
            {
                await service.StopAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // The bound elapsed while a held call was still running.
            }
        }

        started.Clear();
    }

    /// <summary>Simulates a host restart: the old services are stopped (a held call keeps running in the retired provider), the
    /// startup recoveries the host runs execute in their production order against the same database, and a brand-new provider replaces
    /// the old one. No hosted service is started: the test starts what it needs.</summary>
    public async Task RestartAsync()
    {
        await StopHostsAsync(TimeSpan.FromSeconds(2));
        retired.Add(Provider);
        Provider = BuildProvider();
        await RunStartupRecoveriesAsync();
    }

    public async Task RunStartupRecoveriesAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        await mediator.SendAsync(new InterruptLocalExecutionSequencesCommand(), CancellationToken.None);
        await mediator.SendAsync(new RecoverInterruptedVerificationOutputArtifactsCommand(), CancellationToken.None);
        await mediator.SendAsync(new ReconcileInterruptedVerificationExecutionsCommand(), CancellationToken.None);
        await AgentAttemptOutputRecovery.RunAsync(
            scope.ServiceProvider, NullLogger.Instance, CancellationToken.None);
        await mediator.SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);
        await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);
        await mediator.SendAsync(new InterruptPlanningSequencesCommand(), CancellationToken.None);
    }

    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, CancellationToken.None);
    }

    public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(query, CancellationToken.None);
    }

    public async Task<T> InDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> read)
    {
        await using var context = OpenContext();
        return await read(context);
    }

    // ---- arming ---------------------------------------------------------------------------------------------------------

    /// <summary>Reads the recorded availability and arms with exactly its candidate: the one explicit Human request of a cycle.</summary>
    public async Task<ArmLocalExecutionSequenceCommandResult> ArmAsync(LocalExecutionSequenceLineage lineage)
    {
        var availability = await SendAsync(new GetLocalExecutionSequenceAvailabilityQuery(lineage.RunId, lineage.PlanId));
        Assert.True(availability.IsSuccess);
        Assert.True(availability.Value.Available, availability.Value.RefusalCode);
        var candidate = availability.Value;
        var armed = await SendAsync(new ArmLocalExecutionSequenceCommand(
            lineage.RunId, lineage.PlanId, candidate.WorkspaceId!.Value, candidate.CheckpointId!.Value, candidate.FingerprintSha256!,
            candidate.ConfirmationSha256!));
        Assert.True(armed.IsSuccess, armed.IsFailure ? armed.Errors[0].Code : null);
        return armed.Value;
    }

    // ---- waiting --------------------------------------------------------------------------------------------------------

    public async Task<LocalExecutionSequence> WaitForSequenceAsync(
        Guid sequenceId, Func<LocalExecutionSequence, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? HostTimeout);
        LocalExecutionSequence? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await InDbAsync(context => context.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId));
            if (predicate(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException(
            $"The local execution sequence did not reach the expected state in time; last state: {last?.State} ({last?.ReasonCode}/{last?.DetailCode}).");
    }

    public Task<LocalExecutionSequence> WaitForEndAsync(Guid sequenceId, TimeSpan? timeout = null) =>
        WaitForSequenceAsync(sequenceId, sequence => LocalExecutionSequencePolicy.IsEnded(sequence.State), timeout);

    public async Task WaitUntilAsync(Func<DevalCopilotDbContext, Task<bool>> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? HostTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await InDbAsync(condition))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"Timed out waiting for: {description}.");
    }

    public Task<List<LocalExecutionSequenceStep>> StepsAsync(Guid sequenceId) =>
        InDbAsync(context => context.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.SequenceId == sequenceId).OrderBy(step => step.Ordinal).ToListAsync());

    public Task<List<Attempt>> AgentAttemptsAsync(Guid runId) =>
        InDbAsync(context => context.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId && attempt.Kind == AttemptKind.Agent).OrderBy(attempt => attempt.AttemptNumber).ToListAsync());

    public Task<List<VerificationExecution>> ExecutionsAsync(Guid projectId) =>
        InDbAsync(context => context.VerificationExecutions.AsNoTracking()
            .Where(execution => execution.ProjectId == projectId).OrderBy(execution => execution.ExecutionNumber).ToListAsync());

    // ---- production-written lineage --------------------------------------------------------------------------------------

    /// <summary>Seeds one ManualAgent run, its Ready owned workspace, lease, first checkpoint, observed providers and an Accepted
    /// Planner root (or, for a depth-two lineage, a Human-authorized final revision), every plan row written by the production planning
    /// commands, plus the project's enabled recipes. Nothing here claims an Agent operation that mutates the workspace.</summary>
    public async Task<LocalExecutionSequenceLineage> SeedLineageAsync(
        bool depthTwo = false, int recipeCount = 2, int maximumAgentAttempts = 24)
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        db.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent,
            "Implement the ledger table and its query", now,
            maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        run.Claim(now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-local-sequence-ws-{Guid.NewGuid():N}"),
            "branch", LocalExecutionSequenceStagedEvidence.Head, "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, LocalExecutionSequenceStagedEvidence.Head,
            LocalExecutionSequenceStagedEvidence.Fingerprints[0], []);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        if (!await db.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddHours(1));
            db.HostCapabilitySnapshots.Add(codex);
        }

        if (!await db.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.ClaudeCli))
        {
            var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
            claude.MarkDispatched(now);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, claudeExecutable, null, "2.1.276", now, now.AddHours(1));
            db.HostCapabilitySnapshots.Add(claude);
        }

        await db.SaveChangesAsync();

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, LocalExecutionSequenceStagedEvidence.Fingerprints[0], [],
                new ValidatedProposal("Add the ledger table and its query.", LocalExecutionSequenceResponses.ProposalJson), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var root = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var planId = root.Id;
        if (depthTwo)
        {
            var first = await ReviewAndResolveAsync(mediator, db, run.Id, root.Id, "First revised scope");
            var second = await ReviewAndResolveAsync(mediator, db, run.Id, first, "Second revised scope");
            var escalation = await db.CollaborationMessages.SingleAsync(
                message => message.RunId == run.Id && message.Type == CollaborationMessageType.Escalation);
            var authorization = await mediator.SendAsync(
                new AuthorizePlanningImplementationCommand(run.Id, escalation.Id, "The Human accepts the final revision."), CancellationToken.None);
            Assert.True(authorization.IsSuccess, authorization.IsFailure ? authorization.Errors[0].Code : null);
            planId = second;
        }
        else
        {
            var accept = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(run.Id, root.Id), CancellationToken.None);
            Assert.True(accept.IsSuccess);
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, accept.Value.AttemptId), CancellationToken.None)).IsSuccess);
            var accepted = ClaudeCriticalReviewResponseParser.TryParse(LocalExecutionSequenceResponses.AcceptJson);
            Assert.NotNull(accepted);
            Assert.True((await mediator.SendAsync(
                new RecordClaudeCriticalReviewResultCommand(
                    run.Id, accept.Value.AttemptId, AgentOutcome.Accepted, LocalExecutionSequenceStagedEvidence.Fingerprints[0], [], accepted, null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None)).IsSuccess);
        }

        var recipeIds = new List<Guid>();
        for (var number = 1; number <= recipeCount; number++)
        {
            var configured = await mediator.SendAsync(
                new ConfigureVerificationCommandCommand(
                    project.Id, $"Recipe {number}", SentinelExecutable,
                    [$"{LocalExecutionSequenceProcessDouble.RecipeArgumentPrefix}{number}", "test"], 300, true),
                CancellationToken.None);
            Assert.True(configured.IsSuccess, configured.IsFailure ? configured.Errors[0].Code : null);
        }

        recipeIds.AddRange(await db.VerificationCommands.AsNoTracking().Where(command => command.ProjectId == project.Id)
            .OrderBy(command => command.CommandNumber).Select(command => command.Id).ToListAsync());
        return new LocalExecutionSequenceLineage(run.Id, project.Id, workspace.Id, checkpoint.Id, root.Id, planId, recipeIds);
    }

    private static async Task<Guid> ReviewAndResolveAsync(
        IApplicationMediator mediator, DevalCopilotDbContext db, Guid runId, Guid proposalId, string scope)
    {
        var claim = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, claim.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var review = ClaudeCriticalReviewResponseParser.TryParse(LocalExecutionSequenceResponses.ChallengeJson(2));
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, claim.Value.AttemptId, AgentOutcome.Challenged, LocalExecutionSequenceStagedEvidence.Fingerprints[0], [], review, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var challengeIds = await db.CollaborationMessages
            .Where(message => message.AttemptId == claim.Value.AttemptId && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();

        var resolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(runId, claim.Value.AttemptId), CancellationToken.None);
        Assert.True(resolver.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, resolver.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var resolution = ChallengeResolutionResponseParser.TryParse(
            LocalExecutionSequenceResponses.ResolvedJson(challengeIds, scope), challengeIds.ToHashSet());
        Assert.NotNull(resolution);
        Assert.True((await mediator.SendAsync(
            new RecordChallengeResolutionResultCommand(
                runId, resolver.Value.AttemptId, AgentOutcome.Resolved, LocalExecutionSequenceStagedEvidence.Fingerprints[0], [], resolution, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        return (await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == resolver.Value.AttemptId && message.Type == CollaborationMessageType.Proposal)).Id;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopHostsAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Disposal must not mask the test's own failure.
        }

        // A held call must not outlive the harness.
        Process.ClaudeHold?.TrySetResult();
        Review.Hold?.TrySetResult();
        await Task.Delay(50);
        foreach (var provider in retired)
        {
            await provider.DisposeAsync();
        }

        await Provider.DisposeAsync();
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        foreach (var directory in new[] { artifactRoot, toolRoot })
        {
            if (Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // A released provider may still be closing a file; the temp directory is reclaimed later.
                }
            }
        }

        if (File.Exists(databasePath))
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // Same as above.
            }
        }
    }

    private sealed class NoopNotifier : IRunEventNotifier
    {
        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
