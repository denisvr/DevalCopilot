using System.Text;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.RealAdapterInstructionProbe;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The sealed instruction context through the real production claim, the real artifact store, the real planning
/// supervisor and the REAL Codex planning adapter over a recording process double (no provider is started): two
/// unrelated projects never see each other's conventions, a claim sealed before the root files changed replays its exact
/// bytes after a restart (and a historical claim sealed before this contract existed replays its own bytes too, neither
/// rebuilt nor rejected), and a later fresh claim captures the new eligible bytes.
/// </summary>
public sealed class ProjectInstructionContextReplayHostedTests : IDisposable
{
    private const string AlphaV1 = "ALPHA-CONVENTIONS-V1: handlers end with Handler.\r\n";
    private const string AlphaV2 = "ALPHA-CONVENTIONS-V2: handlers end with Endpoint.\r\n";
    private const string BetaV1 = "BETA-CONVENTIONS-V1: prefer small modules.\r\n";
    private const string InvalidProposal = "{ this is not a valid proposal";

    private static readonly string Head = new('a', 40);
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-instruction-replay-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-instruction-replay-artifacts-{Guid.NewGuid():N}");
    private readonly string _toolRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-instruction-replay-tool-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _codexExecutable;

    public ProjectInstructionContextReplayHostedTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_toolRoot);
        _codexExecutable = Path.Combine(_toolRoot, "codex.exe");
        File.WriteAllText(_codexExecutable, string.Empty);
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

    /// <summary>What each workspace's root instruction files currently are, observed at every capture.</summary>
    private sealed class PerWorkspaceEvidence : IGitWorkspaceEvidenceReader
    {
        public Dictionary<string, GitWorkspaceInstructionContext?> Instructions { get; } = new(StringComparer.Ordinal);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, Head, Fingerprint, [], null, null,
                Instructions.GetValueOrDefault(workspacePath)));
    }

    private ServiceProvider BuildProvider(PerWorkspaceEvidence evidence, CapturingProcessDouble process)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        AccountUsageGuardTestServices.Register(services);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<ICodexPlanningAdapter>(new CodexPlanningAdapter(process, _artifactStore));
        services.AddDevalenteMediator(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateCodexPlanningAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed record Seeded(Guid ProjectId, Guid RunId, Guid WorkspaceId, string WorkspacePath);

    private async Task MigrateAndObserveCodexAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, _codexExecutable, null, "1.2.3", now, now.AddMinutes(5));
        db.HostCapabilitySnapshots.Add(codex);
        await db.SaveChangesAsync();
    }

    private static async Task<Seeded> SeedProjectAsync(ServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), name, $@"C:\repos\{Guid.NewGuid():N}", now);
        db.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), $"Plan {name}", now);
        db.Runs.Add(run);
        var path = Path.Combine(Path.GetTempPath(), $"devalcopilot-instruction-replay-ws-{name}-{Guid.NewGuid():N}");
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, path, "branch", Head, "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, Head, Fingerprint, []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), now));
        await db.SaveChangesAsync();
        return new Seeded(project.Id, run.Id, workspace.Id, path);
    }

    private static async Task<Guid> ClaimPlanningAsync(ServiceProvider provider, Guid runId, Guid? repairSource = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var claim = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(
            repairSource is { } source ? new CreateCodexPlanningAttemptCommand(runId, source) : new CreateCodexPlanningAttemptCommand(runId),
            CancellationToken.None);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return claim.Value.AttemptId;
    }

    private static async Task<(Attempt Attempt, Artifact Manifest)> LoadAsync(ServiceProvider provider, Guid attemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        var manifest = await db.Artifacts.AsNoTracking().SingleAsync(
            candidate => candidate.AttemptId == attemptId && candidate.Purpose == ArtifactPurpose.AgentContextManifest);
        return (attempt, manifest);
    }

    private async Task RunSupervisorToTerminalAsync(ServiceProvider provider, Guid attemptId)
    {
        var supervisor = new AgentAttemptSupervisor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICodexPlanningAdapter>(),
            provider.GetRequiredService<IGitWorkspaceEvidenceReader>(),
            provider.GetRequiredService<IArtifactStore>(),
            NullLogger<AgentAttemptSupervisor>.Instance);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
                await using var scope = provider.CreateAsyncScope();
                var status = await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts
                    .Where(a => a.Id == attemptId).Select(a => a.Status).SingleAsync();
                if (status != AttemptStatus.Running)
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

    private static CapturingProcessDouble ProcessWritingTheFinalResponse(string response)
    {
        var process = new CapturingProcessDouble();
        process.OnExecute = request =>
        {
            var arguments = request.Arguments.ToList();
            File.WriteAllText(arguments[arguments.IndexOf("--output-last-message") + 1], response);
        };
        return process;
    }

    private async Task<string> SealedTextAsync(Artifact manifest)
    {
        var window = await _artifactStore.VerifyAndReadSealedAsync(
            manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash, 0, 64 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, window.Status);
        return window.Text;
    }

    private static string AgentsSource(string manifestText)
    {
        using var document = JsonDocument.Parse(manifestText);
        return document.RootElement.GetProperty("projectInstructionContext").GetProperty("sources")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task Two_unrelated_projects_each_reach_the_real_adapter_with_only_their_own_conventions()
    {
        var evidence = new PerWorkspaceEvidence();
        await using var provider = BuildProvider(evidence, new CapturingProcessDouble());
        await MigrateAndObserveCodexAsync(provider);
        var alpha = await SeedProjectAsync(provider, "alpha");
        var beta = await SeedProjectAsync(provider, "beta");
        evidence.Instructions[alpha.WorkspacePath] = OwnContext(AlphaV1);
        evidence.Instructions[beta.WorkspacePath] = OwnContext(BetaV1);

        var alphaClaim = await ClaimPlanningAsync(provider, alpha.RunId);
        var betaClaim = await ClaimPlanningAsync(provider, beta.RunId);

        var (alphaAttempt, alphaManifest) = await LoadAsync(provider, alphaClaim);
        var (betaAttempt, betaManifest) = await LoadAsync(provider, betaClaim);
        var toAlpha = await DeliverAsync(alphaAttempt, alphaManifest, alpha.WorkspacePath, _artifactStore, _codexExecutable);
        var toBeta = await DeliverAsync(betaAttempt, betaManifest, beta.WorkspacePath, _artifactStore, _codexExecutable);

        AssertDeliversOwnConventions(toAlpha, AlphaV1, "BETA-CONVENTIONS");
        AssertDeliversOwnConventions(toBeta, BetaV1, "ALPHA-CONVENTIONS");
        Assert.Equal(Guid.Parse(JsonDocument.Parse(toAlpha.Stdin).RootElement.GetProperty("projectInstructionContext").GetProperty("sourceGitWorkspaceId").GetString()!), alpha.WorkspaceId);
        Assert.Equal(Guid.Parse(JsonDocument.Parse(toBeta.Stdin).RootElement.GetProperty("projectInstructionContext").GetProperty("sourceGitWorkspaceId").GetString()!), beta.WorkspaceId);
    }

    [Fact]
    public async Task A_claim_sealed_before_the_root_files_changed_replays_its_exact_bytes_after_a_restart_and_a_later_fresh_claim_captures_the_new_bytes()
    {
        var evidence = new PerWorkspaceEvidence();
        Seeded alpha;
        Guid claimId;
        string sealedAtClaim;
        await using (var firstProvider = BuildProvider(evidence, new CapturingProcessDouble()))
        {
            await MigrateAndObserveCodexAsync(firstProvider);
            alpha = await SeedProjectAsync(firstProvider, "alpha");
            evidence.Instructions[alpha.WorkspacePath] = OwnContext(AlphaV1);
            claimId = await ClaimPlanningAsync(firstProvider, alpha.RunId);
            sealedAtClaim = await SealedTextAsync((await LoadAsync(firstProvider, claimId)).Manifest);
            Assert.Equal(AlphaV1, AgentsSource(sealedAtClaim));
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        // The root files change while the process is down; a restarted host dispatches the undispatched claim.
        evidence.Instructions[alpha.WorkspacePath] = OwnContext(AlphaV2);
        var process = ProcessWritingTheFinalResponse(InvalidProposal);
        await using var restarted = BuildProvider(evidence, process);
        await RunSupervisorToTerminalAsync(restarted, claimId);

        var request = Assert.Single(process.Requests);
        var stdin = Encoding.UTF8.GetString(request.StandardInput!);
        Assert.Equal(sealedAtClaim, stdin);
        Assert.Equal(AlphaV1, AgentsSource(stdin));
        Assert.DoesNotContain("ALPHA-CONVENTIONS-V2", stdin, StringComparison.Ordinal);
        var (dispatched, sealedManifest) = await LoadAsync(restarted, claimId);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, dispatched.AgentOutcome);
        Assert.Equal(sealedAtClaim, await SealedTextAsync(sealedManifest));

        // A later fresh claim (the one manual format repair) captures what the files are now.
        var repairId = await ClaimPlanningAsync(restarted, alpha.RunId, repairSource: claimId);
        var (repairAttempt, repairManifest) = await LoadAsync(restarted, repairId);
        var delivery = await DeliverAsync(repairAttempt, repairManifest, alpha.WorkspacePath, _artifactStore, _codexExecutable);
        AssertDeliversOwnConventions(delivery, AlphaV2, "ALPHA-CONVENTIONS-V1");
        Assert.Contains("formatRepairNotice", delivery.Stdin, StringComparison.Ordinal);
        // The earlier sealed bytes stayed exactly as sealed.
        Assert.Equal(sealedAtClaim, await SealedTextAsync(sealedManifest));
    }

    [Fact]
    public async Task A_historical_claim_sealed_before_this_contract_replays_its_own_bytes_neither_rebuilt_nor_rejected()
    {
        var evidence = new PerWorkspaceEvidence();
        var process = ProcessWritingTheFinalResponse(InvalidProposal);
        await using var provider = BuildProvider(evidence, process);
        await MigrateAndObserveCodexAsync(provider);
        var alpha = await SeedProjectAsync(provider, "alpha");
        // Today's files would yield a section; the historical claim must keep exactly what it sealed.
        evidence.Instructions[alpha.WorkspacePath] = OwnContext(AlphaV2);

        var attemptId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var legacy = LegacyPlanningManifest(alpha);
        var partial = _artifactStore.GetPartialPath(alpha.RunId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllTextAsync(partial, legacy);
        var sealedFile = await _artifactStore.SealAsync(alpha.RunId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var run = await db.Runs.SingleAsync(candidate => candidate.Id == alpha.RunId);
            run.Claim(now);
            var checkpoint = await db.GitCheckpoints.SingleAsync(candidate => candidate.WorkspaceId == alpha.WorkspaceId);
            db.Attempts.Add(Attempt.ClaimAgentWithAssignment(
                attemptId, alpha.RunId, 1, alpha.WorkspaceId, checkpoint.Id, Fingerprint, artifactId, TimeSpan.FromMinutes(10),
                256 * 1024, 512 * 1024, now, null, null, 1));
            db.Artifacts.Add(Artifact.Record(
                artifactId, alpha.RunId, attemptId, ArtifactPurpose.AgentContextManifest, "application/json",
                sealedFile!.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength, false, ArtifactCaptureOutcome.Captured,
                ArtifactSensitivity.HostConstructedContent, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
            await db.SaveChangesAsync();
        }

        await RunSupervisorToTerminalAsync(provider, attemptId);

        var request = Assert.Single(process.Requests);
        Assert.Equal(legacy, Encoding.UTF8.GetString(request.StandardInput!));
        Assert.Contains("instructionReferences", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("projectInstructionContext", legacy, StringComparison.Ordinal);
        var (dispatched, manifest) = await LoadAsync(provider, attemptId);
        Assert.NotNull(dispatched.AgentDispatchedAtUtc);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, dispatched.AgentOutcome);
        Assert.Equal(legacy, await SealedTextAsync(manifest));
    }

    /// <summary>The planning manifest exactly as it was sealed before this contract: the three fixed documentation names, no
    /// instruction section.</summary>
    private static string LegacyPlanningManifest(Seeded seeded) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["protocolVersion"] = CollaborationMessage.ProtocolVersionOne,
        ["objective"] = "Plan alpha",
        ["projectId"] = seeded.ProjectId,
        ["gitWorkspaceId"] = seeded.WorkspaceId,
        ["gitCheckpointId"] = Guid.NewGuid(),
        ["checkpointFingerprintSha256"] = Fingerprint,
        ["expectedMessageType"] = nameof(CollaborationMessageType.Proposal),
        ["expectedProposalSchema"] = CodexProposalOutputSchema.BuildSchemaDocument(),
        ["instructionReferences"] = new[] { "CLAUDE.md", "docs/engineering-context.md", "docs/architecture/agent-collaboration-protocol.md" },
        ["unresolvedHumanInstruction"] = null,
        ["priorDecisionMessageIds"] = Array.Empty<Guid>(),
    });
}
