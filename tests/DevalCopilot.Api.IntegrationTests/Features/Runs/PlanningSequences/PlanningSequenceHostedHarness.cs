using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>
/// The hosted composition of one planning-sequence test (ADR-0055): the real coordinator and the three real provider supervisors, with
/// their real scoped mediator, validation, EF-transaction pipeline, step probe and filesystem artifact store over a real file-backed
/// SQLite database with production migrations, faking only the three provider adapters and the Git evidence reader (never a real
/// provider, process or Git invocation). Each adapter counts its invocations so a test can prove zero unauthorized external calls.
/// </summary>
internal sealed class PlanningSequenceHostedHarness : IAsyncDisposable
{
    public static readonly string Fingerprint = new('a', 64);

    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(60);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-planning-sequence-{Guid.NewGuid():N}.db");
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-planning-sequence-artifacts-{Guid.NewGuid():N}");
    private readonly List<IHostedService> started = [];

    public PlanningSequenceHostedHarness(Action<ServiceCollection, PlanningSequenceHostedHarness>? configure = null)
    {
        ArtifactStore = new FilesystemArtifactStore(artifactRoot);
        Planner = new ScriptedPlanner(ArtifactStore);
        Reviewer = new ScriptedReviewer(ArtifactStore);
        Resolver = new ScriptedResolver(ArtifactStore, () => OpenContext());
        Evidence = new CountingEvidenceReader();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        AccountUsageGuardTestServices.Register(services);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(Evidence);
        services.AddSingleton<ICodexPlanningAdapter>(Planner);
        services.AddSingleton<ICriticalReviewAdapter>(Reviewer);
        services.AddSingleton<ICodexChallengeResolutionAdapter>(Resolver);
        services.AddSingleton<IArtifactStore>(ArtifactStore);
        services.AddDevalenteMediator(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(provider =>
            new AttemptDurabilityProbe(provider.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddScoped<IPlanningSequenceStepProbe>(provider =>
            new PlanningSequenceStepProbe(provider.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        configure?.Invoke(services, this);
        Provider = services.BuildServiceProvider();
    }

    public ServiceProvider Provider { get; }

    public FilesystemArtifactStore ArtifactStore { get; }

    public ScriptedPlanner Planner { get; }

    public ScriptedReviewer Reviewer { get; }

    public ScriptedResolver Resolver { get; }

    public CountingEvidenceReader Evidence { get; }

    public string DatabasePath => databasePath;

    public DevalCopilotDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    /// <summary>Applies the production migrations (idempotent).</summary>
    public async Task MigrateAsync()
    {
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Seeds one ManualAgent run that is Created and pristine, with a Ready, owned workspace, its latest checkpoint and
    /// observed Codex and Claude runtimes: exactly the state arming admits.</summary>
    public async Task<Scene> SeedSceneAsync(
        string objective = "Plan the next increment", int maximumAgentAttempts = 16, TimeSpan? maximumAgentInvocationTime = null)
    {
        await MigrateAsync();
        await using var context = OpenContext();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        context.Projects.Add(project);

        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, objective, now,
            maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: maximumAgentInvocationTime);
        context.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-planning-sequence-{Guid.NewGuid():N}"),
            "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        context.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        context.GitCheckpoints.Add(checkpoint);

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
        context.RepositoryMutationLeases.Add(lease);

        if (!await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(codex);
        }

        if (!await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.ClaudeCli))
        {
            var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
            claude.MarkDispatched(now);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(claude);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return new Scene(run.Id, project.Id, workspace.Id, checkpoint.Id, Fingerprint);
    }

    /// <summary>Sends the one explicit arming request through the real mediator.</summary>
    public async Task<ArmPlanningSequenceCommandResult> ArmAsync(Scene scene)
    {
        await using var scope = Provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var result = await mediator.SendAsync(
            new ArmPlanningSequenceCommand(scene.RunId, scene.WorkspaceId, scene.CheckpointId, scene.Fingerprint), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
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

    /// <summary>Starts the real coordinator and the three real provider supervisors, like the host does.</summary>
    public async Task StartHostsAsync(bool coordinator = true)
    {
        var scopeFactory = Provider.GetRequiredService<IServiceScopeFactory>();
        var evidence = Provider.GetRequiredService<IGitWorkspaceEvidenceReader>();
        IHostedService[] services =
        [
            new AgentAttemptSupervisor(
                scopeFactory, Planner, evidence, ArtifactStore, NullLogger<AgentAttemptSupervisor>.Instance),
            new ClaudeCriticalReviewSupervisor(
                scopeFactory, Reviewer, evidence, ArtifactStore, NullLogger<ClaudeCriticalReviewSupervisor>.Instance),
            new ChallengeResolutionSupervisor(
                scopeFactory, Resolver, evidence, ArtifactStore, NullLogger<ChallengeResolutionSupervisor>.Instance),
        ];
        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None);
            started.Add(service);
        }

        if (coordinator)
        {
            await StartCoordinatorAsync();
        }
    }

    public async Task StartCoordinatorAsync()
    {
        var service = new PlanningSequenceCoordinator(
            Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PlanningSequenceCoordinator>.Instance);
        await service.StartAsync(CancellationToken.None);
        started.Add(service);
    }

    public async Task StopHostsAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var service in started.AsEnumerable().Reverse())
        {
            await service.StopAsync(cancellation.Token);
        }

        started.Clear();
    }

    /// <summary>Waits until the sequence satisfies the predicate (read through an independent connection) or the bound elapses.</summary>
    public async Task<PlanningSequence> WaitForSequenceAsync(Guid sequenceId, Func<PlanningSequence, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? HostTimeout);
        PlanningSequence? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = OpenContext();
            last = await context.PlanningSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId);
            if (predicate(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"The planning sequence did not reach the expected state in time; last state: {last?.State} ({last?.ReasonCode}).");
    }

    public Task<PlanningSequence> WaitForEndAsync(Guid sequenceId, TimeSpan? timeout = null) =>
        WaitForSequenceAsync(sequenceId, sequence => PlanningSequencePolicy.IsEnded(sequence.State), timeout);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopHostsAsync();
        }
        catch (Exception)
        {
            // Disposal must not mask the test's own failure.
        }

        await Provider.DisposeAsync();
        if (Directory.Exists(artifactRoot))
        {
            Directory.Delete(artifactRoot, recursive: true);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    internal sealed record Scene(Guid RunId, Guid ProjectId, Guid WorkspaceId, Guid CheckpointId, string Fingerprint);

    internal sealed class CountingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        private int captures;

        public int Captures => Volatile.Read(ref captures);

        public string FingerprintToReturn { get; set; } = Fingerprint;

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref captures);
            return Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), FingerprintToReturn, [], null));
        }
    }

    internal sealed class ScriptedPlanner(IArtifactStore artifactStore) : ICodexPlanningAdapter
    {
        private int invocations;

        public int InvocationCount => Volatile.Read(ref invocations);

        public List<Guid> AttemptIds { get; } = [];

        /// <summary>When set, an invocation waits for it: a still-running provider process.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocations);
            lock (AttemptIds)
            {
                AttemptIds.Add(request.AttemptId);
            }

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            await Write(artifactStore, request.RunId, request.AttemptId, JsonSerializer.Serialize(new
            {
                summary = "Add the ledger table and its query.",
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }));
            return new CodexPlanningInvocationResult(
                CodexPlanningInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    internal sealed class ScriptedReviewer(IArtifactStore artifactStore) : ICriticalReviewAdapter
    {
        private int invocations;

        public int InvocationCount => Volatile.Read(ref invocations);

        public List<Guid> AttemptIds { get; } = [];

        /// <summary>The decision for each successive invocation: the number of challenges, or 0 to accept. Unscripted invocations accept.</summary>
        public Queue<int> Decisions { get; } = new();

        public TaskCompletionSource? Gate { get; set; }

        public async Task<CriticalReviewInvocationResult> InvokeAsync(CriticalReviewInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocations);
            lock (AttemptIds)
            {
                AttemptIds.Add(request.AttemptId);
            }

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            int challenges;
            lock (Decisions)
            {
                challenges = Decisions.Count > 0 ? Decisions.Dequeue() : 0;
            }

            var response = challenges == 0
                ? JsonSerializer.Serialize(new
                {
                    decision = "accept",
                    summary = "The proposal is sound and matches the run objective.",
                    rationale = "The scope, steps and verification plan are consistent with the objective.",
                })
                : JsonSerializer.Serialize(new
                {
                    decision = "challenge",
                    summary = "Material issues were found.",
                    challenges = Enumerable.Range(1, challenges).Select(index => new
                    {
                        summary = $"Challenge {index} raises a material concern.",
                        disputedItem = $"Step {index}",
                        materialImpact = "Could cause data loss",
                        reasoning = "The step does not account for concurrent writers",
                        alternativeOrQuestion = "Consider a serialized write path instead",
                    }),
                });
            await Write(artifactStore, request.RunId, request.AttemptId, response);
            return new CriticalReviewInvocationResult(
                CriticalReviewInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    internal sealed class ScriptedResolver(IArtifactStore artifactStore, Func<DevalCopilotDbContext> openContext) : ICodexChallengeResolutionAdapter
    {
        private int invocations;

        public int InvocationCount => Volatile.Read(ref invocations);

        public List<Guid> AttemptIds { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public async Task<ChallengeResolutionInvocationResult> InvokeAsync(
            ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocations);
            lock (AttemptIds)
            {
                AttemptIds.Add(request.AttemptId);
            }

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            // The challenges this attempt must decide are exactly its recorded inputs after the reviewed Proposal.
            await using var context = openContext();
            var challengeIds = await context.AttemptInputMessages.AsNoTracking()
                .Where(input => input.AttemptId == request.AttemptId && input.Sequence >= 1)
                .OrderBy(input => input.Sequence)
                .Select(input => input.CollaborationMessageId)
                .ToListAsync(cancellationToken);
            await Write(artifactStore, request.RunId, request.AttemptId, JsonSerializer.Serialize(new
            {
                summary = "Every challenge has been resolved.",
                decisions = challengeIds.Select((id, index) => new
                {
                    challengeMessageId = id.ToString(),
                    summary = $"Decision {index + 1} accepts the challenge.",
                    resolution = DevalCopilot.Application.Features.Runs.ChallengeResolutionOutputSchema.AcceptedResolution,
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
            }));
            return new ChallengeResolutionInvocationResult(
                ChallengeResolutionInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private static async Task Write(IArtifactStore artifactStore, Guid runId, Guid attemptId, string finalResponse)
    {
        var path = artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, finalResponse);
    }
}
