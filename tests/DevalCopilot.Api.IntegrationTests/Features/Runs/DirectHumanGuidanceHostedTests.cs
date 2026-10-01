using System.Text;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
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
/// Direct human guidance end to end through the real supervisors, the real mediator and EF pipeline, and the REAL Claude
/// adapters. Only the process boundary is a deterministic double that records the actual <c>StandardInput</c> and
/// arguments (no provider is ever started), plus a Git evidence reader that reports a mutation once the double ran.
/// Proves exact accepted text once on stdin, unchanged source identities and tool arguments, validated results,
/// sealed replay of an undispatched claim after a restart, historical unguided and authorized controls, and that a
/// snapshot that disagrees with the sealed manifest never starts a process.
/// </summary>
public sealed class DirectHumanGuidanceHostedTests : IDisposable
{
    private const string Guidance = "SENTINEL-HOSTED-4 Prefer the existing helper.\nKeep the change small.";
    private const string AuthorizedGuidance = "SENTINEL-AUTH-8 Authorized advice only.";

    private static readonly string Head = new('a', 40);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string ChangedFingerprint = new('c', 64);

    private static readonly string ProposalContent = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static readonly string AcceptanceResponse = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-hosted-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-hosted-artifacts-{Guid.NewGuid():N}");
    private readonly string _toolRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-hosted-tool-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _claudeExecutable;

    public DirectHumanGuidanceHostedTests()
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

    private sealed class MutatingEvidence : IGitWorkspaceEvidenceReader
    {
        public bool Mutated { get; set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(Mutated
                ? new GitWorkspaceEvidenceResult(
                    GitWorkspaceEvidenceOutcome.Success, Head, ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")], "diff")
                : new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, Head, Fingerprint, [], null));
    }

    private sealed class RecordingProcessDouble(MutatingEvidence evidence) : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public string FinalResponse { get; set; } = "{}";

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            evidence.Mutated = true;
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

    private sealed record Host(ServiceProvider Provider, MutatingEvidence Evidence, RecordingProcessDouble Process) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private Host BuildHost()
    {
        var evidence = new MutatingEvidence();
        var process = new RecordingProcessDouble(evidence);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddDbContextFactory<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<IProcessExecutionAdapter>(process);
        services.AddSingleton<IClaudeImplementationAdapter>(sp => new ClaudeImplementationAdapter(process, _artifactStore));
        services.AddSingleton<IClaudeReviewCorrectionAdapter>(sp => new ClaudeReviewCorrectionAdapter(process, _artifactStore));
        services.AddSingleton<IRunEventNotifier>(new NoopNotifier());
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return new Host(services.BuildServiceProvider(), evidence, process);
    }

    private static ImplementationSupervisor ImplementationSupervisorFor(Host host) => new(
        host.Provider.GetRequiredService<IServiceScopeFactory>(),
        host.Provider.GetRequiredService<IClaudeImplementationAdapter>(),
        host.Evidence,
        host.Provider.GetRequiredService<IArtifactStore>(),
        NullLogger<ImplementationSupervisor>.Instance);

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

    private static string ImplementationReport(IReadOnlyList<string> changed) => JsonSerializer.Serialize(new
    {
        summary = "Implemented the ledger table and its query.",
        changedRelativePaths = changed,
        implementationNotes = "Added the migration and the query handler.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the backend test suite.",
    });

    private static string StdinOf(ProcessExecutionRequest request) => Encoding.UTF8.GetString(request.StandardInput!);

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    // ---- implementation seeding (production command chain) ------------------------------------------------------

    private async Task<(Guid RunId, Guid AttemptId, Guid ProposalId, Guid AcceptanceId)> SeedImplementationAsync(
        Host host, string? guidance, bool claim = true)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the ledger table and its query", now);
        dbContext.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-ws-{Guid.NewGuid():N}"),
            "branch", Head, "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprint, []));
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\fake\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(codex);
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
        claude.MarkDispatched(now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, _claudeExecutable, null, "2.1.276", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claude);
        run.Claim(now);
        await dbContext.SaveChangesAsync();

        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ProposalContent), null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var proposal = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var review = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposal.Id), CancellationToken.None);
        Assert.True(review.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, review.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var parsed = ClaudeCriticalReviewResponseParser.TryParse(AcceptanceResponse);
        Assert.NotNull(parsed);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(run.Id, review.Value.AttemptId, AgentOutcome.Accepted, Fingerprint, [], parsed, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var acceptance = await dbContext.CollaborationMessages.SingleAsync(
            message => message.AttemptId == review.Value.AttemptId && message.Type == CollaborationMessageType.Acceptance);

        if (!claim)
        {
            return (run.Id, Guid.Empty, proposal.Id, acceptance.Id);
        }

        var implementation = await mediator.SendAsync(new CreateImplementationAttemptCommand(run.Id, proposal.Id, guidance), CancellationToken.None);
        Assert.True(implementation.IsSuccess, string.Join(",", implementation.Errors.Select(error => error.Code)));
        return (run.Id, implementation.Value.AttemptId, proposal.Id, acceptance.Id);
    }

    // ---- correction seeding ------------------------------------------------------------------------------------

    private sealed record CorrectionSeed(Guid RunId, Guid ReviewId, Guid FindingId, Guid ReportId, Guid WorkspaceId, Guid CheckpointId);

    private async Task<CorrectionSeed> SeedCorrectionLineageAsync(Host host, int failedCorrections = 0)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Hosted correction", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", now, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        run.Claim(now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-ws-{Guid.NewGuid():N}"), "branch", Head, "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), now);
        var capability = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
        capability.MarkDispatched(now);
        capability.RecordSuccess(CapabilityLaunchKind.DirectExecutable, _claudeExecutable, null, "1.0", now, now.AddMinutes(5));
        var planning = Attempt.ClaimAgent(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 1);
        planning.MarkAgentDispatched(now);
        planning.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planning.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the correction.",
            JsonSerializer.Serialize(new { scope = "Correction", implementationSteps = "Apply findings.", risks = "None.", verificationPlan = "Run tests.", escalationPoints = "None." }),
            CollaborationMessageProvenance.ProviderObserved, now);
        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 2);
        acceptanceAttempt.MarkAgentDispatched(now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the implementation plan.",
            JsonSerializer.Serialize(new { rationale = "The plan is complete." }), CollaborationMessageProvenance.ProviderObserved, now);
        var implementation = Attempt.ClaimAgentImplementation(Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 3);
        implementation.MarkAgentDispatched(now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, checkpoint.Id, now, processEvidence: TestProcessEvidence.CleanExit);
        var report = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, implementation.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.ExecutionReport, proposal.Id, "Implementation complete.",
            JsonSerializer.Serialize(new { completedWork = "Applied the implementation.", verification = "Tests passed." }),
            CollaborationMessageProvenance.ProviderObserved, now);
        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 4, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 4);
        review.MarkAgentDispatched(now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var finding = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ReviewFinding, report.Id, "The branch needs correction.",
            JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The branch is incomplete.", requiredChange = "Complete the branch." }),
            CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(1));
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(lease);
        db.HostCapabilitySnapshots.Add(capability);
        db.Attempts.AddRange(planning, acceptanceAttempt, implementation, review);
        db.CollaborationMessages.AddRange(proposal, acceptance, report, finding);
        db.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0),
            AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1),
            AttemptInputMessage.Record(Guid.NewGuid(), review.Id, report.Id, 0));
        for (var number = 5; number < 5 + failedCorrections; number++)
        {
            var prior = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), run.Id, number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, number);
            prior.MarkAgentDispatched(now);
            prior.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now);
            db.Attempts.Add(prior);
        }

        await db.SaveChangesAsync();
        return new CorrectionSeed(run.Id, review.Id, finding.Id, report.Id, workspace.Id, checkpoint.Id);
    }

    private static string CorrectionResponse(Guid findingId) => JsonSerializer.Serialize(new
    {
        revisionResponses = new[]
        {
            new { findingMessageId = findingId, disposition = "Fixed", evidence = "The incomplete branch was corrected.", resultingSourceChanges = "Completed the branch." },
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

    private async Task<Guid> ClaimCorrectionAsync(Host host, CorrectionSeed seed, string? guidance)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId, guidance), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code)));
        return Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value).AttemptId;
    }

    private async Task<T> InDbAsync<T>(Host host, Func<DevalCopilotDbContext, Task<T>> read)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private async Task ExecuteSqlAsync(Host host, FormattableString sql) =>
        await InDbAsync(host, async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    // ---- implementation ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_guided_implementation_sends_the_exact_accepted_text_once_with_unchanged_identities_and_records_the_validated_result()
    {
        await using var host = BuildHost();
        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        var (runId, attemptId, proposalId, acceptanceId) = await SeedImplementationAsync(host, "  " + Guidance.Replace("\n", "\r\n", StringComparison.Ordinal) + "  ");

        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: true);

        var request = Assert.Single(host.Process.Requests);
        var stdin = StdinOf(request);
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-HOSTED-4"));
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, Guidance));
        Assert.Contains("--tools", request.Arguments);
        Assert.Equal("Read,Edit,Write,Glob,Grep", request.Arguments[request.Arguments.ToList().IndexOf("--tools") + 1]);
        Assert.Equal("acceptEdits", request.Arguments[request.Arguments.ToList().IndexOf("--permission-mode") + 1]);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("SENTINEL", StringComparison.Ordinal));
        Assert.DoesNotContain(request.EnvironmentVariables.Values, value => value.Contains("SENTINEL", StringComparison.Ordinal));

        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Implemented, attempt.AgentOutcome);
        Assert.Equal(Guidance, attempt.ReadAgentDirectHumanGuidance().Text);
        Assert.Equal(request.WorkingDirectory, await InDbAsync(host, db => db.GitWorkspaces.Where(w => w.Id == attempt.AgentGitWorkspaceId).Select(w => w.WorkspacePath).SingleAsync()));
        var inputs = await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == attemptId).OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync());
        Assert.Equal([proposalId, acceptanceId], inputs);
        Assert.Equal(1, await InDbAsync(host, db => db.CollaborationMessages.CountAsync(m => m.AttemptId == attemptId && m.Type == CollaborationMessageType.ExecutionReport)));
        _ = runId;
    }

    [Fact]
    public async Task A_historical_style_unguided_implementation_sends_no_guidance_member_and_completes()
    {
        await using var host = BuildHost();
        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        var (_, attemptId, _, _) = await SeedImplementationAsync(host, null);

        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: true);

        var request = Assert.Single(host.Process.Requests);
        Assert.True(DirectHumanGuidanceManifest.Agrees(StdinOf(request), null));
        Assert.Equal(AttemptStatus.Completed, (await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId))).Status);
    }

    [Fact]
    public async Task An_undispatched_guided_implementation_replays_its_sealed_context_after_a_restart_despite_later_requests()
    {
        Guid attemptId;
        Guid runId;
        await using (var first = BuildHost())
        {
            (runId, attemptId, _, _) = await SeedImplementationAsync(first, Guidance);
            // A later request and later run-level changes cannot alter the claimed attempt.
            await using var scope = first.Provider.CreateAsyncScope();
            var later = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new CreateImplementationAttemptCommand(runId, Guid.NewGuid(), "SENTINEL-LATER another text"), CancellationToken.None);
            Assert.True(later.IsFailure);
            await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE runs SET RequestedClaudeModel = 'opus' WHERE Id = {runId}");
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        await using var restarted = BuildHost();
        restarted.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);

        await RunSupervisorAsync(ImplementationSupervisorFor(restarted), restarted, attemptId, expectTerminal: true);

        var request = Assert.Single(restarted.Process.Requests);
        var stdin = StdinOf(request);
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-HOSTED-4"));
        Assert.DoesNotContain("SENTINEL-LATER", stdin, StringComparison.Ordinal);
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, Guidance));
        Assert.Equal(Guidance, (await InDbAsync(restarted, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId))).ReadAgentDirectHumanGuidance().Text);
    }

    public static IEnumerable<object?[]> ImplementationDisagreements() =>
    [
        ["a different valid text", "Different valid advisory text.", true, true],
        ["cleared while the sealed context is guided", null, true, true],
        ["a snapshot written beside an unguided sealed context", "Snapshot only advisory text.", false, true],
        ["malformed text beside a guided sealed context", " padded malformed ", true, false],
    ];

    [Theory]
    [MemberData(nameof(ImplementationDisagreements))]
    public async Task A_snapshot_that_disagrees_with_the_sealed_manifest_never_starts_a_provider_process_for_an_implementation(
        string scenario, string? storedSnapshot, bool guidedClaim, bool reachesDispatch)
    {
        _ = scenario;
        await using var host = BuildHost();
        host.Process.FinalResponse = ImplementationReport(["src/Foo.cs"]);
        var (_, attemptId, _, _) = await SeedImplementationAsync(host, guidedClaim ? Guidance : null);
        await ExecuteSqlAsync(host, $"UPDATE attempts SET AgentDirectHumanGuidance = {storedSnapshot} WHERE Id = {attemptId}");

        await RunSupervisorAsync(ImplementationSupervisorFor(host), host, attemptId, expectTerminal: reachesDispatch);

        Assert.Empty(host.Process.Requests);
        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.NotEqual(AgentOutcome.Implemented, attempt.AgentOutcome);
        if (reachesDispatch)
        {
            Assert.Equal(AttemptStatus.Failed, attempt.Status);
        }
        else
        {
            Assert.Null(attempt.AgentDispatchedAtUtc);
            Assert.Equal(AttemptStatus.Running, attempt.Status);
        }
    }

    // ---- review correction -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_guided_correction_sends_the_exact_accepted_text_once_with_unchanged_identities_and_records_the_validated_result()
    {
        await using var host = BuildHost();
        var seed = await SeedCorrectionLineageAsync(host);
        host.Process.FinalResponse = CorrectionResponse(seed.FindingId);
        var attemptId = await ClaimCorrectionAsync(host, seed, "  " + Guidance.Replace("\n", "\r\n", StringComparison.Ordinal) + "  ");

        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, attemptId, expectTerminal: true);

        var request = Assert.Single(host.Process.Requests);
        var stdin = StdinOf(request);
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-HOSTED-4"));
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, Guidance));
        Assert.False(JsonSerializer.Deserialize<JsonElement>(stdin).TryGetProperty("humanGuidance", out _));
        Assert.Equal("Read,Edit,Write,Glob,Grep", request.Arguments[request.Arguments.ToList().IndexOf("--tools") + 1]);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("SENTINEL", StringComparison.Ordinal));

        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(Guidance, attempt.ReadAgentDirectHumanGuidance().Text);
        var inputs = await InDbAsync(host, db => db.AttemptInputMessages.Where(m => m.AttemptId == attemptId).OrderBy(m => m.Sequence).Select(m => m.CollaborationMessageId).ToListAsync());
        Assert.Equal([seed.ReportId, seed.FindingId], inputs);
        Assert.Equal(1, await InDbAsync(host, db => db.CollaborationMessages.CountAsync(m => m.AttemptId == attemptId && m.Type == CollaborationMessageType.RevisionResponse)));
    }

    [Fact]
    public async Task An_undispatched_guided_correction_replays_its_sealed_context_after_a_restart_despite_later_requests()
    {
        CorrectionSeed seed;
        Guid attemptId;
        await using (var first = BuildHost())
        {
            seed = await SeedCorrectionLineageAsync(first);
            attemptId = await ClaimCorrectionAsync(first, seed, Guidance);
            await using var scope = first.Provider.CreateAsyncScope();
            var later = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
                .SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId, "SENTINEL-LATER another text"), CancellationToken.None);
            Assert.True(later.IsFailure);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        await using var restarted = BuildHost();
        restarted.Process.FinalResponse = CorrectionResponse(seed.FindingId);

        await RunSupervisorAsync(CorrectionSupervisorFor(restarted), restarted, attemptId, expectTerminal: true);

        var request = Assert.Single(restarted.Process.Requests);
        var stdin = StdinOf(request);
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-HOSTED-4"));
        Assert.DoesNotContain("SENTINEL-LATER", stdin, StringComparison.Ordinal);
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, Guidance));
    }

    [Theory]
    [MemberData(nameof(ImplementationDisagreements))]
    public async Task A_snapshot_that_disagrees_with_the_sealed_manifest_never_starts_a_provider_process_for_a_correction(
        string scenario, string? storedSnapshot, bool guidedClaim, bool reachesDispatch)
    {
        _ = scenario;
        await using var host = BuildHost();
        var seed = await SeedCorrectionLineageAsync(host);
        host.Process.FinalResponse = CorrectionResponse(seed.FindingId);
        var attemptId = await ClaimCorrectionAsync(host, seed, guidedClaim ? Guidance : null);
        await ExecuteSqlAsync(host, $"UPDATE attempts SET AgentDirectHumanGuidance = {storedSnapshot} WHERE Id = {attemptId}");

        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, attemptId, expectTerminal: reachesDispatch);

        Assert.Empty(host.Process.Requests);
        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.NotEqual(AgentOutcome.CorrectionApplied, attempt.AgentOutcome);
        if (reachesDispatch)
        {
            Assert.Equal(AttemptStatus.Failed, attempt.Status);
        }
        else
        {
            Assert.Null(attempt.AgentDispatchedAtUtc);
            Assert.Equal(AttemptStatus.Running, attempt.Status);
        }
    }

    [Fact]
    public async Task A_historical_style_authorized_correction_keeps_its_own_human_guidance_and_sends_no_direct_member()
    {
        await using var host = BuildHost();
        var seed = await SeedCorrectionLineageAsync(host, failedCorrections: 2);
        host.Process.FinalResponse = CorrectionResponse(seed.FindingId);
        Guid escalationId;
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            // A guided request at exhaustion is refused whole, with nothing created or consumed.
            var refused = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId, Guidance), CancellationToken.None);
            Assert.Equal("agent_attempts.direct_guidance_unavailable", Assert.Single(refused.Errors).Code);
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().ReviewCorrectionEscalations.CountAsync());

            var escalated = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId), CancellationToken.None);
            escalationId = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(escalated.Value).EscalationId;
            var authorized = await mediator.SendAsync(new AuthorizeReviewCorrectionCommand(seed.RunId, escalationId, AuthorizedGuidance), CancellationToken.None);
            Assert.True(authorized.IsSuccess);
            // With an available authorization a guided request is still refused and consumes nothing.
            var refusedAgain = await mediator.SendAsync(new CreateReviewCorrectionAttemptCommand(seed.RunId, seed.ReviewId, Guidance), CancellationToken.None);
            Assert.Equal("agent_attempts.direct_guidance_unavailable", Assert.Single(refusedAgain.Errors).Code);
            Assert.Null((await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().ReviewCorrectionAuthorizations.AsNoTracking().SingleAsync()).ConsumedByAttemptId);
        }

        var attemptId = await ClaimCorrectionAsync(host, seed, null);

        await RunSupervisorAsync(CorrectionSupervisorFor(host), host, attemptId, expectTerminal: true);

        var stdin = StdinOf(Assert.Single(host.Process.Requests));
        var root = JsonSerializer.Deserialize<JsonElement>(stdin);
        Assert.Equal(AuthorizedGuidance, root.GetProperty("humanGuidance").GetProperty("text").GetString());
        Assert.True(DirectHumanGuidanceManifest.Agrees(stdin, null));
        Assert.Equal(1, Occurrences(stdin, "SENTINEL-AUTH-8"));
        var attempt = await InDbAsync(host, db => db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId));
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Null(await InDbAsync(host, db => db.Database.SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}").SingleAsync()));
    }
}
