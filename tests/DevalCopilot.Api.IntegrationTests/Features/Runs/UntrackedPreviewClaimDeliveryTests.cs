using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.RealAdapterInstructionProbe;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Physically proven untracked-file previews (ADR-0022) through REAL production claims: the real Git capture over a real worktree
/// whose untracked paths are real hard links to an outside file feeds the real claim handlers, the real artifact store and the
/// REAL provider adapters (both providers) over a recording process double. The outside sentinel never reaches the claimed
/// manifest, the sealed bytes or the adapter's standard input while an unrelated safe sibling does; a claim that requests no
/// previews delivers none; the reserved root instruction names keep their own section; a manifest sealed before a second name
/// appeared replays its exact bytes while a later fresh claim carries the truthful omission. No provider is ever started.
/// </summary>
public sealed class UntrackedPreviewClaimDeliveryTests : IDisposable
{
    private const string AgentsMarker = "ROOT-AGENTS-MARKER-41ae conventions of this worktree";
    private const string SharedText = "SHARED-NAME-TEXT-0c19 safe until a second name appears";

    private static readonly string ProposalContent = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private static readonly string ChallengeResponse = JsonSerializer.Serialize(new
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

    private static readonly string AcceptanceResponse = JsonSerializer.Serialize(new
    {
        decision = "accept",
        summary = "Sound and complete.",
        rationale = "The proposal is feasible as written.",
    });

    private readonly UntrackedPreviewScene _scene = UntrackedPreviewScene.Create();
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-preview-claims-{Guid.NewGuid():N}.db");
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _launch;
    private readonly GitWorkspaceEvidenceReader _reader = new(new ChildProcessExecutionAdapter());

    public UntrackedPreviewClaimDeliveryTests()
    {
        _artifactStore = new FilesystemArtifactStore(Path.Combine(_scene.Root, "artifacts"));
        _launch = Path.Combine(_scene.Root, "provider.exe");
        File.WriteAllText(_launch, string.Empty);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        _scene.Dispose();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(_reader);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddDevalenteMediator(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(CreateImplementationAttemptCommand).Assembly);
        services.AddScoped<IAttemptDurabilityProbe>(sp =>
            new AttemptDurabilityProbe(sp.GetRequiredService<DbContextOptions<DevalCopilotDbContext>>()));
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    /// <summary>The run, workspace, checkpoint, lease and observed provider capabilities, all pointing at the REAL worktree and
    /// at the fingerprint the real capture computes for its current state (the claims re-capture and compare it).</summary>
    private async Task<(Guid RunId, string Fingerprint)> SeedAsync(ServiceProvider provider)
    {
        var baseline = await _reader.CaptureAsync(_scene.Repository, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, baseline.Outcome);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", _scene.Repository, now);
        db.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the ledger table and its query", now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, _scene.Repository, "branch", baseline.HeadCommitSha!, "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, baseline.HeadCommitSha!, baseline.FingerprintSha256!, []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        foreach (var (capability, executable) in new[] { (Capability.CodexCli, @"C:\fake\codex.exe"), (Capability.ClaudeCli, _launch) })
        {
            var snapshot = HostCapabilitySnapshot.Seed(capability, now);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, executable, null, "1.2.3", now, now.AddMinutes(5));
            db.HostCapabilitySnapshots.Add(snapshot);
        }

        run.Claim(now);
        await db.SaveChangesAsync();
        return (run.Id, baseline.FingerprintSha256!);
    }

    private static async Task<Guid> ClaimPlanningAsync(ServiceProvider provider, Guid runId)
    {
        await using var scope = provider.CreateAsyncScope();
        var planning = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);
        Assert.True(planning.IsSuccess, planning.IsFailure ? planning.Errors[0].Code : null);
        return planning.Value.AttemptId;
    }

    private static async Task<Guid> ClaimCriticalReviewAsync(ServiceProvider provider, Guid runId, Guid proposalId)
    {
        await using var scope = provider.CreateAsyncScope();
        var review = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(review.IsSuccess, review.IsFailure ? review.Errors[0].Code : null);
        return review.Value.AttemptId;
    }

    private static async Task RecordReviewAsync(
        ServiceProvider provider, Guid runId, Guid reviewAttemptId, AgentOutcome outcome, string response, string fingerprint)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, reviewAttemptId), CancellationToken.None)).IsSuccess);
        var parsed = ClaudeCriticalReviewResponseParser.TryParse(response);
        Assert.NotNull(parsed);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, reviewAttemptId, outcome, fingerprint, [], parsed, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
    }

    private static async Task<Guid> ClaimResolutionAsync(ServiceProvider provider, Guid runId, Guid reviewAttemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var resolution = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateChallengeResolutionAttemptCommand(runId, reviewAttemptId), CancellationToken.None);
        Assert.True(resolution.IsSuccess, resolution.IsFailure ? resolution.Errors[0].Code : null);
        return resolution.Value.AttemptId;
    }

    private static async Task<Guid> ClaimImplementationAsync(ServiceProvider provider, Guid runId, Guid proposalId)
    {
        await using var scope = provider.CreateAsyncScope();
        var implementation = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new CreateImplementationAttemptCommand(runId, proposalId, null), CancellationToken.None);
        Assert.True(implementation.IsSuccess, implementation.IsFailure ? implementation.Errors[0].Code : null);
        return implementation.Value.AttemptId;
    }

    /// <summary>The claim's own sealed manifest, delivered through the REAL adapter of its role.</summary>
    private async Task<Delivery> DeliverClaimAsync(ServiceProvider provider, Guid attemptId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        var manifest = await db.Artifacts.AsNoTracking().SingleAsync(
            candidate => candidate.AttemptId == attemptId && candidate.Purpose == ArtifactPurpose.AgentContextManifest);
        var delivery = await DeliverAsync(attempt, manifest, _scene.Repository, _artifactStore, _launch);
        Assert.Equal(delivery.SealedManifest, delivery.Stdin);
        return delivery;
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private static JsonElement[] UntrackedEntries(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.GetProperty("changeEvidence").GetProperty("untrackedFiles").GetProperty("files")
            .EnumerateArray().Select(entry => entry.Clone()).ToArray();
    }

    /// <summary>The sentinel appears nowhere; the safe sibling and control appear exactly once, as included previews; each unsafe
    /// path is named with the fixed omission and neither text nor size; the root instruction name keeps only its own section.</summary>
    private static void AssertSafeDelivery(Delivery delivery)
    {
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.SealedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.Stdin, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(delivery.Stdin, UntrackedPreviewScene.SafeText));
        Assert.Equal(1, Occurrences(delivery.Stdin, UntrackedPreviewScene.ControlText));
        var entries = UntrackedEntries(delivery.Stdin).ToDictionary(entry => entry.GetProperty("path").GetString()!);
        foreach (var linked in UntrackedPreviewScene.LinkedPaths)
        {
            Assert.Equal("containment_unproven", entries[linked].GetProperty("omissionReason").GetString());
            Assert.Equal(JsonValueKind.Null, entries[linked].GetProperty("sizeBytes").ValueKind);
            Assert.Equal(JsonValueKind.Null, entries[linked].GetProperty("text").ValueKind);
        }

        Assert.Equal("included", entries["safe-sibling.txt"].GetProperty("preview").GetString());
        Assert.Equal("included", entries["deep/control.txt"].GetProperty("preview").GetString());
        Assert.Equal("reserved_instruction_file", entries["AGENTS.md"].GetProperty("omissionReason").GetString());
        Assert.Equal(1, Occurrences(delivery.Stdin, AgentsMarker));
    }

    private void ArrangeUnsafeAndSafeFiles()
    {
        _scene.AddOutsideHardLinks();
        _scene.AddSafeSiblings();
        _scene.Write("AGENTS.md", AgentsMarker);
        // Precondition independent of the host under test: the linked paths really read as the outside bytes.
        Assert.Equal(UntrackedPreviewScene.Sentinel, File.ReadAllText(Path.Combine(_scene.Repository, "linked.txt")));
        Assert.Equal(UntrackedPreviewScene.Sentinel, File.ReadAllText(Path.Combine(_scene.Repository, "deep", "nested", "linked.txt")));
    }

    [Fact]
    public async Task Claude_and_Codex_claims_deliver_the_safe_sibling_and_never_the_outside_sentinel_and_planning_requests_no_previews()
    {
        ArrangeUnsafeAndSafeFiles();
        await using var provider = BuildProvider();
        var (runId, fingerprint) = await SeedAsync(provider);

        // A claim that requests no previews delivers none, the safe sibling included, and never carries the sentinel either.
        var planningId = await ClaimPlanningAsync(provider, runId);
        var planning = await DeliverClaimAsync(provider, planningId);
        Assert.Equal(AgentProvider.Codex, planning.Provider);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, planning.Stdin, StringComparison.Ordinal);
        Assert.DoesNotContain(UntrackedPreviewScene.SafeText, planning.Stdin, StringComparison.Ordinal);
        Assert.DoesNotContain("untrackedFiles", planning.Stdin, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(planning.Stdin, AgentsMarker));
        await CompletePlanningAsync(provider, runId, planningId, fingerprint);
        var proposalId = await ProposalOfAsync(provider, planningId);

        var reviewId = await ClaimCriticalReviewAsync(provider, runId, proposalId);
        var review = await DeliverClaimAsync(provider, reviewId);
        Assert.Equal(AgentProvider.ClaudeCode, review.Provider);
        Assert.Equal(AgentResponseContract.CriticalReview, review.Contract);
        AssertSafeDelivery(review);

        await RecordReviewAsync(provider, runId, reviewId, AgentOutcome.Challenged, ChallengeResponse, fingerprint);
        var resolutionId = await ClaimResolutionAsync(provider, runId, reviewId);
        var resolution = await DeliverClaimAsync(provider, resolutionId);
        Assert.Equal(AgentProvider.Codex, resolution.Provider);
        Assert.Equal(AgentResponseContract.ChallengeResolution, resolution.Contract);
        AssertSafeDelivery(resolution);
    }

    [Fact]
    public async Task A_Claude_implementation_claim_after_an_accepted_review_delivers_the_same_safe_evidence()
    {
        ArrangeUnsafeAndSafeFiles();
        await using var provider = BuildProvider();
        var (runId, fingerprint) = await SeedAsync(provider);
        var planningId = await ClaimPlanningAsync(provider, runId);
        await CompletePlanningAsync(provider, runId, planningId, fingerprint);
        var proposalId = await ProposalOfAsync(provider, planningId);
        var reviewId = await ClaimCriticalReviewAsync(provider, runId, proposalId);
        await RecordReviewAsync(provider, runId, reviewId, AgentOutcome.Accepted, AcceptanceResponse, fingerprint);

        var implementationId = await ClaimImplementationAsync(provider, runId, proposalId);
        var implementation = await DeliverClaimAsync(provider, implementationId);

        Assert.Equal(AgentProvider.ClaudeCode, implementation.Provider);
        Assert.Equal(AgentResponseContract.ImplementationReport, implementation.Contract);
        AssertSafeDelivery(implementation);
    }

    [Fact]
    public async Task A_manifest_sealed_before_a_second_name_appeared_replays_its_exact_bytes_and_a_later_fresh_claim_omits_the_file()
    {
        _scene.Write("shared-name.txt", SharedText);
        _scene.AddSafeSiblings();
        await using var provider = BuildProvider();
        var (runId, fingerprint) = await SeedAsync(provider);
        var planningId = await ClaimPlanningAsync(provider, runId);
        await CompletePlanningAsync(provider, runId, planningId, fingerprint);
        var proposalId = await ProposalOfAsync(provider, planningId);

        var reviewId = await ClaimCriticalReviewAsync(provider, runId, proposalId);
        var atClaim = await DeliverClaimAsync(provider, reviewId);
        Assert.Equal(1, Occurrences(atClaim.Stdin, SharedText));
        Assert.Equal("included", UntrackedEntries(atClaim.Stdin).Single(entry => entry.GetProperty("path").GetString() == "shared-name.txt")
            .GetProperty("preview").GetString());

        // A second name for the same bytes appears outside the worktree: same content, same Git identity, same fingerprint.
        _scene.AddOutsideAliasOf("shared-name.txt", "outside-alias.txt");
        Assert.Equal(fingerprint, (await _reader.CaptureAsync(_scene.Repository, CancellationToken.None)).FingerprintSha256);

        // The sealed manifest replays its exact bytes (it still carries the preview it was sealed with) ...
        var replayed = await DeliverClaimAsync(provider, reviewId);
        Assert.Equal(atClaim.SealedManifest, replayed.SealedManifest);
        Assert.Equal(atClaim.Stdin, replayed.Stdin);
        Assert.Equal(1, Occurrences(replayed.Stdin, SharedText));

        // ... while a later fresh claim captures the truthful omission and still delivers the healthy siblings.
        await RecordReviewAsync(provider, runId, reviewId, AgentOutcome.Accepted, AcceptanceResponse, fingerprint);
        var fresh = await DeliverClaimAsync(provider, await ClaimImplementationAsync(provider, runId, proposalId));
        Assert.NotEqual(atClaim.Stdin, fresh.Stdin);
        Assert.DoesNotContain(SharedText, fresh.Stdin, StringComparison.Ordinal);
        var shared = UntrackedEntries(fresh.Stdin).Single(entry => entry.GetProperty("path").GetString() == "shared-name.txt");
        Assert.Equal("containment_unproven", shared.GetProperty("omissionReason").GetString());
        Assert.Equal(JsonValueKind.Null, shared.GetProperty("text").ValueKind);
        Assert.Equal(1, Occurrences(fresh.Stdin, UntrackedPreviewScene.SafeText));
        Assert.Equal(1, Occurrences(fresh.Stdin, UntrackedPreviewScene.ControlText));
        // The earlier sealed artifact is still exactly what it was.
        Assert.Equal(atClaim.SealedManifest, (await DeliverClaimAsync(provider, reviewId)).SealedManifest);
    }

    // ---- tracked evidence (ADR-0024) -------------------------------------------------------------------------------------------

    private void ArrangeTrackedUnsafeAndSafeFiles()
    {
        _scene.CommitTrackedFixtures();
        _scene.MakeTrackedChanges();
        // Precondition independent of the host under test: the linked tracked paths really read as the outside bytes.
        Assert.Equal(UntrackedPreviewScene.Sentinel, File.ReadAllText(Path.Combine(_scene.Repository, "tracked-linked.txt")));
        Assert.Equal(UntrackedPreviewScene.Sentinel, File.ReadAllText(Path.Combine(_scene.Repository, "deep", "tracked-nested-linked.txt")));
    }

    /// <summary>The sentinel appears nowhere; the safe sibling's attested change appears exactly once; each unsafe tracked path is named
    /// with the fixed omission and no text; the diff is never presented as complete.</summary>
    private static void AssertTrackedDelivery(Delivery delivery)
    {
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.SealedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, delivery.Stdin, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(delivery.Stdin, "TRACKED-SAFE-AFTER-8d12"));
        Assert.Equal(1, Occurrences(delivery.Stdin, "TRACKED-CONTROL-AFTER-56be"));
        using var document = JsonDocument.Parse(delivery.Stdin);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.Equal("host_prefix_suffix_v1", evidence.GetProperty("trackedComparison").GetProperty("method").GetString());
        var selection = evidence.GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        Assert.Equal(2, selection.GetProperty("omissionReasons").GetProperty("containment_unproven").GetInt32());
        var items = selection.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("path").GetString()!);
        foreach (var linked in UntrackedPreviewScene.TrackedLinkedPaths)
        {
            Assert.Equal("containment_unproven", items[linked].GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task Claude_and_Codex_claims_deliver_the_attested_tracked_sibling_and_never_the_outside_sentinel_of_a_tracked_hard_link()
    {
        ArrangeTrackedUnsafeAndSafeFiles();
        await using var provider = BuildProvider();
        var (runId, fingerprint) = await SeedAsync(provider);
        var planningId = await ClaimPlanningAsync(provider, runId);
        var planning = await DeliverClaimAsync(provider, planningId);
        Assert.DoesNotContain(UntrackedPreviewScene.Sentinel, planning.Stdin, StringComparison.Ordinal);
        await CompletePlanningAsync(provider, runId, planningId, fingerprint);
        var proposalId = await ProposalOfAsync(provider, planningId);

        var reviewId = await ClaimCriticalReviewAsync(provider, runId, proposalId);
        var review = await DeliverClaimAsync(provider, reviewId);
        Assert.Equal(AgentProvider.ClaudeCode, review.Provider);
        AssertTrackedDelivery(review);

        await RecordReviewAsync(provider, runId, reviewId, AgentOutcome.Challenged, ChallengeResponse, fingerprint);
        var resolutionId = await ClaimResolutionAsync(provider, runId, reviewId);
        var resolution = await DeliverClaimAsync(provider, resolutionId);
        Assert.Equal(AgentProvider.Codex, resolution.Provider);
        AssertTrackedDelivery(resolution);
    }

    [Fact]
    public async Task A_Claude_implementation_claim_delivers_the_same_attested_tracked_evidence()
    {
        ArrangeTrackedUnsafeAndSafeFiles();
        await using var provider = BuildProvider();
        var (runId, fingerprint) = await SeedAsync(provider);
        var planningId = await ClaimPlanningAsync(provider, runId);
        await CompletePlanningAsync(provider, runId, planningId, fingerprint);
        var proposalId = await ProposalOfAsync(provider, planningId);
        var reviewId = await ClaimCriticalReviewAsync(provider, runId, proposalId);
        await RecordReviewAsync(provider, runId, reviewId, AgentOutcome.Accepted, AcceptanceResponse, fingerprint);

        var implementation = await DeliverClaimAsync(provider, await ClaimImplementationAsync(provider, runId, proposalId));

        Assert.Equal(AgentResponseContract.ImplementationReport, implementation.Contract);
        AssertTrackedDelivery(implementation);
    }

    [Fact]
    public async Task A_tracked_manifest_sealed_before_a_second_name_appeared_replays_its_exact_bytes_after_a_restart_and_a_fresh_claim_omits_the_file()
    {
        _scene.CommitTrackedFixtures();
        _scene.Write("tracked-safe.txt", UntrackedPreviewScene.TrackedSafeAfter);
        _scene.Write("deep/tracked-control.txt", UntrackedPreviewScene.TrackedControlAfter);
        _scene.Write("tracked-linked.txt", "SHARED-TRACKED-TEXT-6a21 edited tracked file\n");
        Guid runId;
        Guid reviewId;
        Guid proposalId;
        string fingerprint;
        Delivery atClaim;
        await using (var first = BuildProvider())
        {
            (runId, fingerprint) = await SeedAsync(first);
            var planningId = await ClaimPlanningAsync(first, runId);
            await CompletePlanningAsync(first, runId, planningId, fingerprint);
            proposalId = await ProposalOfAsync(first, planningId);
            reviewId = await ClaimCriticalReviewAsync(first, runId, proposalId);
            atClaim = await DeliverClaimAsync(first, reviewId);
        }

        Assert.Equal(1, Occurrences(atClaim.Stdin, "SHARED-TRACKED-TEXT-6a21"));
        Assert.Equal(1, Occurrences(atClaim.Stdin, "TRACKED-SAFE-AFTER-8d12"));

        // The host restarts while a second name for the same bytes appears outside the worktree: same content, same raw identity.
        _scene.AddOutsideAliasOf("tracked-linked.txt", "outside-alias.txt");
        Assert.Equal(fingerprint, (await _reader.CaptureAsync(_scene.Repository, CancellationToken.None)).FingerprintSha256);
        await using var restarted = BuildProvider();

        var replayed = await DeliverClaimAsync(restarted, reviewId);
        Assert.Equal(atClaim.SealedManifest, replayed.SealedManifest);
        Assert.Equal(atClaim.Stdin, replayed.Stdin);

        await RecordReviewAsync(restarted, runId, reviewId, AgentOutcome.Accepted, AcceptanceResponse, fingerprint);
        var fresh = await DeliverClaimAsync(restarted, await ClaimImplementationAsync(restarted, runId, proposalId));
        Assert.NotEqual(atClaim.Stdin, fresh.Stdin);
        Assert.DoesNotContain("SHARED-TRACKED-TEXT-6a21", fresh.Stdin, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(fresh.Stdin, "TRACKED-SAFE-AFTER-8d12"));
        using var document = JsonDocument.Parse(fresh.Stdin);
        var selection = document.RootElement.GetProperty("changeEvidence").GetProperty("diffSelection");
        Assert.Equal(
            "containment_unproven",
            selection.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("path").GetString() == "tracked-linked.txt")
                .GetProperty("reason").GetString());
        // The earlier sealed artifact is still exactly what it was.
        Assert.Equal(atClaim.SealedManifest, (await DeliverClaimAsync(restarted, reviewId)).SealedManifest);
    }

    private static async Task CompletePlanningAsync(ServiceProvider provider, Guid runId, Guid planningId, string fingerprint)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, planningId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                runId, planningId, AgentOutcome.Proposed, fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ProposalContent), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
    }

    private static async Task<Guid> ProposalOfAsync(ServiceProvider provider, Guid planningId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (await db.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == planningId && message.Type == CollaborationMessageType.Proposal)).Id;
    }
}
