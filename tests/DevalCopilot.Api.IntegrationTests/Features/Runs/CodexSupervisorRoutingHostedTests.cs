using System.Text.Json;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Devalente.Shared.Cqrs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Two Codex supervisors run together (ADR-0009): the planning supervisor and the challenge-resolution supervisor, each with its
/// real eligibility feed, the real mediator, the real dispatch and recording commands, and SQLite. Only the external provider
/// boundary is a counting double. The planning supervisor is given a genuine pre-dispatch opportunity while a valid, undispatched
/// Resolver attempt exists: its feed is read through an observing mediator, so the test waits for completed feed reads rather
/// than for time, and the Resolver is only then offered to the resolution supervisor, which must complete it exactly once.
/// </summary>
public sealed class CodexSupervisorRoutingHostedTests : IDisposable
{
    private static readonly string Head = new('a', 40);
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-routing-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-routing-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public CodexSupervisorRoutingHostedTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class FixedEvidence : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, Head, Fingerprint, [], null));
    }

    /// <summary>Counts completed planning-feed reads and records whether any returned the Resolver attempt.</summary>
    private sealed class FeedObserver
    {
        private int _planningFeedReads;
        private int _planningFeedReadsOfferingAttempts;

        public int PlanningFeedReads => Volatile.Read(ref _planningFeedReads);

        public int PlanningFeedReadsOfferingAttempts => Volatile.Read(ref _planningFeedReadsOfferingAttempts);

        public void Record(int offered)
        {
            if (offered > 0)
            {
                Interlocked.Increment(ref _planningFeedReadsOfferingAttempts);
            }

            Interlocked.Increment(ref _planningFeedReads);
        }
    }

    private sealed class ObservingMediator(IApplicationMediator inner, FeedObserver observer) : IApplicationMediator
    {
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken) =>
            inner.SendAsync(command, cancellationToken);

        public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            var result = await inner.SendAsync(query, cancellationToken);
            if (query is GetEligibleAgentAttemptsQuery && result is IReadOnlyCollection<EligibleAgentAttempt> eligible)
            {
                observer.Record(eligible.Count);
            }

            return result;
        }
    }

    private sealed class CountingPlanningAdapter : ICodexPlanningAdapter
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(new CodexPlanningInvocationResult(CodexPlanningInvocationOutcome.Failed, false, false, null));
        }
    }

    private sealed class ResolvingAdapter(IServiceProvider services, IArtifactStore artifactStore) : ICodexChallengeResolutionAdapter
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public async Task<ChallengeResolutionInvocationResult> InvokeAsync(
            ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var challengeIds = await db.AttemptInputMessages.AsNoTracking()
                .Where(input => input.AttemptId == request.AttemptId && input.Sequence > 0)
                .OrderBy(input => input.Sequence).Select(input => input.CollaborationMessageId).ToListAsync(cancellationToken);
            var response = JsonSerializer.Serialize(new
            {
                summary = "Every challenge has been resolved.",
                decisions = challengeIds.Select(id => new
                {
                    challengeMessageId = id.ToString(),
                    summary = "Accept the challenge.",
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "The challenge is accepted.",
                    resultingPlanChanges = "The plan is narrowed.",
                    nextAction = "Implement the revised plan.",
                }),
                revisedProposal = new
                {
                    summary = "A narrowed revised proposal.",
                    scope = "Narrowed scope.",
                    implementationSteps = "REVISED steps.",
                    risks = "Narrowed risks.",
                    verificationPlan = "Narrowed verification.",
                    escalationPoints = "Narrowed escalation.",
                },
            });
            foreach (var (purpose, content) in new[]
                     {
                         (ArtifactPurpose.AgentFinalResponse, response),
                         (ArtifactPurpose.AgentStandardOutput, string.Empty),
                         (ArtifactPurpose.AgentStandardError, string.Empty),
                     })
            {
                var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, purpose);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            return new ChallengeResolutionInvocationResult(
                ChallengeResolutionInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private sealed record Host(ServiceProvider Provider, FeedObserver Observer, CountingPlanningAdapter Planning, ResolvingAdapter Resolving)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private Host BuildHost()
    {
        var observer = new FeedObserver();
        var planning = new CountingPlanningAdapter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        AccountUsageGuardTestServices.Register(services);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(new FixedEvidence());
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<ICodexPlanningAdapter>(planning);
        services.AddSingleton<IRunEventNotifier>(new NoopNotifier());
        services.AddDevalenteMediator(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

        // The real mediator, observed: wrapped so each completed planning-feed read is counted.
        var mediator = services.Single(descriptor => descriptor.ServiceType == typeof(IApplicationMediator));
        services.Remove(mediator);
        services.TryAdd(new ServiceDescriptor(
            typeof(IApplicationMediator),
            sp => new ObservingMediator(
                (IApplicationMediator)(mediator.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, mediator.ImplementationType!)),
                observer),
            mediator.Lifetime));

        ResolvingAdapter? resolving = null;
        services.AddSingleton<ICodexChallengeResolutionAdapter>(sp => resolving ??= new ResolvingAdapter(sp, _artifactStore));
        var provider = services.BuildServiceProvider();
        return new Host(provider, observer, planning, (ResolvingAdapter)provider.GetRequiredService<ICodexChallengeResolutionAdapter>());
    }

    private sealed class NoopNotifier : IRunEventNotifier
    {
        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static AgentAttemptSupervisor PlanningSupervisor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<ICodexPlanningAdapter>(),
        host.Provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<AgentAttemptSupervisor>.Instance);

    private static ChallengeResolutionSupervisor ResolutionSupervisor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<ICodexChallengeResolutionAdapter>(),
        host.Provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ChallengeResolutionSupervisor>.Instance);

    private static async Task UntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Timed out waiting for: {what}.");
            await Task.Delay(25);
        }
    }

    /// <summary>A real planning lineage and critical-review challenge through the production commands, then the production claim of
    /// the Resolver attempt for it; returns the undispatched Resolver attempt.</summary>
    private async Task<(Guid RunId, Guid ResolverAttemptId)> SeedUndispatchedResolverAsync(Host host)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        db.Projects.Add(project);
        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Route the resolver", now,
            maximumAgentAttempts: 24, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        run.Claim(now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-routing-ws-{Guid.NewGuid():N}"), "branch", Head, "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprint, []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        foreach (var (capability, path) in new[] { (Capability.CodexCli, @"C:\fake\codex.exe"), (Capability.ClaudeCli, @"C:\fake\claude.exe") })
        {
            var snapshot = HostCapabilitySnapshot.Seed(capability, now);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, path, null, "1.2.3", now, now.AddMinutes(5));
            db.HostCapabilitySnapshots.Add(snapshot);
        }

        await db.SaveChangesAsync();

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table.", JsonSerializer.Serialize(new
                {
                    scope = "Ledger",
                    implementationSteps = "Add the table then the query",
                    risks = "Unbounded content",
                    verificationPlan = "Tests",
                    escalationPoints = "None expected",
                })),
                null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var root = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var critique = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(run.Id, root.Id), CancellationToken.None);
        Assert.True(critique.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, critique.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var challenged = ClaudeCriticalReviewResponseParser.TryParse(JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "Material issues were found.",
            challenges = new[]
            {
                new
                {
                    summary = "Challenge one raises a material concern.",
                    disputedItem = "Step one",
                    materialImpact = "Could cause data loss",
                    reasoning = "The step does not account for concurrent writers",
                    alternativeOrQuestion = "Consider a serialized write path instead",
                },
            },
        }));
        Assert.NotNull(challenged);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, critique.Value.AttemptId, AgentOutcome.Challenged, Fingerprint, [], challenged, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        var resolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(run.Id, critique.Value.AttemptId), CancellationToken.None);
        Assert.True(resolver.IsSuccess, resolver.IsFailure ? resolver.Errors[0].Code : null);
        var stored = await db.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == resolver.Value.AttemptId);
        Assert.Equal(AgentRole.Resolver, stored.AgentRole);
        Assert.Equal(AgentResponseContract.ChallengeResolution, stored.AgentResponseContract);
        Assert.Null(stored.AgentDispatchedAtUtc);
        return (run.Id, resolver.Value.AttemptId);
    }

    private async Task<Attempt> ReadAsync(Host host, Guid attemptId)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts.AsNoTracking()
            .SingleAsync(attempt => attempt.Id == attemptId);
    }

    [Fact]
    public async Task The_planning_supervisor_neither_consumes_nor_invokes_a_valid_undispatched_resolver_which_the_resolution_supervisor_completes_once()
    {
        await using var host = BuildHost();
        var (_, resolverId) = await SeedUndispatchedResolverAsync(host);

        // Phase 1: only planning runs. Wait for completed feed reads that happened with the Resolver already waiting.
        var planningSupervisor = PlanningSupervisor(host);
        var readsBefore = host.Observer.PlanningFeedReads;
        await planningSupervisor.StartAsync(CancellationToken.None);
        try
        {
            await UntilAsync(
                () => Task.FromResult(host.Observer.PlanningFeedReads >= readsBefore + 3),
                "three completed planning-feed reads while the Resolver attempt is undispatched");
        }
        finally
        {
            await planningSupervisor.StopAsync(CancellationToken.None);
        }

        var untouched = await ReadAsync(host, resolverId);
        Assert.Equal(0, host.Observer.PlanningFeedReadsOfferingAttempts);
        Assert.Equal(0, host.Planning.Invocations);
        Assert.Equal(AttemptStatus.Running, untouched.Status);
        Assert.Null(untouched.AgentDispatchedAtUtc);
        Assert.Null(untouched.AgentOutcome);

        // Phase 2: with the planning supervisor still active alongside, the resolution supervisor completes it exactly once.
        var resolution = ResolutionSupervisor(host);
        await planningSupervisor.StartAsync(CancellationToken.None);
        await resolution.StartAsync(CancellationToken.None);
        try
        {
            await UntilAsync(
                async () => (await ReadAsync(host, resolverId)).Status != AttemptStatus.Running,
                "the Resolver attempt to be recorded");
            await Task.Delay(300); // a settled period during which both supervisors keep polling must not repeat or alter the result
        }
        finally
        {
            await resolution.StopAsync(CancellationToken.None);
            await planningSupervisor.StopAsync(CancellationToken.None);
        }

        var completed = await ReadAsync(host, resolverId);
        Assert.Equal(AttemptStatus.Completed, completed.Status);
        Assert.Equal(AgentOutcome.Resolved, completed.AgentOutcome);
        Assert.Equal(1, host.Resolving.Invocations);
        Assert.Equal(0, host.Planning.Invocations);
    }
}
