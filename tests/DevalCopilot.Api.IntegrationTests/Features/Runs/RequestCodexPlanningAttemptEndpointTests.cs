using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Runs.RequestCodexPlanningAttempt;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/codex-plan</c> against the real Api host:
/// authentication, every distinct conflict code
/// <c>CreateCodexPlanningAttemptCommandHandler</c> can produce (and the exact HTTP status the
/// shared <c>IResultProblemDetailsFactory</c> convention maps each one to), and the golden path.
/// Each test constructs its own <see cref="CodexPlanningApiWebApplicationFactory"/> — never
/// shared via <c>IClassFixture</c> — because <c>HostCapabilitySnapshot</c> is a single
/// host-scoped row per capability: sharing one factory's database across tests that need the
/// Codex snapshot in different states (never observed vs. observed) would make tests order
/// dependent.
/// </summary>
public sealed class RequestCodexPlanningAttemptEndpointTests : IDisposable
{
    private static readonly string MatchingFingerprint = CodexPlanningApiWebApplicationFactory.MatchingFingerprint;

    private readonly CodexPlanningApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/codex-plan", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_run_is_not_active()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: true, completeRun: true);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("runs.not_active", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_no_git_workspace_exists_for_the_project()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(includeWorkspace: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.workspace_not_ready", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_workspace_is_not_ready()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(workspaceReady: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.workspace_not_ready", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_no_lease_is_active_for_the_workspace()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(leaseActive: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.lease_not_active", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_no_checkpoint_exists_for_the_workspace()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(includeCheckpoint: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_missing", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_there_is_no_codex_capability_snapshot_at_all()
    {
        var (runId, _, _) = await SeedEligibleRunAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
            dbContext.HostCapabilitySnapshots.Remove(codex);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.provider_not_observed", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_codex_capability_was_never_observed_successfully()
    {
        // The real host seeds the Codex capability catalog row as NeverProbed at startup and this
        // test never touches it — the freshly seeded state IS "not yet observed".
        var (runId, _, _) = await SeedEligibleRunAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.provider_not_observed", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_an_agent_attempt_is_already_running_for_the_run()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Attempts.Add(Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, DateTimeOffset.UtcNow, 1));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("attempts.run_has_active_attempt", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    // The run-wide invariant: a Codex plan must never be creatable while ANY attempt kind — not
    // just a prior Agent attempt — is Running for this run. Mirrors
    // CreateCodexPlanningAttemptCommandHandlerTests.HandleAsync_fails_when_a_non_agent_attempt_is_already_running_for_the_run
    // at the Application layer, but exercised here as a real HTTP round trip.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Returns_conflict_when_a_non_agent_attempt_is_already_running_for_the_run(bool simulated)
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var runningAttempt = simulated
                ? Attempt.Claim(Guid.NewGuid(), runId, 1, now)
                : Attempt.ClaimProcess(
                    Guid.NewGuid(), runId, 1,
                    new ProcessExecutionIntent(
                        @"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos",
                        TimeSpan.FromMinutes(5), 65536, 131072),
                    now);
            dbContext.Attempts.Add(runningAttempt);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("attempts.run_has_active_attempt", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Single(verifyContext.Attempts.Where(a => a.RunId == runId));
    }

    [Fact]
    public async Task Returns_conflict_when_the_run_wide_agent_claim_budget_is_exhausted()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(
            claimRun: true, codexObserved: true, maximumAgentAttempts: 1, preExistingAgentAttempts: 1);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.budget_exhausted", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        // Exhaustion never invokes the provider or creates a second claimed attempt: the one
        // pre-existing Agent attempt this test seeded remains the only one for this run.
        Assert.Single(verifyContext.Attempts.Where(a => a.RunId == runId));
    }

    [Fact]
    public async Task Succeeds_and_returns_the_attempt_identity_only()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(codexObserved: true);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestCodexPlanningAttemptResponse>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.AttemptId);
        Assert.Equal(1, payload.AttemptNumber);

        // The success body carries only the attempt identity — never the checkpoint fingerprint,
        // the workspace path, or the resolved Codex executable path this run was seeded with.
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_workspace_source_has_drifted_since_the_checkpoint_was_captured()
    {
        // A dedicated factory whose IGitWorkspaceEvidenceReader reports a fingerprint that never
        // matches any checkpoint seeded here — proving the handler's own fresh, pre-dispatch
        // evidence recheck (never trusting a stored checkpoint as still current) rejects it.
        await using var driftFactory = new CodexPlanningCheckpointDriftApiWebApplicationFactory();
        var (runId, _, _) = await SeedEligibleRunAsync(driftFactory, codexObserved: true);

        using var client = driftFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_not_current", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        Assert.DoesNotContain(CodexPlanningCheckpointDriftApiWebApplicationFactory.DriftedFingerprint, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_a_safe_domain_conflict_when_sealing_the_context_manifest_fails()
    {
        // A dedicated factory whose IArtifactStore always fails to seal — proving the handler's
        // own context-manifest-seal failure reaches the client as a safe, generic 422 rather than
        // a raw exception or filesystem detail, and creates no attempt.
        await using var sealFailureFactory = new CodexPlanningSealFailureApiWebApplicationFactory();
        var (runId, _, _) = await SeedEligibleRunAsync(sealFailureFactory, codexObserved: true);

        using var client = sealFailureFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var body = await response.Content.ReadAsStringAsync();

        // Error.Failure maps to 422 under this codebase's IResultProblemDetailsFactory
        // convention (confirmed directly against the shared package: NotFound->404,
        // Conflict->409, Failure->422), never a bare 400/500 with leaked exception detail.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("agent_attempts.context_manifest_seal_failed", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);

        using var scope = sealFailureFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(dbContext.Attempts.Where(a => a.RunId == runId));
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MatchingFingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain("resolvedExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawOutput", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    private Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        bool claimRun = false,
        bool completeRun = false,
        bool includeWorkspace = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool includeCheckpoint = true,
        bool codexObserved = false,
        int maximumAgentAttempts = 16,
        int preExistingAgentAttempts = 0) =>
        SeedEligibleRunAsync(
            _factory, claimRun, completeRun, includeWorkspace, workspaceReady, leaseActive, includeCheckpoint, codexObserved,
            maximumAgentAttempts, preExistingAgentAttempts);

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        WebApplicationFactory<Program> factory,
        bool claimRun = false,
        bool completeRun = false,
        bool includeWorkspace = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool includeCheckpoint = true,
        bool codexObserved = false,
        int maximumAgentAttempts = 16,
        int preExistingAgentAttempts = 0)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Codex planning project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", now, maximumAgentAttempts: maximumAgentAttempts);
        if (claimRun || completeRun)
        {
            run.Claim(now);
        }

        if (completeRun)
        {
            run.Complete(now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);

        var workspaceId = Guid.Empty;
        var checkpointId = Guid.Empty;

        if (includeWorkspace)
        {
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
            if (workspaceReady)
            {
                workspace.MarkReady();
            }

            dbContext.GitWorkspaces.Add(workspace);
            workspaceId = workspace.Id;

            if (includeCheckpoint)
            {
                var checkpoint = GitCheckpoint.Capture(
                    Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), MatchingFingerprint, []);
                dbContext.GitCheckpoints.Add(checkpoint);
                checkpointId = checkpoint.Id;
            }

            var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
            if (!leaseActive)
            {
                lease.Release(now);
            }

            dbContext.RepositoryMutationLeases.Add(lease);
        }

        for (var slot = 1; slot <= preExistingAgentAttempts; slot++)
        {
            var preExisting = Attempt.ClaimAgent(
                Guid.NewGuid(), run.Id, slot, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, slot);
            preExisting.Fail(now);
            dbContext.Attempts.Add(preExisting);
        }

        await dbContext.SaveChangesAsync();

        if (codexObserved)
        {
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        return (run.Id, workspaceId, checkpointId);
    }
}

/// <summary>
/// The real Api host with two narrow test seams: a fixed <see cref="IGitWorkspaceEvidenceReader"/>
/// (so a seeded checkpoint can be proven "current" without a real Git repository on disk — the
/// same pattern <c>VerificationCommandsEndpointTests.ReviewApiWebApplicationFactory</c> uses), and
/// three background hosted services removed: <see cref="HostCapabilityReadinessSupervisor"/> and
/// <see cref="AgentAttemptSupervisor"/> would otherwise race a test's own deterministic seeding of
/// <c>HostCapabilitySnapshot</c> and <c>Attempt</c> rows (real probing against a machine that
/// almost certainly has no real Codex CLI installed, and real dispatch of a seeded Agent attempt
/// against a fabricated executable path); <c>SimulatedRunSupervisor</c> polls every 200ms for ANY
/// run still in <see cref="RunLifecycle.Created"/> and claims it with a Simulated
/// <c>AttemptNumber</c> 1 — exactly the run state <c>CreateCodexPlanningAttemptCommandHandler</c>
/// itself still accepts, so leaving it running raced these tests' own seeded runs for the first
/// attempt-number slot. Every other hosted service (Process, Verification) is left registered
/// exactly as production wires it, since neither touches Agent-attempt, Codex-capability, or
/// Simulated-run state.
/// </summary>
public sealed class CodexPlanningApiWebApplicationFactory : ApiWebApplicationFactory
{
    public const string MatchingFingerprint =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new CodexPlanningTestHostSeams.FixedEvidenceReader(MatchingFingerprint));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}

/// <summary>
/// Same real host as <see cref="CodexPlanningApiWebApplicationFactory"/>, except its fixed
/// evidence reader reports a fingerprint that deliberately never matches any checkpoint a test
/// seeds — proving <c>CreateCodexPlanningAttemptCommandHandler</c>'s fresh, pre-dispatch evidence
/// recheck (it never trusts a stored checkpoint row as still current) produces
/// "agent_attempts.checkpoint_not_current".
/// </summary>
public sealed class CodexPlanningCheckpointDriftApiWebApplicationFactory : ApiWebApplicationFactory
{
    public const string DriftedFingerprint =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new CodexPlanningTestHostSeams.FixedEvidenceReader(DriftedFingerprint));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}

/// <summary>
/// Same real host as <see cref="CodexPlanningApiWebApplicationFactory"/>, except
/// <see cref="IArtifactStore"/> is a thin decorator over a genuine <see cref="FilesystemArtifactStore"/>
/// (rooted beneath a disposable temp directory, never the real application-data artifact root)
/// whose <c>SealAsync</c> always reports failure — exactly the "seal itself fails" case the real
/// store's own contract documents (<c>IArtifactStore.SealAsync</c> never throws for it). Proves
/// the handler's own "agent_attempts.context_manifest_seal_failed" conflict end to end without
/// needing to break the real filesystem.
/// </summary>
public sealed class CodexPlanningSealFailureApiWebApplicationFactory : ApiWebApplicationFactory
{
    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-codex-seal-failure-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(
                new CodexPlanningTestHostSeams.FixedEvidenceReader(CodexPlanningApiWebApplicationFactory.MatchingFingerprint));

            services.RemoveAll<IArtifactStore>();
            services.AddSingleton<IArtifactStore>(new SealAlwaysFailsArtifactStore(_artifactRoot));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    private sealed class SealAlwaysFailsArtifactStore(string root) : IArtifactStore
    {
        private readonly FilesystemArtifactStore _inner = new(root);

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.GetPartialPath(runId, attemptId, purpose);

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.GetSealedRelativePath(runId, attemptId, purpose);

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult<SealedOutputFile?>(null);

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.HasSealedFile(runId, attemptId, purpose);

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.HasPartialFile(runId, attemptId, purpose);

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            _inner.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.DeleteOrphanedPartialFile(runId, attemptId, purpose);

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            _inner.DeleteOrphanedSealedFile(runId, attemptId, purpose);

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            _inner.ReadPartialAsync(runId, attemptId, purpose, fromOffset, maxBytes, cancellationToken);

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            _inner.VerifyAndReadSealedAsync(relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
    }
}

/// <summary>Test seams shared by every <c>CodexPlanning*ApiWebApplicationFactory</c> above.</summary>
internal static class CodexPlanningTestHostSeams
{
    public static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : class
    {
        foreach (var descriptor in services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(THostedService))
            .ToList())
        {
            services.Remove(descriptor);
        }
    }

    public sealed class FixedEvidenceReader(string fingerprintSha256) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null));
    }
}
