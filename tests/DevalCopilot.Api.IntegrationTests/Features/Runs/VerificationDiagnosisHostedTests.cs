using System.Text;
using System.Text.Json;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Devalente.Shared.Cqrs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The explicit verification-failure journey end to end (ADR-0018) through the real supervisors, the real mediator and EF
/// pipeline, the REAL Claude adapters, and the real verification supervisor with its sealed output. Only the process boundary
/// (one double serves the Claude CLI and the verification command) and the two Codex adapters (review and diagnosis) are
/// deterministic doubles: no provider is ever started and the doubles prove reachability and sealed-form agreement, never
/// real-provider reliability. Every row below is written by production commands: planning, acceptance, implementation,
/// verification and its sealed stdout/stderr, diagnosis, correction, and the ordinary review.
/// </summary>
public sealed partial class VerificationDiagnosisHostedTests : IDisposable
{
    private const string SentinelExecutable = @"C:\sentinel-tools\build-tool.exe";
    private const string SentinelArgument = "--sentinel-argument-7712";
    private const string FailureLine = "SENTINEL-FAILURE error CS1002: ; expected";
    private const string StderrLine = "SENTINEL-STDERR build failed";

    private static readonly string Head = new('a', 40);
    private static readonly string[] Fingerprints = [new('a', 64), new('c', 64), new('d', 64), new('e', 64), new('f', 64)];

    private static readonly string ProposalJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly string _toolRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-hosted-tool-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _claudeExecutable;

    public VerificationDiagnosisHostedTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_toolRoot);
        _claudeExecutable = Path.Combine(_toolRoot, "claude.exe");
        File.WriteAllText(_claudeExecutable, string.Empty);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        foreach (var directory in new[] { _artifactRoot, _toolRoot })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    // ---- deterministic boundaries ------------------------------------------------------------------------------

    private const string OwnAgentsText = "OWN-PROJECT-CONVENTIONS: keep handlers small.\r\nSay \"yes\" & use <Operation>Handler — naïve 𝄞.\r\n";
    private const string ForeignMarker = "FOREIGN-PROJECT-CONVENTIONS";

    private sealed class StagedEvidence : IGitWorkspaceEvidenceReader
    {
        public int Stage { get; set; }

        /// <summary>The project's own root instruction files as every claim's capture observes them.</summary>
        public GitWorkspaceInstructionContext? Instructions { get; set; } = RealAdapterInstructionProbe.OwnContext(OwnAgentsText);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, Head, Fingerprints[Stage],
                Stage == 0 ? [] : [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], null, null, Instructions));
    }

    /// <summary>One double for both process users: a Claude CLI request (no sink paths) advances the evidence stage, a
    /// verification request (sink paths) writes the queued output into those sinks like the real adapter does.</summary>
    private sealed class RecordingProcessDouble(StagedEvidence evidence) : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public string FinalResponse { get; set; } = "{}";

        public Queue<(int ExitCode, string Stdout, string Stderr)> VerificationResults { get; } = new();

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            if (VerificationResults.Count > 0 && request.StandardOutputSinkPath is { } stdoutSink && request.StandardErrorSinkPath is { } stderrSink)
            {
                var (exitCode, stdout, stderr) = VerificationResults.Dequeue();
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutSink)!);
                File.WriteAllText(stdoutSink, stdout);
                File.WriteAllText(stderrSink, stderr);
                return Task.FromResult(new ProcessExecutionResult
                {
                    Outcome = ProcessExecutionOutcome.Exited,
                    ExitCode = exitCode,
                    StandardOutput = string.Empty,
                    StandardOutputTruncated = false,
                    StandardError = string.Empty,
                    StandardErrorTruncated = false,
                    Duration = TimeSpan.FromMilliseconds(5),
                });
            }

            evidence.Stage++;
            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = FinalResponse, session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromMilliseconds(5),
            });
        }
    }

    private sealed class NoopNotifier : IRunEventNotifier
    {
        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReviewAdapter(IArtifactStore artifactStore) : ICodexImplementationReviewAdapter
    {
        public int InvocationCount { get; private set; }

        public string FinalResponseJson { get; set; } = "{}";

        public List<(Guid AttemptId, string Text)> ReceivedManifests { get; } = [];

        public async Task<ImplementationReviewInvocationResult> InvokeAsync(
            ImplementationReviewInvocationRequest request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            var window = await artifactStore.VerifyAndReadSealedAsync(
                request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
                0, 64 * 1024, cancellationToken);
            Assert.Equal(SealedReadStatus.Ok, window.Status);
            ReceivedManifests.Add((request.AttemptId, window.Text));
            await WriteOutputsAsync(artifactStore, request.RunId, request.AttemptId, FinalResponseJson, cancellationToken);
            return new ImplementationReviewInvocationResult(
                ImplementationReviewInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private sealed class FakeDiagnosisAdapter(IArtifactStore artifactStore) : ICodexVerificationDiagnosisAdapter
    {
        public int InvocationCount { get; private set; }

        public string FinalResponseJson { get; set; } = "{}";

        /// <summary>Runs while the provider is "working": the seam where a change commits mid-invocation.</summary>
        public Func<Task>? DuringInvocation { get; set; }

        public List<(Guid AttemptId, string Text)> ReceivedManifests { get; } = [];

        public async Task<VerificationDiagnosisInvocationResult> InvokeAsync(
            VerificationDiagnosisInvocationRequest request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            var window = await artifactStore.VerifyAndReadSealedAsync(
                request.ContextManifestRelativeStoragePath, request.ContextManifestByteLength, request.ContextManifestContentHash,
                0, 64 * 1024, cancellationToken);
            Assert.Equal(SealedReadStatus.Ok, window.Status);
            ReceivedManifests.Add((request.AttemptId, window.Text));
            if (DuringInvocation is not null)
            {
                await DuringInvocation();
            }

            await WriteOutputsAsync(artifactStore, request.RunId, request.AttemptId, FinalResponseJson, cancellationToken);
            return new VerificationDiagnosisInvocationResult(
                VerificationDiagnosisInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private static async Task WriteOutputsAsync(
        IArtifactStore artifactStore, Guid runId, Guid attemptId, string finalResponse, CancellationToken cancellationToken)
    {
        foreach (var (purpose, content) in new[]
                 {
                     (ArtifactPurpose.AgentFinalResponse, finalResponse),
                     (ArtifactPurpose.AgentStandardOutput, string.Empty),
                     (ArtifactPurpose.AgentStandardError, string.Empty),
                 })
        {
            var path = artifactStore.GetPartialPath(runId, attemptId, purpose);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
    }

    private sealed record Host(
        ServiceProvider Provider, StagedEvidence Evidence, RecordingProcessDouble Process, FakeReviewAdapter Review,
        FakeDiagnosisAdapter Diagnosis) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private Host BuildHost(StagedEvidence? evidence = null, ScriptedAccountUsageAdapter? accountUsage = null)
    {
        evidence ??= new StagedEvidence();
        var process = new RecordingProcessDouble(evidence);
        var review = new FakeReviewAdapter(_artifactStore);
        var diagnosis = new FakeDiagnosisAdapter(_artifactStore);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        AccountUsageGuardTestServices.Register(services, accountUsage);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<IVerificationOutputArtifactStore>(_artifactStore);
        services.AddSingleton<IProcessExecutionAdapter>(process);
        services.AddSingleton<IClaudeImplementationAdapter>(sp => new ClaudeImplementationAdapter(process, _artifactStore));
        services.AddSingleton<IClaudeReviewCorrectionAdapter>(sp => new ClaudeReviewCorrectionAdapter(process, _artifactStore));
        services.AddSingleton<ICodexImplementationReviewAdapter>(review);
        services.AddSingleton<ICodexVerificationDiagnosisAdapter>(diagnosis);
        services.AddSingleton<IRunEventNotifier>(new NoopNotifier());
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return new Host(services.BuildServiceProvider(), evidence, process, review, diagnosis);
    }

    private static ImplementationSupervisor ImplementationSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<IClaudeImplementationAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ImplementationSupervisor>.Instance);

    private static ImplementationReviewSupervisor ReviewSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<ICodexImplementationReviewAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ImplementationReviewSupervisor>.Instance);

    private static VerificationDiagnosisSupervisor DiagnosisSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<ICodexVerificationDiagnosisAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<VerificationDiagnosisSupervisor>.Instance);

    private static ReviewCorrectionSupervisor CorrectionSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<IClaudeReviewCorrectionAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ReviewCorrectionSupervisor>.Instance);

    private static VerificationExecutionSupervisor VerificationSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<IProcessExecutionAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IVerificationOutputArtifactStore>(),
        NullLogger<VerificationExecutionSupervisor>.Instance);

    private static async Task RunSupervisorUntilAsync(BackgroundService supervisor, Func<Task<bool>> isDone, bool expectDone = true)
    {
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(expectDone ? 15 : 2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(40);
                if (expectDone && await isDone())
                {
                    break;
                }
            }
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await supervisor.StopAsync(stop.Token);
        }
    }

    private async Task RunAttemptSupervisorAsync(Host host, BackgroundService supervisor, Guid attemptId, bool expectTerminal = true) =>
        await RunSupervisorUntilAsync(
            supervisor,
            async () => await InDbAsync(host, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.Status).SingleAsync()) != AttemptStatus.Running,
            expectTerminal);

    private static string StdinOf(ProcessExecutionRequest request) => Encoding.UTF8.GetString(request.StandardInput!);

    private async Task<T> InDbAsync<T>(Host host, Func<DevalCopilotDbContext, Task<T>> read)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private async Task<TResult> SendAsync<TResult>(Host host, ICommand<TResult> command)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, CancellationToken.None);
    }

    private async Task<TResult> QueryAsync<TResult>(Host host, IQuery<TResult> query)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(query, CancellationToken.None);
    }

    // ---- production-written lineage -------------------------------------------------------------------------------

    private sealed record Lineage(Guid RunId, Guid WorkspaceId, Guid ProjectId, Guid RootId, Guid PlanId);

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
            summary = $"Revised proposal [{scope}] addressing every challenge.",
            scope,
            implementationSteps = $"Steps unique to [{scope}]",
            risks = $"Risks unique to [{scope}]",
            verificationPlan = $"Verification unique to [{scope}]",
            escalationPoints = $"Escalation unique to [{scope}]",
        },
    });

    private static readonly string AcceptJson = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    private async Task<Lineage> SeedLineageAsync(Host host, bool revised)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        db.Projects.Add(project);
        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the ledger table and its query", now,
            maximumAgentAttempts: 24, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        run.Claim(now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-diagnosis-ws-{Guid.NewGuid():N}"),
            "branch", Head, "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprints[0], []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        db.HostCapabilitySnapshots.Add(codex);
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
        claude.MarkDispatched(now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, _claudeExecutable, null, "2.1.276", now, now.AddMinutes(5));
        db.HostCapabilitySnapshots.Add(claude);
        await db.SaveChangesAsync();

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, Fingerprints[0], [],
                new ValidatedProposal("Add the ledger table and its query.", ProposalJson), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var root = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var planId = root.Id;
        if (revised)
        {
            var challenge = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(run.Id, root.Id), CancellationToken.None);
            Assert.True(challenge.IsSuccess);
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, challenge.Value.AttemptId), CancellationToken.None)).IsSuccess);
            var challenged = ClaudeCriticalReviewResponseParser.TryParse(ChallengeResponseJson(2));
            Assert.NotNull(challenged);
            Assert.True((await mediator.SendAsync(
                new RecordClaudeCriticalReviewResultCommand(
                    run.Id, challenge.Value.AttemptId, AgentOutcome.Challenged, Fingerprints[0], [], challenged, null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None)).IsSuccess);
            var challengeIds = await db.CollaborationMessages
                .Where(message => message.AttemptId == challenge.Value.AttemptId && message.Type == CollaborationMessageType.Challenge)
                .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();

            var resolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(run.Id, challenge.Value.AttemptId), CancellationToken.None);
            Assert.True(resolver.IsSuccess);
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, resolver.Value.AttemptId), CancellationToken.None)).IsSuccess);
            var resolution = ChallengeResolutionResponseParser.TryParse(ResolvedResponseJson(challengeIds, "First revised scope"), challengeIds.ToHashSet());
            Assert.NotNull(resolution);
            Assert.True((await mediator.SendAsync(
                new RecordChallengeResolutionResultCommand(
                    run.Id, resolver.Value.AttemptId, AgentOutcome.Resolved, Fingerprints[0], [], resolution, null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None)).IsSuccess);
            planId = (await db.CollaborationMessages.SingleAsync(
                message => message.AttemptId == resolver.Value.AttemptId && message.Type == CollaborationMessageType.Proposal)).Id;
        }
        else
        {
            var accept = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(run.Id, root.Id), CancellationToken.None);
            Assert.True(accept.IsSuccess);
            Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, accept.Value.AttemptId), CancellationToken.None)).IsSuccess);
            var accepted = ClaudeCriticalReviewResponseParser.TryParse(AcceptJson);
            Assert.NotNull(accepted);
            Assert.True((await mediator.SendAsync(
                new RecordClaudeCriticalReviewResultCommand(
                    run.Id, accept.Value.AttemptId, AgentOutcome.Accepted, Fingerprints[0], [], accepted, null,
                    ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
                CancellationToken.None)).IsSuccess);
        }

        var configured = await mediator.SendAsync(
            new ConfigureVerificationCommandCommand(project.Id, "Backend tests", SentinelExecutable, [SentinelArgument, "test"], 300, true),
            CancellationToken.None);
        Assert.True(configured.IsSuccess, configured.IsFailure ? configured.Errors[0].Code : null);
        return new Lineage(run.Id, workspace.Id, project.Id, root.Id, planId);
    }

    private static string ImplementationReport(IReadOnlyList<string> changed) => JsonSerializer.Serialize(new
    {
        summary = "Implemented the plan.",
        changedRelativePaths = changed,
        implementationNotes = "Added the migration and the query handler.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the backend test suite.",
    });

    private static string CorrectionResponse(IReadOnlyList<Guid> findingIds) => JsonSerializer.Serialize(new
    {
        revisionResponses = findingIds.Select((findingId, index) => new
        {
            findingMessageId = findingId,
            disposition = "Fixed",
            evidence = $"The failing statement {index + 1} was completed.",
            resultingSourceChanges = $"Completed statement {index + 1}.",
        }),
        executionReport = new
        {
            summary = "Correction complete.",
            changedRelativePaths = new[] { "src/Foo.cs" },
            implementationNotes = "Applied the requested correction.",
            unexpectedDiscoveries = "None.",
            remainingRisks = "None.",
            recommendedVerification = "Run tests.",
        },
    });

    private static string FindingsJson(int count) => JsonSerializer.Serialize(new
    {
        outcome = VerificationDiagnosisOutputSchema.FindingsOutcome,
        summary = "The build fails on missing statements.",
        findings = Enumerable.Range(1, count).Select(index => new
        {
            severity = ImplementationReviewOutputSchema.Severities[1],
            category = ImplementationReviewOutputSchema.Categories[0],
            summary = $"Statement {index} is incomplete.",
            evidence = $"The build output reports a syntax error at statement {index}.",
            requiredChange = $"Complete statement {index}.",
            affectedRelativePath = (string?)"src/Foo.cs",
        }),
        escalation = (object?)null,
    });

    private static readonly string EscalationJson = JsonSerializer.Serialize(new
    {
        outcome = VerificationDiagnosisOutputSchema.EscalationOutcome,
        summary = "The failure needs a human decision.",
        findings = Array.Empty<object>(),
        escalation = new
        {
            unresolvedDecision = "Whether the failing check is within the scope of the plan.",
            options = "Adjust the plan scope or accept the failure as out of scope.",
            consequences = "No source change is justified until that is decided.",
            evidence = "The failure concerns local tool access outside the plan.",
            recommendedChoice = "Decide the scope before changing source.",
        },
    });

    private static string ApprovedJson() => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ApprovedOutcome,
        summary = "The implementation matches the plan.",
        rationale = "Every step from the plan was followed and verification passed.",
        residualRisks = "None material.",
        findings = Array.Empty<object>(),
    });

    // ---- journey steps ------------------------------------------------------------------------------------------------

    private async Task<(Guid AttemptId, CollaborationMessage Report)> ImplementAsync(Host host, Lineage lineage)
    {
        var claim = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.PlanId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        await RunAttemptSupervisorAsync(host, ImplementationSupervisorFor(host), claim.Value.AttemptId);
        Assert.Equal(AgentOutcome.Implemented, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == claim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var report = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == claim.Value.AttemptId && m.Type == CollaborationMessageType.ExecutionReport));
        return (claim.Value.AttemptId, report);
    }

    /// <summary>Claims one verification execution of the project's only command at the workspace's current checkpoint and runs
    /// the real verification supervisor; the queued result is what the double "wrote and exited with".</summary>
    private async Task<VerificationExecution> VerifyAsync(Host host, Lineage lineage, int exitCode)
    {
        host.Process.VerificationResults.Enqueue((exitCode, exitCode == 0 ? "all tests passed" : FailureLine, exitCode == 0 ? string.Empty : StderrLine));
        var command = await InDbAsync(host, db => db.VerificationCommands.SingleAsync(candidate => candidate.ProjectId == lineage.ProjectId));
        var checkpointId = await InDbAsync(host, db => db.GitCheckpoints.Where(candidate => candidate.WorkspaceId == lineage.WorkspaceId)
            .OrderByDescending(candidate => candidate.CheckpointNumber).Select(candidate => candidate.Id).FirstAsync());
        var claim = await SendAsync(host, new ClaimVerificationExecutionCommand(lineage.ProjectId, command.Id, checkpointId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        await RunSupervisorUntilAsync(
            VerificationSupervisorFor(host),
            async () => await InDbAsync(host, db => db.VerificationExecutions.Where(e => e.Id == claim.Value.VerificationExecutionId).Select(e => e.Status).SingleAsync())
                != VerificationExecutionStatus.Running);
        var execution = await InDbAsync(host, db => db.VerificationExecutions.AsNoTracking().SingleAsync(e => e.Id == claim.Value.VerificationExecutionId));
        Assert.Equal(exitCode == 0 ? VerificationExecutionStatus.Passed : VerificationExecutionStatus.Failed, execution.Status);
        return execution;
    }

    private async Task<Guid> DiagnoseAsync(Host host, Lineage lineage, Guid reportId, string response, AgentOutcome expected)
    {
        var claim = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, reportId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        host.Diagnosis.FinalResponseJson = response;
        await RunAttemptSupervisorAsync(host, DiagnosisSupervisorFor(host), claim.Value.AttemptId);
        Assert.Equal(expected, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == claim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        return claim.Value.AttemptId;
    }

    private async Task<JsonElement> ReadManifestAsync(Host host, Guid attemptId)
    {
        var artifact = await InDbAsync(host, db => db.Artifacts.AsNoTracking().SingleAsync(
            candidate => candidate.AttemptId == attemptId && candidate.Purpose == ArtifactPurpose.AgentContextManifest));
        var window = await _artifactStore.VerifyAndReadSealedAsync(
            artifact.RelativeStoragePath, artifact.ByteLength, artifact.ContentHash, 0, 64 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, window.Status);
        return JsonSerializer.Deserialize<JsonElement>(window.Text);
    }

    private async Task<(Guid CorrectionId, CollaborationMessage Report)> CorrectDiagnosisAsync(Host host, Lineage lineage, Guid diagnosisId, IReadOnlyList<Guid> findingIds)
    {
        var claim = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, diagnosisId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        host.Process.FinalResponse = CorrectionResponse(findingIds);
        await RunAttemptSupervisorAsync(host, CorrectionSupervisorFor(host), created.AttemptId);
        Assert.Equal(AgentOutcome.CorrectionApplied, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == created.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var report = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == created.AttemptId && m.Type == CollaborationMessageType.ExecutionReport));
        return (created.AttemptId, report);
    }

    private async Task<List<Guid>> FindingIdsAsync(Host host, Guid diagnosisId) =>
        await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.AttemptId == diagnosisId && m.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(m => m.Sequence).Select(m => m.Id).ToListAsync());

    // ---- the journey ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_verification_is_diagnosed_corrected_reverified_and_only_then_approved_by_an_ordinary_review()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);

        // Without verification evidence nothing can be reviewed or diagnosed; the failed run then makes only the diagnosis claimable.
        var noEvidence = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, report.Id));
        Assert.Equal("agent_attempts.verification_evidence_missing", Assert.Single(noEvidence.Errors).Code);
        var failed = await VerifyAsync(host, lineage, exitCode: 1);
        Assert.Equal(2, await InDbAsync(host, db => db.VerificationOutputArtifacts.CountAsync(o => o.VerificationExecutionId == failed.Id)));

        // The approval gate is untouched: a failed command still blocks the ordinary review.
        var reviewRefused = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, report.Id));
        Assert.Equal("agent_attempts.verification_evidence_not_passed", Assert.Single(reviewRefused.Errors).Code);
        Assert.Equal(0, host.Review.InvocationCount);

        // Diagnosis: Codex-side, read-only, a separate request.
        var status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        Assert.Equal(report.Id, status.Value.DiagnosableExecutionReportMessageId);
        var diagnosisId = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(2), AgentOutcome.DiagnosisFindingsRecorded);
        Assert.Equal(1, host.Diagnosis.InvocationCount);
        var findingIds = await FindingIdsAsync(host, diagnosisId);
        Assert.Equal(2, findingIds.Count);
        Assert.All(
            await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().Where(m => m.AttemptId == diagnosisId).ToListAsync()),
            message =>
            {
                Assert.Equal(CollaborationMessageType.ReviewFinding, message.Type);
                Assert.Equal(report.Id, message.InReplyToMessageId);
                Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
            });
        // A diagnosis never approves anything and records no checkpoint review.
        Assert.Empty(await InDbAsync(host, db => db.CheckpointReviews.ToListAsync()));
        Assert.Empty(await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.ReviewApproval).ToListAsync()));

        // What the provider was handed is the sealed manifest: instructions before the untrusted boundary, the verified excerpts
        // of the real failed output, and none of the recipe, path, or hash.
        var received = host.Diagnosis.ReceivedManifests.Single().Text;
        Assert.Equal((await ReadManifestAsync(host, diagnosisId)).GetRawText(), JsonSerializer.Deserialize<JsonElement>(received).GetRawText());
        Assert.True(received.IndexOf("\"instruction\"", StringComparison.Ordinal) < received.IndexOf("\"untrustedEvidenceBoundary\"", StringComparison.Ordinal));
        Assert.True(received.IndexOf("\"failureOutputNotice\"", StringComparison.Ordinal) < received.IndexOf("\"untrustedEvidenceBoundary\"", StringComparison.Ordinal));
        Assert.Contains(FailureLine, received, StringComparison.Ordinal);
        Assert.Contains(StderrLine, received, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelExecutable, received, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelArgument, received, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", received, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".sealed", received, StringComparison.Ordinal);
        var manifest = JsonSerializer.Deserialize<JsonElement>(received);
        Assert.Equal(lineage.PlanId, manifest.GetProperty("implementedPlan").GetProperty("messageId").GetGuid());
        Assert.Equal("VerificationDiagnosis", manifest.GetProperty("expectedResponseContract").GetString());

        // A second diagnosis of the identical report and verification is refused; the status offers the exact correction.
        var repeat = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
        Assert.Equal("agent_attempts.already_diagnosed", Assert.Single(repeat.Errors).Code);
        status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        Assert.True(status.Value.CorrectionApplicable);
        Assert.Equal(2, status.Value.FindingCount);
        Assert.Null(status.Value.DiagnosableExecutionReportMessageId);

        // Correction: the existing contract and adapter, inputs exactly [report, findings...], with the fixed source notice.
        var (correctionId, correctionReport) = await CorrectDiagnosisAsync(host, lineage, diagnosisId, findingIds);
        Assert.Equal(
            [report.Id, .. findingIds],
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == correctionId).OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));
        var correctionStdin = StdinOf(host.Process.Requests.Last(request => request.ExecutablePath != SentinelExecutable));
        Assert.Contains("explicit diagnosis of a failed local verification", correctionStdin, StringComparison.Ordinal);
        Assert.DoesNotContain(FailureLine, correctionStdin, StringComparison.Ordinal);
        Assert.Equal(lineage.RootId, correctionReport.InReplyToMessageId);
        Assert.Equal(
            findingIds,
            await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.AttemptId == correctionId && m.Type == CollaborationMessageType.RevisionResponse)
                .OrderBy(m => m.Sequence).Select(m => m.InReplyToMessageId!.Value).ToListAsync()));

        // The source is spent: the same diagnosis can no longer be corrected, and the corrected checkpoint needs fresh verification.
        var again = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, diagnosisId));
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Assert.Single(again.Errors).Code);
        var reviewBeforeVerification = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, correctionReport.Id));
        Assert.Equal("agent_attempts.verification_evidence_missing", Assert.Single(reviewBeforeVerification.Errors).Code);
        status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        Assert.Equal(correctionReport.Id, status.Value.ReviewableExecutionReportMessageId);
        Assert.Equal(1, status.Value.ReviewCorrectionAttemptsUsed);

        // New explicit verification passes; then the ordinary review of the corrected report judges the implemented plan.
        var passed = await VerifyAsync(host, lineage, exitCode: 0);
        var reviewClaim = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, correctionReport.Id));
        Assert.True(reviewClaim.IsSuccess, reviewClaim.IsFailure ? reviewClaim.Errors[0].Code : null);
        host.Review.FinalResponseJson = ApprovedJson();
        await RunAttemptSupervisorAsync(host, ReviewSupervisorFor(host), reviewClaim.Value.AttemptId);
        Assert.Equal(AgentOutcome.ReviewApproved, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == reviewClaim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var review = JsonSerializer.Deserialize<JsonElement>(host.Review.ReceivedManifests.Single().Text);
        Assert.Equal(lineage.PlanId, review.GetProperty("resolvedPlan").GetProperty("messageId").GetGuid());
        Assert.Equal(report.Id, review.GetProperty("correctionEvidence").GetProperty("previousExecutionReport").GetProperty("messageId").GetGuid());
        var approved = await InDbAsync(host, db => db.CheckpointReviews.Include(r => r.Evidence).SingleAsync());
        Assert.Equal(ReviewDecision.Approved, approved.Decision);
        Assert.Equal([passed.Id], approved.Evidence.Select(item => item.VerificationExecutionId).ToArray());
        // The ordinary review sits on its own contract: the diagnosis never counted as a review and vice versa.
        Assert.Equal(1, host.Diagnosis.InvocationCount);
        Assert.Equal(1, host.Review.InvocationCount);

        // Every claim of the run — including the Codex diagnosis and the diagnosis-origin Claude correction — reaches its REAL
        // adapter as its own sealed manifest, carrying this project's conventions once and no other project's.
        var (attempts, artifacts, workspacePath) = await InDbAsync(host, async db => (
            await db.Attempts.AsNoTracking().Where(a => a.RunId == lineage.RunId && a.Kind == AttemptKind.Agent)
                .OrderBy(a => a.AttemptNumber).ToListAsync(),
            await db.Artifacts.AsNoTracking().Where(a => a.RunId == lineage.RunId && a.Purpose == ArtifactPurpose.AgentContextManifest)
                .ToListAsync(),
            (await db.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == lineage.WorkspaceId)).WorkspacePath));
        var deliveries = new List<RealAdapterInstructionProbe.Delivery>();
        foreach (var attempt in attempts)
        {
            var sealedManifest = artifacts.Single(artifact => artifact.Id == attempt.AgentContextManifestArtifactId);
            var delivery = await RealAdapterInstructionProbe.DeliverAsync(attempt, sealedManifest, workspacePath, _artifactStore, _claudeExecutable);
            RealAdapterInstructionProbe.AssertDeliversOwnConventions(delivery, OwnAgentsText, ForeignMarker);
            deliveries.Add(delivery);
        }

        Assert.Contains(deliveries, delivery => delivery.Contract == AgentResponseContract.VerificationDiagnosis && delivery.Provider == AgentProvider.Codex);
        Assert.Contains(deliveries, delivery => delivery.Contract == AgentResponseContract.ReviewCorrection
            && delivery.Provider == AgentProvider.ClaudeCode
            && delivery.Stdin.Contains("explicit diagnosis of a failed local verification", StringComparison.Ordinal));
        Assert.Contains(deliveries, delivery => delivery.Contract == AgentResponseContract.ImplementationReview);
        Assert.Contains(deliveries, delivery => delivery.Contract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task A_revised_plan_is_the_implemented_plan_the_diagnosis_seals_not_the_planner_root()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: true);
        Assert.NotEqual(lineage.RootId, lineage.PlanId);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);

        var diagnosisId = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);

        var text = host.Diagnosis.ReceivedManifests.Single().Text;
        var plan = JsonSerializer.Deserialize<JsonElement>(text).GetProperty("implementedPlan");
        Assert.Equal(lineage.PlanId, plan.GetProperty("messageId").GetGuid());
        Assert.Equal("Steps unique to [First revised scope]", plan.GetProperty("structuredContent").GetProperty("implementationSteps").GetString());
        Assert.DoesNotContain("Add the table then the query", text, StringComparison.Ordinal);
        Assert.DoesNotContain(lineage.RootId.ToString(), text, StringComparison.Ordinal);

        // The correction still replies to the Planner root and is accepted as a link of the chain.
        var findingIds = await FindingIdsAsync(host, diagnosisId);
        var (_, correctionReport) = await CorrectDiagnosisAsync(host, lineage, diagnosisId, findingIds);
        Assert.Equal(lineage.RootId, correctionReport.InReplyToMessageId);
    }

    [Fact]
    public async Task A_corrected_report_can_be_diagnosed_again_and_the_one_shared_allowance_ends_in_a_single_escalation()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);

        var firstDiagnosis = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);
        var (_, secondReport) = await CorrectDiagnosisAsync(host, lineage, firstDiagnosis, await FindingIdsAsync(host, firstDiagnosis));

        // The corrected checkpoint fails verification again: its report is diagnosed, an exact new identity.
        await VerifyAsync(host, lineage, exitCode: 1);
        var secondDiagnosis = await DiagnoseAsync(host, lineage, secondReport.Id, FindingsJson(2), AgentOutcome.DiagnosisFindingsRecorded);
        Assert.Equal(secondReport.Id, JsonSerializer.Deserialize<JsonElement>(host.Diagnosis.ReceivedManifests.Last().Text).GetProperty("executionReport").GetProperty("messageId").GetGuid());
        var (_, thirdReport) = await CorrectDiagnosisAsync(host, lineage, secondDiagnosis, await FindingIdsAsync(host, secondDiagnosis));

        // The two corrections spent the shared allowance; a third diagnosis can still be recorded, but not corrected.
        await VerifyAsync(host, lineage, exitCode: 1);
        var thirdDiagnosis = await DiagnoseAsync(host, lineage, thirdReport.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);
        var attemptsBefore = await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId));
        var first = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, thirdDiagnosis));
        var second = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, thirdDiagnosis));

        var escalated = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(first.Value);
        Assert.Equal(escalated.EscalationId, Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.Escalated>(second.Value).EscalationId);
        Assert.Equal(attemptsBefore, await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId)));
        var row = await InDbAsync(host, db => db.DiagnosisCorrectionEscalations.SingleAsync());
        Assert.Equal(thirdDiagnosis, row.VerificationDiagnosisAttemptId);
        var message = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == row.CollaborationMessageId));
        Assert.Equal(CollaborationMessageType.Escalation, message.Type);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, message.Provenance);
        // The allowance is immutable per run and this source has no extra-correction authorization: no supported continuation is invented.
        Assert.Contains("explicit human decision", message.StructuredContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("change the run", message.StructuredContentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowance before", message.StructuredContentJson, StringComparison.OrdinalIgnoreCase);
        // No ordinary escalation or extra-correction grant exists for this source, and none is consumed or created.
        Assert.Empty(await InDbAsync(host, db => db.ReviewCorrectionEscalations.ToListAsync()));
        Assert.Empty(await InDbAsync(host, db => db.ReviewCorrectionAuthorizations.ToListAsync()));
        var status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        Assert.True(status.Value.CorrectionBudgetExhausted);
        Assert.Equal(row.Id, status.Value.CorrectionEscalationId);
    }

    [Fact]
    public async Task An_escalating_diagnosis_records_one_escalation_grants_nothing_and_leaves_the_review_gate_closed()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);

        var diagnosisId = await DiagnoseAsync(host, lineage, report.Id, EscalationJson, AgentOutcome.DiagnosisEscalated);

        var messages = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().Where(m => m.AttemptId == diagnosisId).ToListAsync());
        var escalation = Assert.Single(messages);
        Assert.Equal(CollaborationMessageType.Escalation, escalation.Type);
        Assert.Equal(report.Id, escalation.InReplyToMessageId);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, escalation.Provenance);
        Assert.Empty(await InDbAsync(host, db => db.CheckpointReviews.ToListAsync()));
        // It is a successful diagnosis for that identity (no second one), but nothing can be corrected from it.
        var repeat = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
        Assert.Equal("agent_attempts.already_diagnosed", Assert.Single(repeat.Errors).Code);
        var correction = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, diagnosisId));
        Assert.Equal(CreateDiagnosisCorrectionAttemptCommandHandler.NotApplicableCode, Assert.Single(correction.Errors).Code);
        var review = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, report.Id));
        Assert.Equal("agent_attempts.verification_evidence_not_passed", Assert.Single(review.Errors).Code);
        var status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        Assert.False(status.Value.CorrectionApplicable);
        Assert.Equal(escalation.Id, status.Value.DiagnosisEscalationMessageId);
    }

    [Fact]
    public async Task A_failed_diagnosis_may_be_requested_again_explicitly_and_an_invalid_answer_is_never_repaired()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);

        var invalid = await DiagnoseAsync(host, lineage, report.Id, "this is not a diagnosis", AgentOutcome.InvalidStructuredOutput);
        Assert.Empty(await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.AttemptId == invalid).ToListAsync()));
        Assert.Null(await InDbAsync(host, db => db.Attempts.Where(a => a.Id == invalid).Select(a => a.AgentRepairSourceAttemptId).SingleAsync()));

        var retry = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);
        Assert.NotEqual(invalid, retry);
        Assert.Equal(2, host.Diagnosis.InvocationCount);
    }

    [Fact]
    public async Task Evidence_that_changed_before_dispatch_ends_the_claim_without_a_provider_invocation()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);
        var claim = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
        Assert.True(claim.IsSuccess);

        // A newer execution of the same command lands between the claim and the dispatch.
        await VerifyAsync(host, lineage, exitCode: 1);
        await RunAttemptSupervisorAsync(host, DiagnosisSupervisorFor(host), claim.Value.AttemptId);

        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId));
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(0, host.Diagnosis.InvocationCount);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
        Assert.Empty(await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.AttemptId == attempt.Id).ToListAsync()));
    }

    [Fact]
    public async Task A_response_produced_against_evidence_that_changed_while_the_provider_ran_records_no_finding_but_keeps_the_truthful_evidence()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);
        var claim = await SendAsync(host, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
        Assert.True(claim.IsSuccess);

        host.Diagnosis.FinalResponseJson = FindingsJson(1);
        host.Diagnosis.DuringInvocation = async () => await VerifyAsync(host, lineage, exitCode: 1);
        await RunAttemptSupervisorAsync(host, DiagnosisSupervisorFor(host), claim.Value.AttemptId);

        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == claim.Value.AttemptId));
        Assert.Equal(AgentOutcome.VerificationEvidenceChanged, attempt.AgentOutcome);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        Assert.NotNull(attempt.GetAgentProcessExecutionEvidence());
        Assert.Empty(await InDbAsync(host, db => db.CollaborationMessages.Where(m => m.AttemptId == attempt.Id).ToListAsync()));
        Assert.Contains(
            await InDbAsync(host, db => db.Artifacts.Where(a => a.AttemptId == attempt.Id).Select(a => a.Purpose).ToListAsync()),
            purpose => purpose == ArtifactPurpose.AgentFinalResponse);
        host.Diagnosis.DuringInvocation = null;
        // The changed verification can be diagnosed afresh: it is a different exact identity.
        var fresh = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);
        Assert.NotEqual(attempt.Id, fresh);
    }

    [Fact]
    public async Task A_claimed_diagnosis_replays_its_sealed_manifest_after_a_restart()
    {
        Guid attemptId;
        string sealedBytes;
        var evidence = new StagedEvidence();
        await using (var first = BuildHost(evidence))
        {
            var lineage = await SeedLineageAsync(first, revised: false);
            var (_, report) = await ImplementAsync(first, lineage);
            await VerifyAsync(first, lineage, exitCode: 1);
            var claim = await SendAsync(first, new CreateVerificationDiagnosisAttemptCommand(lineage.RunId, report.Id));
            Assert.True(claim.IsSuccess);
            attemptId = claim.Value.AttemptId;
            sealedBytes = (await ReadManifestAsync(first, attemptId)).GetRawText();
        }

        // A new host over the same database and artifacts: the claimed, undispatched attempt is picked up and handed exactly the
        // sealed bytes; nothing is rebuilt from today's evidence.
        evidence.Stage = 1;
        await using var second = BuildHost(evidence);
        second.Diagnosis.FinalResponseJson = FindingsJson(1);
        await RunAttemptSupervisorAsync(second, DiagnosisSupervisorFor(second), attemptId);

        Assert.Equal(AgentOutcome.DiagnosisFindingsRecorded, await InDbAsync(second, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.AgentOutcome).SingleAsync()));
        Assert.Equal(sealedBytes, JsonSerializer.Deserialize<JsonElement>(second.Diagnosis.ReceivedManifests.Single().Text).GetRawText());
    }

    private const string DirectGuidanceText = "SENTINEL-HOSTED-GUIDE Keep the fix inside the existing helper.";

    /// <summary>The Claude CLI invocations (those carrying standard input) the process double has seen since <paramref name="skip"/>.</summary>
    private static List<ProcessExecutionRequest> ClaudeInvocationsSince(Host host, int skip)
    {
        lock (host.Process.Requests)
        {
            return host.Process.Requests.Skip(skip).Where(request => request.StandardInput is not null).ToList();
        }
    }

    [Fact]
    public async Task A_guided_diagnosis_correction_reaches_the_real_adapter_with_the_exact_sealed_text_once_and_is_projected()
    {
        await using var host = BuildHost();
        var lineage = await SeedLineageAsync(host, revised: false);
        var (_, report) = await ImplementAsync(host, lineage);
        await VerifyAsync(host, lineage, exitCode: 1);
        var diagnosisId = await DiagnoseAsync(host, lineage, report.Id, FindingsJson(2), AgentOutcome.DiagnosisFindingsRecorded);
        var findingIds = await FindingIdsAsync(host, diagnosisId);

        var claim = await SendAsync(host, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, diagnosisId, "  " + DirectGuidanceText + "\r\n"));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        host.Process.FinalResponse = CorrectionResponse(findingIds);
        var before = host.Process.Requests.Count;
        await RunAttemptSupervisorAsync(host, CorrectionSupervisorFor(host), created.AttemptId);

        Assert.Equal(AgentOutcome.CorrectionApplied, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == created.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var invocation = Assert.Single(ClaudeInvocationsSince(host, before));
        var stdin = Encoding.UTF8.GetString(invocation.StandardInput!);
        Assert.Equal(1, stdin.Split("SENTINEL-HOSTED-GUIDE").Length - 1);
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, DirectGuidanceText));
        var sealedManifest = await ReadManifestAsync(host, created.AttemptId);
        Assert.Equal(sealedManifest.GetRawText(), JsonSerializer.Deserialize<JsonElement>(stdin).GetRawText());
        Assert.True(sealedManifest.TryGetProperty("sourceNotice", out _));
        Assert.DoesNotContain(invocation.Arguments, argument => argument.Contains("SENTINEL", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation.EnvironmentVariables.Values, value => value.Contains("SENTINEL", StringComparison.Ordinal));

        var status = await QueryAsync(host, new GetVerificationDiagnosisStatusQuery(lineage.RunId));
        var fact = Assert.IsType<DirectHumanGuidanceFact>(status.Value.CorrectionDirectGuidance);
        Assert.Equal(DirectHumanGuidanceEvidence.Provided, fact.Evidence);
        Assert.Equal(DirectGuidanceText, fact.Text);
        Assert.Empty(await InDbAsync(host, db => db.ReviewCorrectionAuthorizations.ToListAsync()));
    }

    [Fact]
    public async Task A_guided_diagnosis_correction_is_replayed_after_a_restart_from_its_sealed_manifest()
    {
        Guid attemptId;
        string sealedBytes;
        IReadOnlyList<Guid> findingIds;
        var evidence = new StagedEvidence();
        await using (var first = BuildHost(evidence))
        {
            var lineage = await SeedLineageAsync(first, revised: false);
            var (_, report) = await ImplementAsync(first, lineage);
            await VerifyAsync(first, lineage, exitCode: 1);
            var diagnosisId = await DiagnoseAsync(first, lineage, report.Id, FindingsJson(1), AgentOutcome.DiagnosisFindingsRecorded);
            findingIds = await FindingIdsAsync(first, diagnosisId);
            var claim = await SendAsync(first, new CreateDiagnosisCorrectionAttemptCommand(lineage.RunId, diagnosisId, DirectGuidanceText));
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            attemptId = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value).AttemptId;
            sealedBytes = (await ReadManifestAsync(first, attemptId)).GetRawText();
        }

        // A new host over the same database and artifacts: the claimed, undispatched guided correction is picked up and handed
        // exactly the sealed bytes; nothing is rebuilt from today's settings or evidence.
        await using var second = BuildHost(evidence);
        second.Process.FinalResponse = CorrectionResponse(findingIds);
        await RunAttemptSupervisorAsync(second, CorrectionSupervisorFor(second), attemptId);

        Assert.Equal(AgentOutcome.CorrectionApplied, await InDbAsync(second, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var invocation = Assert.Single(ClaudeInvocationsSince(second, 0));
        var stdin = Encoding.UTF8.GetString(invocation.StandardInput!);
        Assert.Equal(sealedBytes, JsonSerializer.Deserialize<JsonElement>(stdin).GetRawText());
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, DirectGuidanceText));
    }
}
