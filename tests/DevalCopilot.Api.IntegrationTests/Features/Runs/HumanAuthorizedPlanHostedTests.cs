using System.Text;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
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

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The human-authorized escalated plan end to end (ADR-0016) through the real supervisors, the real mediator and EF
/// pipeline, and the REAL Claude adapters. Only the process boundary and the Codex review adapter are deterministic
/// doubles (no provider is ever started). The whole lineage is written by production commands, so the escalation, the
/// authorization, the claim, the ExecutionReport, verification, code review, an ordinary correction, and its re-review
/// all run against exactly the evidence production writes. A double proves provider reachability only, never real-provider
/// reliability.
/// </summary>
public sealed class HumanAuthorizedPlanHostedTests : IDisposable
{
    private const string Rationale = "SENTINEL-PLAN-AUTH I reviewed both rounds and accept the final plan.";

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

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly string _toolRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-hosted-tool-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _claudeExecutable;

    public HumanAuthorizedPlanHostedTests()
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

    private sealed class StagedEvidence : IGitWorkspaceEvidenceReader
    {
        public int Stage { get; set; }

        /// <summary>The project's own root instruction files as every claim's capture observes them.</summary>
        public GitWorkspaceInstructionContext? Instructions { get; set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, Head, Fingerprints[Stage],
                Stage == 0 ? [] : [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], null, null, Instructions));
    }

    private const string OwnAgentsText = "OWN-PROJECT-CONVENTIONS: keep handlers small.\r\nSay \"yes\" & use <Operation>Handler — naïve 𝄞.\r\n";
    private const string ForeignMarker = "FOREIGN-PROJECT-CONVENTIONS";

    private sealed class RecordingProcessDouble(StagedEvidence evidence) : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public string FinalResponse { get; set; } = "{}";

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
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

        /// <summary>The sealed context manifest text exactly as the adapter is handed it, per invocation.</summary>
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
            foreach (var (purpose, content) in new[]
                     {
                         (ArtifactPurpose.AgentFinalResponse, FinalResponseJson),
                         (ArtifactPurpose.AgentStandardOutput, string.Empty),
                         (ArtifactPurpose.AgentStandardError, string.Empty),
                     })
            {
                var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, purpose);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            return new ImplementationReviewInvocationResult(
                ImplementationReviewInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private sealed record Host(
        ServiceProvider Provider, StagedEvidence Evidence, RecordingProcessDouble Process, FakeReviewAdapter Review) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private Host BuildHost()
    {
        var evidence = new StagedEvidence { Instructions = RealAdapterInstructionProbe.OwnContext(OwnAgentsText) };
        var process = new RecordingProcessDouble(evidence);
        var review = new FakeReviewAdapter(_artifactStore);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<IProcessExecutionAdapter>(process);
        services.AddSingleton<IClaudeImplementationAdapter>(sp => new ClaudeImplementationAdapter(process, _artifactStore));
        services.AddSingleton<IClaudeReviewCorrectionAdapter>(sp => new ClaudeReviewCorrectionAdapter(process, _artifactStore));
        services.AddSingleton<ICodexImplementationReviewAdapter>(review);
        services.AddSingleton<IRunEventNotifier>(new NoopNotifier());
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return new Host(services.BuildServiceProvider(), evidence, process, review);
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

    private static ReviewCorrectionSupervisor CorrectionSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<IClaudeReviewCorrectionAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ReviewCorrectionSupervisor>.Instance);

    private static async Task RunSupervisorAsync(BackgroundService supervisor, Host host, Guid attemptId, bool expectTerminal)
    {
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(expectTerminal ? 15 : 2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(40);
                await using var scope = host.Provider.CreateAsyncScope();
                var status = await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts
                    .Where(a => a.Id == attemptId).Select(a => a.Status).SingleAsync();
                if (expectTerminal && status != AttemptStatus.Running)
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

    private static string StdinOf(ProcessExecutionRequest request) => Encoding.UTF8.GetString(request.StandardInput!);

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

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

    // ---- production lineage ------------------------------------------------------------------------------------

    private sealed record Lineage(
        Guid RunId, Guid WorkspaceId, Guid ProjectId, Guid RootId, Guid FirstRevisionId, Guid FinalId, Guid EscalationId,
        IReadOnlyList<Guid> SecondChallengeIds, IReadOnlyList<Guid> SecondDecisionIds);

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

    private sealed record Seeded(Guid RunId, Guid WorkspaceId, Guid ProjectId, Guid RootId);

    /// <summary>A production-written run with one Planner root Proposal, a ready workspace, lease and both providers observed.</summary>
    private async Task<Seeded> SeedRootAsync(Host host, int maximumAgentAttempts, TimeSpan? maximumAgentInvocationTime = null)
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
            maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: maximumAgentInvocationTime);
        run.Claim(now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-plan-auth-ws-{Guid.NewGuid():N}"),
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

        return new Seeded(run.Id, workspace.Id, project.Id, root.Id);
    }

    /// <summary>The escalation content written before ADR-0020, reproduced here independently of production: a historical
    /// record that must keep authorizing, replaying and validating exactly as it did when it was written.</summary>
    private static string HistoricalEscalationContent(Guid root, Guid first, Guid second, IReadOnlyList<Guid> challenges) =>
        JsonSerializer.Serialize(new
        {
            unresolvedDecision =
                "The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.",
            options =
                "Decide manually whether the revised proposal is acceptable, or start a new explicit planning request. Neither is chosen by this record.",
            consequences =
                "The revised proposal is not implementable through this lineage and is not approved; a third review is not available.",
            evidence =
                $"Root proposal {root}; first revision {first}; second revision {second}; {challenges.Count} second-round "
                + $"challenge(s) each decided once: {string.Join(", ", challenges)}.",
            recommendedChoice =
                "Read the second-round decisions before starting any new planning request.",
        });

    private async Task<Lineage> SeedEscalatedLineageAsync(Host host, int maximumAgentAttempts = 16, bool historicalEscalation = false)
    {
        var seeded = await SeedRootAsync(host, maximumAgentAttempts);
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();

        var first = await ReviewAndResolveAsync(mediator, db, seeded.RunId, seeded.RootId, 2, "First revised scope");
        var second = await ReviewAndResolveAsync(mediator, db, seeded.RunId, first.RevisedId, 2, "Second revised scope");
        var escalation = await db.CollaborationMessages.SingleAsync(
            message => message.RunId == seeded.RunId && message.Type == CollaborationMessageType.Escalation);
        Assert.Equal(second.RevisedId, escalation.InReplyToMessageId);
        if (historicalEscalation)
        {
            // What the production writer recorded before ADR-0020: the same message, its original serialization.
            var historical = HistoricalEscalationContent(seeded.RootId, first.RevisedId, second.RevisedId, second.ChallengeIds);
            Assert.NotEqual(historical, escalation.StructuredContentJson);
            await db.CollaborationMessages.Where(message => message.Id == escalation.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, historical));
        }
        else
        {
            Assert.DoesNotContain("not implementable through this lineage", escalation.StructuredContentJson, StringComparison.Ordinal);
            Assert.Contains("separately authorize one implementation", escalation.StructuredContentJson, StringComparison.Ordinal);
        }

        var decisions = await db.CollaborationMessages
            .Where(message => message.AttemptId == second.ResolverAttemptId && message.Type == CollaborationMessageType.Decision)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();

        return new Lineage(seeded.RunId, seeded.WorkspaceId, seeded.ProjectId, seeded.RootId, first.RevisedId, second.RevisedId, escalation.Id, second.ChallengeIds, decisions);
    }

    private sealed record Round(Guid ResolverAttemptId, Guid RevisedId, List<Guid> ChallengeIds);

    private static async Task<Round> ReviewAndResolveAsync(
        IApplicationMediator mediator, DevalCopilotDbContext db, Guid runId, Guid proposalId, int challengeCount, string scope)
    {
        var claim = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, claim.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var review = ClaudeCriticalReviewResponseParser.TryParse(ChallengeResponseJson(challengeCount));
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, claim.Value.AttemptId, AgentOutcome.Challenged, Fingerprints[0], [], review, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var challengeIds = await db.CollaborationMessages
            .Where(message => message.AttemptId == claim.Value.AttemptId && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();

        var resolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(runId, claim.Value.AttemptId), CancellationToken.None);
        Assert.True(resolver.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, resolver.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var resolution = ChallengeResolutionResponseParser.TryParse(ResolvedResponseJson(challengeIds, scope), challengeIds.ToHashSet());
        Assert.NotNull(resolution);
        Assert.True((await mediator.SendAsync(
            new RecordChallengeResolutionResultCommand(
                runId, resolver.Value.AttemptId, AgentOutcome.Resolved, Fingerprints[0], [], resolution, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var revised = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == resolver.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);
        return new Round(resolver.Value.AttemptId, revised.Id, challengeIds);
    }

    private async Task<AuthorizePlanningImplementationCommandResult> AuthorizeAsync(Host host, Lineage lineage, string rationale = Rationale)
    {
        var result = await SendAsync(host, new AuthorizePlanningImplementationCommand(lineage.RunId, lineage.EscalationId, rationale));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    private async Task<Guid> ClaimAsync(Host host, Lineage lineage)
    {
        var claim = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return claim.Value.AttemptId;
    }

    private async Task SeedPassedVerificationAsync(Host host, Lineage lineage, int executionNumber)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var command = await db.VerificationCommands.SingleOrDefaultAsync(candidate => candidate.ProjectId == lineage.ProjectId);
        if (command is null)
        {
            command = VerificationCommand.Configure(
                Guid.NewGuid(), lineage.ProjectId, 1, "Backend tests", @"C:\dotnet.exe", ["test"], 300, true, DateTimeOffset.UtcNow);
            db.VerificationCommands.Add(command);
            await db.SaveChangesAsync();
        }

        var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == lineage.WorkspaceId);
        var checkpoint = await db.GitCheckpoints.Where(candidate => candidate.WorkspaceId == lineage.WorkspaceId)
            .OrderByDescending(candidate => candidate.CheckpointNumber).FirstAsync();
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), lineage.ProjectId, executionNumber, workspace, checkpoint, command, DateTimeOffset.UtcNow);
        execution.MarkDispatched(DateTimeOffset.UtcNow);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, DateTimeOffset.UtcNow);
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
    }

    private static string ImplementationReport(IReadOnlyList<string> changed) => JsonSerializer.Serialize(new
    {
        summary = "Implemented the final ledger plan.",
        changedRelativePaths = changed,
        implementationNotes = "Added the migration and the query handler.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the backend test suite.",
    });

    private static string ChangesRequestedJson() => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
        summary = "One material issue was found.",
        rationale = (string?)null,
        residualRisks = (string?)null,
        findings = new[]
        {
            new
            {
                severity = ImplementationReviewOutputSchema.Severities[0],
                category = ImplementationReviewOutputSchema.Categories[0],
                summary = "The branch is incomplete.",
                evidence = "Evidence 1",
                requiredChange = "Complete the branch.",
                affectedRelativePath = (string?)"src/Foo.cs",
            },
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

    private static string CorrectionResponse(Guid findingId) => JsonSerializer.Serialize(new
    {
        revisionResponses = new[]
        {
            new { findingMessageId = findingId, disposition = "Fixed", evidence = "The branch was completed.", resultingSourceChanges = "Completed the branch." },
        },
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

    // ---- the complete chain --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_second_resolution_authorization_explicit_implementation_report_verification_review_correction_and_re_review_all_hold(bool historicalEscalation)
    {
        await using var host = BuildHost();
        var lineage = await SeedEscalatedLineageAsync(host, historicalEscalation: historicalEscalation);

        // Without the human decision the final plan stays refused and nothing starts.
        var refused = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));
        Assert.Equal("agent_attempts.proposal_lineage_exhausted", Assert.Single(refused.Errors).Code);
        Assert.Equal(AttemptStatus.Completed, await InDbAsync(host, db => db.Attempts.Where(a => a.AgentRole == AgentRole.Resolver)
            .OrderByDescending(a => a.AttemptNumber).Select(a => a.Status).FirstAsync()));

        // Authorization is a separate human decision: it claims no attempt, reserves no budget, and starts no provider.
        var attemptsBeforeAuthorization = await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId));
        var authorization = await AuthorizeAsync(host, lineage);
        Assert.Equal(attemptsBeforeAuthorization, await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId)));
        Assert.Empty(host.Process.Requests);
        Assert.Equal(0, host.Review.InvocationCount);

        // The explicit implementation request consumes the one grant with the attempt and its exact ordered inputs.
        var attemptId = await ClaimAsync(host, lineage);
        Assert.Equal(
            lineage.SecondDecisionIds.Prepend(lineage.FinalId).Append(authorization.HumanInstructionMessageId),
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == attemptId)
                .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));
        Assert.Equal(attemptId, await InDbAsync(host, db => db.PlanningImplementationAuthorizations
            .Where(g => g.Id == authorization.AuthorizationId).Select(g => g.ConsumedByAttemptId).SingleAsync()));

        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: true);

        var implementationRequest = Assert.Single(host.Process.Requests);
        var stdin = StdinOf(implementationRequest);
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-PLAN-AUTH"));
        Assert.True(PlanningImplementationAuthorizationManifest.Agrees(stdin, new PlanningImplementationAuthorizationFact(
            authorization.AuthorizationId, lineage.EscalationId, lineage.FinalId, authorization.HumanInstructionMessageId, Rationale)));
        Assert.Equal("Read,Edit,Write,Glob,Grep", implementationRequest.Arguments[implementationRequest.Arguments.ToList().IndexOf("--tools") + 1]);
        Assert.Equal("acceptEdits", implementationRequest.Arguments[implementationRequest.Arguments.ToList().IndexOf("--permission-mode") + 1]);
        Assert.DoesNotContain(implementationRequest.Arguments, argument => argument.Contains("SENTINEL", StringComparison.Ordinal));
        Assert.DoesNotContain(implementationRequest.EnvironmentVariables.Values, value => value.Contains("SENTINEL", StringComparison.Ordinal));

        var implemented = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.Equal(AttemptStatus.Completed, implemented.Status);
        Assert.Equal(AgentOutcome.Implemented, implemented.AgentOutcome);
        Assert.NotEqual(implemented.AgentGitCheckpointId, implemented.AgentResultGitCheckpointId);
        var report = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == attemptId && m.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(lineage.FinalId, report.InReplyToMessageId);

        // Local verification for the exact result checkpoint, then the code review claim of that report.
        await SeedPassedVerificationAsync(host, lineage, executionNumber: 1);
        var firstReviewClaim = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, report.Id));
        Assert.True(firstReviewClaim.IsSuccess, firstReviewClaim.IsFailure ? firstReviewClaim.Errors[0].Code : null);
        // The first review answers in an unusable shape: the manual format repair then reviews the same report.
        host.Review.FinalResponseJson = "this is not a review";
        await RunSupervisorAsync(ReviewSupervisorFor(host), host, firstReviewClaim.Value.AttemptId, expectTerminal: true);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, await InDbAsync(host, db => db.Attempts
            .Where(a => a.Id == firstReviewClaim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var reviewClaim = await SendAsync(host, CreateCodeReviewAttemptCommand.ForRepair(lineage.RunId, firstReviewClaim.Value.AttemptId));
        Assert.True(reviewClaim.IsSuccess, reviewClaim.IsFailure ? reviewClaim.Errors[0].Code : null);
        host.Review.FinalResponseJson = ChangesRequestedJson();
        await RunSupervisorAsync(ReviewSupervisorFor(host), host, reviewClaim.Value.AttemptId, expectTerminal: true);
        Assert.Equal(AgentOutcome.ReviewChangesRequested, await InDbAsync(host, db => db.Attempts
            .Where(a => a.Id == reviewClaim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var finding = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == reviewClaim.Value.AttemptId && m.Type == CollaborationMessageType.ReviewFinding));
        Assert.Equal(report.Id, finding.InReplyToMessageId);
        // The review target is the implemented final plan — in the initial review and in its format repair — both as the
        // adapter received it and as sealed. The Planner root stays a separate historical lineage identity.
        Assert.Equal(AgentOutcome.ReviewChangesRequested, await InDbAsync(host, db => db.Attempts
            .Where(a => a.Id == reviewClaim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        AssertReviewsFinalPlan(host, firstReviewClaim.Value.AttemptId, lineage, expectedRepair: false);
        AssertReviewsFinalPlan(host, reviewClaim.Value.AttemptId, lineage, expectedRepair: true);
        Assert.Equal(
            (await ReadManifestAsync(host, reviewClaim.Value.AttemptId)).GetRawText(),
            JsonSerializer.Deserialize<JsonElement>(host.Review.ReceivedManifests.Single(m => m.AttemptId == reviewClaim.Value.AttemptId).Text).GetRawText());
        var rootAndFirst = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking()
            .Where(m => m.Id == lineage.RootId || m.Id == lineage.FirstRevisionId || m.Id == lineage.FinalId)
            .Select(m => new { m.Id, m.StructuredContentJson }).ToListAsync());
        Assert.Equal(3, rootAndFirst.Select(m => m.StructuredContentJson).Distinct().Count());
        Assert.Equal(3, new[] { lineage.RootId, lineage.FirstRevisionId, lineage.FinalId }.Distinct().Count());

        // Ordinary correction and its re-review run unchanged against the same report chain.
        var correction = await SendAsync(host, new CreateReviewCorrectionAttemptCommand(lineage.RunId, reviewClaim.Value.AttemptId));
        var correctionClaim = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(correction.Value);
        Assert.Equal(
            [report.Id, finding.Id],
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == correctionClaim.AttemptId)
                .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));
        host.Process.FinalResponse = CorrectionResponse(finding.Id);
        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, correctionClaim.AttemptId, expectTerminal: true);
        Assert.Equal(2, host.Process.Requests.Count);
        Assert.Equal(AgentOutcome.CorrectionApplied, await InDbAsync(host, db => db.Attempts
            .Where(a => a.Id == correctionClaim.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var response = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == correctionClaim.AttemptId && m.Type == CollaborationMessageType.RevisionResponse));
        Assert.Equal(finding.Id, response.InReplyToMessageId);
        var correctionReport = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == correctionClaim.AttemptId && m.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(lineage.RootId, correctionReport.InReplyToMessageId);

        await SeedPassedVerificationAsync(host, lineage, executionNumber: 2);
        var reReview = await SendAsync(host, new CreateCodeReviewAttemptCommand(lineage.RunId, correctionReport.Id));
        Assert.True(reReview.IsSuccess, reReview.IsFailure ? reReview.Errors[0].Code : null);
        host.Review.FinalResponseJson = ApprovedJson();
        await RunSupervisorAsync(ReviewSupervisorFor(host), host, reReview.Value.AttemptId, expectTerminal: true);
        Assert.Equal(AgentOutcome.ReviewApproved, await InDbAsync(host, db => db.Attempts
            .Where(a => a.Id == reReview.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        Assert.Equal(3, host.Review.InvocationCount);
        // The correction re-review judges the same implemented final plan and keeps the exact correction evidence.
        AssertReviewsFinalPlan(host, reReview.Value.AttemptId, lineage, expectedRepair: false);
        var reReviewManifest = JsonSerializer.Deserialize<JsonElement>(
            host.Review.ReceivedManifests.Single(m => m.AttemptId == reReview.Value.AttemptId).Text);
        Assert.Equal(report.Id, reReviewManifest.GetProperty("correctionEvidence").GetProperty("previousExecutionReport").GetProperty("messageId").GetGuid());
        Assert.Equal(correctionReport.Id, reReviewManifest.GetProperty("executionReport").GetProperty("messageId").GetGuid());

        // The grant stayed spent exactly once and the authorization stays one message and one relation.
        Assert.Equal(1, await InDbAsync(host, db => db.PlanningImplementationAuthorizations.CountAsync()));
        Assert.Equal(1, await InDbAsync(host, db => db.CollaborationMessages.CountAsync(m => m.Type == CollaborationMessageType.HumanInstruction)));
        // The run's ordinary gates (here its time budget) refuse any further claim, and the spent grant is never reused.
        var second = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));
        Assert.True(second.IsFailure);
        Assert.Equal(attemptId, await InDbAsync(host, db => db.PlanningImplementationAuthorizations.Select(g => g.ConsumedByAttemptId).SingleAsync()));

        await AssertEveryClaimDeliversTheProjectsOwnConventionsThroughItsRealAdapterAsync(host, lineage, expectDiagnosis: false);
    }

    /// <summary>Every Agent attempt this production lineage claimed — planning, both critical reviews and resolutions, the
    /// authorized implementation, the code review and its format repair, the correction and its re-review — is fed, from its
    /// own sealed manifest, through the REAL adapter of its role and provider. What each adapter hands the provider is the
    /// sealed bytes exactly, carrying the project's own conventions once and nothing from any other project.</summary>
    private async Task AssertEveryClaimDeliversTheProjectsOwnConventionsThroughItsRealAdapterAsync(
        Host host, Lineage lineage, bool expectDiagnosis)
    {
        var (attempts, artifacts, workspacePath) = await InDbAsync(host, async db => (
            await db.Attempts.AsNoTracking().Where(a => a.RunId == lineage.RunId && a.Kind == AttemptKind.Agent)
                .OrderBy(a => a.AttemptNumber).ToListAsync(),
            await db.Artifacts.AsNoTracking().Where(a => a.RunId == lineage.RunId && a.Purpose == ArtifactPurpose.AgentContextManifest)
                .ToListAsync(),
            (await db.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == lineage.WorkspaceId)).WorkspacePath));
        var grant = await InDbAsync(host, db => db.PlanningImplementationAuthorizations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == lineage.RunId));
        var deliveries = new List<RealAdapterInstructionProbe.Delivery>();
        foreach (var attempt in attempts)
        {
            var manifest = artifacts.Single(artifact => artifact.Id == attempt.AgentContextManifestArtifactId);
            var authorization = grant is not null && grant.ConsumedByAttemptId == attempt.Id
                ? new PlanningImplementationAuthorizationFact(
                    grant.Id, grant.EscalationMessageId, grant.FinalProposalMessageId, grant.HumanInstructionMessageId, Rationale)
                : null;
            var delivery = await RealAdapterInstructionProbe.DeliverAsync(
                attempt, manifest, workspacePath, _artifactStore, _claudeExecutable, authorization);
            RealAdapterInstructionProbe.AssertDeliversOwnConventions(delivery, OwnAgentsText, ForeignMarker);
            deliveries.Add(delivery);
        }

        var expected = new[]
        {
            AgentResponseContract.Proposal, AgentResponseContract.CriticalReview, AgentResponseContract.ChallengeResolution,
            AgentResponseContract.ImplementationReport, AgentResponseContract.ImplementationReview, AgentResponseContract.ReviewCorrection,
        }.Concat(expectDiagnosis ? [AgentResponseContract.VerificationDiagnosis] : []);
        Assert.Equal(expected.Order(), deliveries.Select(delivery => delivery.Contract).Distinct().Order());
        Assert.Equal(
            new[] { AgentProvider.ClaudeCode, AgentProvider.Codex }.Order(),
            deliveries.Select(delivery => delivery.Provider).Distinct().Order());
        // The format-repair claim of the code review is among them, and it too carries the conventions.
        Assert.Contains(deliveries, delivery => delivery.Stdin.Contains("formatRepairNotice", StringComparison.Ordinal));
    }

    private const string RootPlanSummary = "Add the ledger table and its query.";
    private const string RootPlanSteps = "Add the table then the query";

    private static void AssertReviewsFinalPlan(Host host, Guid reviewAttemptId, Lineage lineage, bool expectedRepair)
    {
        var text = host.Review.ReceivedManifests.Single(m => m.AttemptId == reviewAttemptId).Text;
        var manifest = JsonSerializer.Deserialize<JsonElement>(text);
        var plan = manifest.GetProperty("resolvedPlan");
        Assert.Equal(lineage.FinalId, plan.GetProperty("messageId").GetGuid());
        Assert.NotEqual(lineage.RootId, plan.GetProperty("messageId").GetGuid());
        Assert.NotEqual(lineage.FirstRevisionId, plan.GetProperty("messageId").GetGuid());
        Assert.Contains("[Second revised scope]", plan.GetProperty("summary").GetString());
        Assert.Equal("Steps unique to [Second revised scope]", plan.GetProperty("structuredContent").GetProperty("implementationSteps").GetString());
        // Neither superseded plan's text reaches the review adapter.
        Assert.DoesNotContain("First revised scope", text, StringComparison.Ordinal);
        Assert.DoesNotContain(RootPlanSummary, text, StringComparison.Ordinal);
        Assert.DoesNotContain(RootPlanSteps, text, StringComparison.Ordinal);
        Assert.Equal(expectedRepair, manifest.TryGetProperty("formatRepairNotice", out _));
    }

    // ---- Implemented-plan identity of ordinary plans (ADR-0017) ------------------------------------------------------

    private static readonly string AcceptJson = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    private static async Task AcceptAsync(IApplicationMediator mediator, Guid runId, Guid proposalId)
    {
        var claim = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, claim.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var review = ClaudeCriticalReviewResponseParser.TryParse(AcceptJson);
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, claim.Value.AttemptId, AgentOutcome.Accepted, Fingerprints[0], [], review, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
    }

    /// <summary>The ordinary lineages, written by production commands: the plan the implementation will consume is
    /// <c>FinalId</c> — the Planner root (accepted), or its first Resolver revision (optionally Accepted by its own review).</summary>
    private async Task<Lineage> SeedOrdinaryAsync(Host host, bool revised, bool acceptPlan)
    {
        var seeded = await SeedRootAsync(host, 24, TimeSpan.FromHours(24));
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        if (!revised)
        {
            await AcceptAsync(mediator, seeded.RunId, seeded.RootId);
            return new Lineage(seeded.RunId, seeded.WorkspaceId, seeded.ProjectId, seeded.RootId, seeded.RootId, seeded.RootId, Guid.Empty, [], []);
        }

        var round = await ReviewAndResolveAsync(mediator, db, seeded.RunId, seeded.RootId, 2, "First revised scope");
        if (acceptPlan)
        {
            await AcceptAsync(mediator, seeded.RunId, round.RevisedId);
        }

        var decisions = await db.CollaborationMessages
            .Where(message => message.AttemptId == round.ResolverAttemptId && message.Type == CollaborationMessageType.Decision)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();
        return new Lineage(
            seeded.RunId, seeded.WorkspaceId, seeded.ProjectId, seeded.RootId, round.RevisedId, round.RevisedId, Guid.Empty, round.ChallengeIds, decisions);
    }

    private async Task<(Guid AttemptId, CollaborationMessage Report)> ImplementAsync(Host host, Lineage lineage)
    {
        var attemptId = await ClaimAsync(host, lineage);
        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: true);
        Assert.Equal(AgentOutcome.Implemented, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var report = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == attemptId && m.Type == CollaborationMessageType.ExecutionReport));
        return (attemptId, report);
    }

    private async Task<Guid> ReviewAsync(Host host, Lineage lineage, Guid reportId, string response, AgentOutcome expected, Guid? repairOf = null)
    {
        var claim = await SendAsync(host, repairOf is { } source
            ? CreateCodeReviewAttemptCommand.ForRepair(lineage.RunId, source)
            : new CreateCodeReviewAttemptCommand(lineage.RunId, reportId));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        host.Review.FinalResponseJson = response;
        await RunSupervisorAsync(ReviewSupervisorFor(host), host, claim.Value.AttemptId, expectTerminal: true);
        Assert.Equal(expected, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == claim.Value.AttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        return claim.Value.AttemptId;
    }

    /// <summary>What the review adapter actually received names the implemented plan — identifier, summary and structured
    /// content — and none of the other plans' text.</summary>
    private async Task AssertReviewedPlanAsync(Host host, Guid reviewAttemptId, Guid planId, string expectedSteps, params string[] absentText)
    {
        var text = host.Review.ReceivedManifests.Single(m => m.AttemptId == reviewAttemptId).Text;
        var manifest = JsonSerializer.Deserialize<JsonElement>(text);
        var plan = manifest.GetProperty("resolvedPlan");
        var message = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == planId));
        Assert.Equal(planId, plan.GetProperty("messageId").GetGuid());
        Assert.Equal(message.Summary, plan.GetProperty("summary").GetString());
        Assert.Equal(expectedSteps, plan.GetProperty("structuredContent").GetProperty("implementationSteps").GetString());
        foreach (var absent in absentText)
        {
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_ordinary_first_revision_is_reviewed_repaired_corrected_twice_and_re_reviewed_as_the_implemented_revision()
    {
        await using var host = BuildHost();
        var lineage = await SeedOrdinaryAsync(host, revised: true, acceptPlan: false);
        const string revisedSteps = "Steps unique to [First revised scope]";
        string[] superseded = [RootPlanSummary, RootPlanSteps, lineage.RootId.ToString()];

        var (implementationId, report) = await ImplementAsync(host, lineage);
        Assert.Equal(lineage.FinalId, report.InReplyToMessageId);
        Assert.Equal(
            lineage.SecondDecisionIds.Prepend(lineage.FinalId),
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == implementationId)
                .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));
        await SeedPassedVerificationAsync(host, lineage, executionNumber: 1);

        // The first review answers in an unusable shape; the manual repair then reviews the same report.
        var invalidReview = await ReviewAsync(host, lineage, report.Id, "not a review", AgentOutcome.InvalidStructuredOutput);
        var repair = await ReviewAsync(host, lineage, report.Id, ChangesRequestedJson(), AgentOutcome.ReviewChangesRequested, repairOf: invalidReview);
        await AssertReviewedPlanAsync(host, invalidReview, lineage.FinalId, revisedSteps, superseded);
        await AssertReviewedPlanAsync(host, repair, lineage.FinalId, revisedSteps, superseded);
        var finding = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == repair && m.Type == CollaborationMessageType.ReviewFinding));

        // First correction: replies keep the root and the findings; the correction manifest carries no plan.
        var correction = await SendAsync(host, new CreateReviewCorrectionAttemptCommand(lineage.RunId, repair));
        var correctionClaim = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(correction.Value);
        host.Process.FinalResponse = CorrectionResponse(finding.Id);
        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, correctionClaim.AttemptId, expectTerminal: true);
        var correctionStdin = StdinOf(host.Process.Requests.Last());
        Assert.DoesNotContain(revisedSteps, correctionStdin, StringComparison.Ordinal);
        Assert.DoesNotContain(RootPlanSteps, correctionStdin, StringComparison.Ordinal);
        Assert.Equal(
            [report.Id, finding.Id],
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == correctionClaim.AttemptId)
                .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));
        var report2 = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == correctionClaim.AttemptId && m.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(lineage.RootId, report2.InReplyToMessageId);
        var response = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == correctionClaim.AttemptId && m.Type == CollaborationMessageType.RevisionResponse));
        Assert.Equal(finding.Id, response.InReplyToMessageId);

        await SeedPassedVerificationAsync(host, lineage, executionNumber: 2);
        var reReview1 = await ReviewAsync(host, lineage, report2.Id, ChangesRequestedJson(), AgentOutcome.ReviewChangesRequested);
        await AssertReviewedPlanAsync(host, reReview1, lineage.FinalId, revisedSteps, superseded);
        var finding2 = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == reReview1 && m.Type == CollaborationMessageType.ReviewFinding));

        // A second correction link (within the ordinary correction budget): the plan identity and the replies are unchanged.
        var second = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(
            (await SendAsync(host, new CreateReviewCorrectionAttemptCommand(lineage.RunId, reReview1))).Value);
        host.Process.FinalResponse = CorrectionResponse(finding2.Id);
        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, second.AttemptId, expectTerminal: true);
        var report3 = await InDbAsync(host, db => db.CollaborationMessages.AsNoTracking().SingleAsync(
            m => m.AttemptId == second.AttemptId && m.Type == CollaborationMessageType.ExecutionReport));
        Assert.Equal(lineage.RootId, report3.InReplyToMessageId);
        Assert.Equal(
            [report2.Id, finding2.Id],
            await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == second.AttemptId)
                .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync()));

        await SeedPassedVerificationAsync(host, lineage, executionNumber: 3);
        var invalidReReview = await ReviewAsync(host, lineage, report3.Id, "not a review", AgentOutcome.InvalidStructuredOutput);
        var reReviewRepair = await ReviewAsync(host, lineage, report3.Id, ApprovedJson(), AgentOutcome.ReviewApproved, repairOf: invalidReReview);
        await AssertReviewedPlanAsync(host, invalidReReview, lineage.FinalId, revisedSteps, superseded);
        await AssertReviewedPlanAsync(host, reReviewRepair, lineage.FinalId, revisedSteps, superseded);
        var repairManifest = JsonSerializer.Deserialize<JsonElement>(host.Review.ReceivedManifests.Single(m => m.AttemptId == reReviewRepair).Text);
        Assert.True(repairManifest.TryGetProperty("formatRepairNotice", out _));
        Assert.Equal(report2.Id, repairManifest.GetProperty("correctionEvidence").GetProperty("previousExecutionReport").GetProperty("messageId").GetGuid());
        Assert.Equal(5, host.Review.InvocationCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task An_accepted_root_and_an_accepted_first_revision_are_reviewed_as_the_exact_plan_they_implemented(bool revised, bool accepted)
    {
        await using var host = BuildHost();
        var lineage = await SeedOrdinaryAsync(host, revised, accepted);
        var (implementationId, report) = await ImplementAsync(host, lineage);
        Assert.Equal(lineage.FinalId, report.InReplyToMessageId);
        var inputs = await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == implementationId)
            .OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync());
        Assert.Equal(lineage.FinalId, inputs[0]);
        Assert.Equal(revised ? lineage.SecondDecisionIds.Count + 2 : 2, inputs.Count);
        await SeedPassedVerificationAsync(host, lineage, executionNumber: 1);

        var review = await ReviewAsync(host, lineage, report.Id, ApprovedJson(), AgentOutcome.ReviewApproved);

        if (revised)
        {
            await AssertReviewedPlanAsync(host, review, lineage.FinalId, "Steps unique to [First revised scope]", RootPlanSummary, RootPlanSteps);
        }
        else
        {
            await AssertReviewedPlanAsync(host, review, lineage.RootId, RootPlanSteps);
        }
    }

    [Fact]
    public async Task An_older_root_target_sealed_review_replays_its_exact_bytes_after_a_restart()
    {
        Lineage lineage;
        Guid reviewAttemptId;
        string oldManifestText;
        await using (var first = BuildHost())
        {
            lineage = await SeedOrdinaryAsync(first, revised: true, acceptPlan: false);
            var (_, report) = await ImplementAsync(first, lineage);
            await SeedPassedVerificationAsync(first, lineage, executionNumber: 1);
            var claim = await SendAsync(first, new CreateCodeReviewAttemptCommand(lineage.RunId, report.Id));
            Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
            reviewAttemptId = claim.Value.AttemptId;

            // Fabricate what a review claimed before ADR-0017 sealed: the same manifest naming the root as its plan.
            var root = await InDbAsync(first, db => db.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == lineage.RootId));
            var artifact = await InDbAsync(first, db => db.Artifacts.AsNoTracking().SingleAsync(
                a => a.AttemptId == reviewAttemptId && a.Purpose == ArtifactPurpose.AgentContextManifest));
            var manifest = System.Text.Json.Nodes.JsonNode.Parse((await ReadManifestAsync(first, reviewAttemptId)).GetRawText())!.AsObject();
            manifest["resolvedPlan"] = new System.Text.Json.Nodes.JsonObject
            {
                ["messageId"] = root.Id,
                ["summary"] = root.Summary,
                ["structuredContent"] = System.Text.Json.Nodes.JsonNode.Parse(root.StructuredContentJson),
            };
            oldManifestText = manifest.ToJsonString();
            var bytes = Encoding.UTF8.GetBytes(oldManifestText);
            var sealedPath = Path.Combine(_artifactRoot, artifact.RelativeStoragePath);
            File.SetAttributes(sealedPath, FileAttributes.Normal);
            await File.WriteAllBytesAsync(sealedPath, bytes);
            var hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            await using var scope = first.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await db.Artifacts.Where(a => a.Id == artifact.Id).ExecuteUpdateAsync(set => set
                .SetProperty(a => a.ContentHash, hash).SetProperty(a => a.ByteLength, (long)bytes.Length));
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        await using var restarted = BuildHost();
        restarted.Evidence.Stage = 1;
        restarted.Review.FinalResponseJson = ApprovedJson();

        await RunSupervisorAsync(ReviewSupervisorFor(restarted), restarted, reviewAttemptId, expectTerminal: true);

        var received = Assert.Single(restarted.Review.ReceivedManifests);
        Assert.Equal(reviewAttemptId, received.AttemptId);
        Assert.Equal(oldManifestText, received.Text);
        Assert.Equal(lineage.RootId, JsonSerializer.Deserialize<JsonElement>(received.Text).GetProperty("resolvedPlan").GetProperty("messageId").GetGuid());
        Assert.Equal(AgentOutcome.ReviewApproved, await InDbAsync(restarted, db => db.Attempts.Where(a => a.Id == reviewAttemptId).Select(a => a.AgentOutcome).SingleAsync()));
        Assert.Equal(1, await InDbAsync(restarted, db => db.Attempts.CountAsync(a => a.AgentRole == AgentRole.CodeReviewer)));
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

    // ---- replay, tampering, and the unspent-grant guarantees -----------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_undispatched_authorized_claim_replays_its_sealed_context_after_a_restart_without_a_second_grant_or_consent(
        bool historicalEscalation)
    {
        Lineage lineage;
        Guid attemptId;
        AuthorizePlanningImplementationCommandResult authorization;
        string sealedBefore;
        await using (var first = BuildHost())
        {
            lineage = await SeedEscalatedLineageAsync(first, historicalEscalation: historicalEscalation);
            authorization = await AuthorizeAsync(first, lineage);
            attemptId = await ClaimAsync(first, lineage);
            sealedBefore = (await ReadManifestAsync(first, attemptId)).GetRawText();
            // Later requests cannot alter the claimed attempt or spend another grant.
            var later = await SendAsync(first, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));
            Assert.True(later.IsFailure);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        await using var restarted = BuildHost();
        restarted.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);

        await RunSupervisorAsync(ImplementationSupervisorFor(restarted), restarted, attemptId, expectTerminal: true);

        var request = Assert.Single(restarted.Process.Requests);
        Assert.Equal(sealedBefore, JsonSerializer.Deserialize<JsonElement>(StdinOf(request)).GetRawText());
        Assert.Equal(AgentOutcome.Implemented, await InDbAsync(restarted, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.AgentOutcome).SingleAsync()));
        var grant = await InDbAsync(restarted, db => db.PlanningImplementationAuthorizations.AsNoTracking().SingleAsync());
        Assert.Equal(authorization.AuthorizationId, grant.Id);
        Assert.Equal(attemptId, grant.ConsumedByAttemptId);
        Assert.Equal(1, await InDbAsync(restarted, db => db.Attempts.CountAsync(a => a.AgentRole == AgentRole.Implementer)));
    }

    public static IEnumerable<object[]> Tampers() =>
    [
        ["instruction-content"],
        ["wrong-consumption-owner"],
        ["missing-input"],
        ["grant-unconsumed"],
    ];

    [Theory]
    [MemberData(nameof(Tampers))]
    public async Task A_tampered_grant_or_input_set_never_starts_a_provider_process(string tamper)
    {
        await using var host = BuildHost();
        var lineage = await SeedEscalatedLineageAsync(host);
        var authorization = await AuthorizeAsync(host, lineage);
        var attemptId = await ClaimAsync(host, lineage);
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            switch (tamper)
            {
                case "instruction-content":
                    await db.CollaborationMessages.Where(m => m.Id == authorization.HumanInstructionMessageId)
                        .ExecuteUpdateAsync(set => set.SetProperty(m => m.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"y\"}"));
                    break;
                case "wrong-consumption-owner":
                    var otherAttemptId = await db.Attempts.Where(a => a.Id != attemptId).Select(a => a.Id).FirstAsync();
                    await db.PlanningImplementationAuthorizations
                        .ExecuteUpdateAsync(set => set.SetProperty(g => g.ConsumedByAttemptId, (Guid?)otherAttemptId));
                    break;
                case "missing-input":
                    await db.AttemptInputMessages.Where(m => m.AttemptId == attemptId && m.CollaborationMessageId == authorization.HumanInstructionMessageId).ExecuteDeleteAsync();
                    break;
                default:
                    await db.PlanningImplementationAuthorizations
                        .ExecuteUpdateAsync(set => set.SetProperty(g => g.ConsumedByAttemptId, (Guid?)null).SetProperty(g => g.ConsumedAtUtc, (DateTimeOffset?)null));
                    break;
            }
        }

        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: false);

        Assert.Empty(host.Process.Requests);
        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.NotEqual(AgentOutcome.Implemented, attempt.AgentOutcome);
    }

    [Fact]
    public async Task A_failed_authorized_attempt_keeps_the_grant_spent_and_a_new_request_cannot_reuse_it()
    {
        await using var host = BuildHost();
        var lineage = await SeedEscalatedLineageAsync(host);
        await AuthorizeAsync(host, lineage);
        var attemptId = await ClaimAsync(host, lineage);
        host.Process.FinalResponse = "{ not a report";

        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: true);

        Assert.Single(host.Process.Requests);
        Assert.Equal(AttemptStatus.Failed, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.Status).SingleAsync()));
        // A failed implementation that may have touched the worktree flags the workspace; once an operator makes it ready again,
        // the spent grant is still spent: a second request cannot reuse it.
        await InDbAsync(host, db => db.GitWorkspaces.ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WorkspaceStatus.Ready)));
        var again = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));
        Assert.Equal(PlanningImplementationAuthorizationErrors.ClaimConsumedCode, Assert.Single(again.Errors).Code);
        Assert.Equal(attemptId, await InDbAsync(host, db => db.PlanningImplementationAuthorizations.Select(g => g.ConsumedByAttemptId).SingleAsync()));
    }

    [Fact]
    public async Task The_ordinary_run_budget_still_applies_and_a_refusal_leaves_the_grant_unconsumed()
    {
        await using var host = BuildHost();
        var lineage = await SeedEscalatedLineageAsync(host, maximumAgentAttempts: 5);
        await AuthorizeAsync(host, lineage);

        var claim = await SendAsync(host, new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalId));

        Assert.Equal("agent_attempts.budget_exhausted", Assert.Single(claim.Errors).Code);
        Assert.Null(await InDbAsync(host, db => db.PlanningImplementationAuthorizations.Select(g => g.ConsumedByAttemptId).SingleAsync()));
        Assert.Empty(host.Process.Requests);
    }

    [Fact]
    public async Task A_claim_reserves_one_ordinary_budget_slot_and_the_authorization_itself_reserves_none()
    {
        await using var host = BuildHost();
        var lineage = await SeedEscalatedLineageAsync(host);
        var usedBefore = await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId && a.Kind == AttemptKind.Agent));
        await AuthorizeAsync(host, lineage);
        Assert.Equal(usedBefore, await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId && a.Kind == AttemptKind.Agent)));

        var attemptId = await ClaimAsync(host, lineage);

        Assert.Equal(usedBefore + 1, await InDbAsync(host, db => db.Attempts.CountAsync(a => a.RunId == lineage.RunId && a.Kind == AttemptKind.Agent)));
        Assert.Equal(usedBefore + 1, await InDbAsync(host, db => db.Attempts.Where(a => a.Id == attemptId).Select(a => a.AgentBudgetSlot!.Value).SingleAsync()));
    }
}
