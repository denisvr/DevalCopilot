using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestClaudeCriticalReview;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/claude-critical-review</c> against the real
/// Api host: authentication, every distinct conflict/not-found code
/// <c>CreateClaudeCriticalReviewAttemptCommandHandler</c> can produce (and the exact HTTP status
/// the shared <c>IResultProblemDetailsFactory</c> convention maps each one to), and the golden
/// path. Mirrors <c>RequestCodexPlanningAttemptEndpointTests</c> exactly, adapted for the extra
/// reviewed-Proposal validation chain this endpoint alone has. Each test constructs its own
/// <see cref="ClaudeCriticalReviewApiWebApplicationFactory"/> — never shared via
/// <c>IClassFixture</c> — for the same reason the Codex tests do not:
/// <c>HostCapabilitySnapshot</c> is a single host-scoped row per capability, so sharing one
/// factory's database across tests that need the Claude snapshot in different states would make
/// tests order dependent.
/// </summary>
public sealed class RequestClaudeCriticalReviewEndpointTests : IDisposable
{
    private static readonly string MatchingFingerprint = ClaudeCriticalReviewApiWebApplicationFactory.MatchingFingerprint;

    private static readonly string ValidProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly ClaudeCriticalReviewApiWebApplicationFactory _factory = new();

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

        var response = await client.PostAsJsonAsync(
            $"/api/runs/{Guid.NewGuid()}/agent-attempts/claude-critical-review", new RequestClaudeCriticalReviewRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await PostReviewAsync(client, Guid.NewGuid(), Guid.NewGuid());
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

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
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

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
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

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
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

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
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

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_missing", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_an_attempt_is_already_running_for_the_run()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: true, claudeObserved: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Attempts.Add(Attempt.Claim(Guid.NewGuid(), runId, 1, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("attempts.run_has_active_attempt", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_claude_capability_was_never_observed_successfully()
    {
        // The real host seeds the Claude capability catalog row as NeverProbed at startup and
        // this test never touches it — the freshly seeded state IS "not yet observed".
        var (runId, _, _) = await SeedEligibleRunAsync();
        using var client = CreateAuthenticatedClient();

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.provider_not_observed", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_workspace_source_has_drifted_since_the_checkpoint_was_captured()
    {
        // A dedicated factory whose IGitWorkspaceEvidenceReader reports a fingerprint that never
        // matches any checkpoint seeded here — proving the handler's own fresh, pre-dispatch
        // evidence recheck (never trusting a stored checkpoint as still current) rejects it.
        await using var driftFactory = new ClaudeCriticalReviewCheckpointDriftApiWebApplicationFactory();
        var (runId, _, _) = await SeedEligibleRunAsync(driftFactory, claudeObserved: true);

        using var client = driftFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_not_current", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        Assert.DoesNotContain(ClaudeCriticalReviewCheckpointDriftApiWebApplicationFactory.DriftedFingerprint, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_proposal_message()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claudeObserved: true);
        using var client = CreateAuthenticatedClient();

        var response = await PostReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_attempts.proposal_not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_when_the_proposal_belongs_to_a_different_run()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claudeObserved: true);
        var (otherRunId, _, _) = await SeedEligibleRunAsync();
        var (proposalMessageId, _) = await SeedCompletedCodexProposalAsync(otherRunId, workspaceId, checkpointId);

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, proposalMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_attempts.proposal_not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_proposal_is_not_a_provider_observed_codex_proposal()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claudeObserved: true);

        Guid selfAssertedMessageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            // Simulated provenance, and no owning attempt at all — never a real,
            // provider-observed Codex proposal, regardless of its Type/Actor.
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
                ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
                "A simulated proposal, never provider-observed.", ValidProposalStructuredContentJson,
                CollaborationMessageProvenance.Simulated, now);
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
            selfAssertedMessageId = message.Id;
        }

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, selfAssertedMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.not_provider_observed_codex_proposal", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_proposals_owning_attempt_did_not_complete_as_a_valid_proposal()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claudeObserved: true);
        var (proposalMessageId, _) = await SeedCompletedCodexProposalAsync(
            runId, workspaceId, checkpointId, ownerCompletesAsProposed: false);

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, proposalMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.proposal_attempt_not_valid", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_proposal_was_made_against_a_different_checkpoint()
    {
        var (runId, workspaceId, staleCheckpointId) = await SeedEligibleRunAsync(claudeObserved: true, includeCheckpoint: false);

        Guid proposalMessageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            // The stale checkpoint the owning attempt actually committed to.
            var staleCheckpoint = GitCheckpoint.Capture(
                Guid.NewGuid(), workspaceId, 1, now, new string('a', 40), new string('c', 64), []);
            dbContext.GitCheckpoints.Add(staleCheckpoint);
            await dbContext.SaveChangesAsync();

            var owningAttempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, workspaceId, staleCheckpoint.Id, staleCheckpoint.FingerprintSha256, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            owningAttempt.MarkAgentDispatched(now.AddSeconds(1));
            owningAttempt.CompleteAgent(AgentOutcome.Proposed, staleCheckpoint.FingerprintSha256, now.AddSeconds(2));
            dbContext.Attempts.Add(owningAttempt);

            var proposalMessage = CollaborationMessage.Record(
                Guid.NewGuid(), runId, owningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
                "Add the ledger table and its query.", ValidProposalStructuredContentJson,
                CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(2));
            dbContext.CollaborationMessages.Add(proposalMessage);
            await dbContext.SaveChangesAsync();
            proposalMessageId = proposalMessage.Id;

            // The current checkpoint — a later, distinct checkpoint whose fingerprint matches
            // what the fixed evidence reader reports, so the handler's own pre-dispatch recheck
            // passes and this checkpoint (not the stale one above) resolves as current.
            var currentCheckpoint = GitCheckpoint.Capture(
                Guid.NewGuid(), workspaceId, 2, now.AddSeconds(3), new string('a', 40), MatchingFingerprint, []);
            dbContext.GitCheckpoints.Add(currentCheckpoint);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, proposalMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.proposal_checkpoint_stale", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_proposal_already_has_a_successful_review()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claudeObserved: true);
        var (proposalMessageId, _) = await SeedCompletedCodexProposalAsync(runId, workspaceId, checkpointId);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            var priorReview = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 2, workspaceId, checkpointId, MatchingFingerprint, proposalMessageId, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            priorReview.MarkAgentDispatched(now.AddSeconds(1));
            priorReview.CompleteAgent(AgentOutcome.Accepted, MatchingFingerprint, now.AddSeconds(2));
            dbContext.Attempts.Add(priorReview);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, proposalMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.already_reviewed", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Succeeds_and_returns_the_attempt_identity_only()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claudeObserved: true);
        var (proposalMessageId, _) = await SeedCompletedCodexProposalAsync(runId, workspaceId, checkpointId);

        using var client = CreateAuthenticatedClient();
        var response = await PostReviewAsync(client, runId, proposalMessageId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestClaudeCriticalReviewResponse>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.AttemptId);
        Assert.Equal(2, payload.AttemptNumber);

        // The success body carries only the attempt identity — never the checkpoint fingerprint,
        // the workspace path, the resolved Claude executable path, or the reviewed proposal's
        // own content this run was seeded with.
        AssertNoDisclosure(body);
    }

    private static Task<HttpResponseMessage> PostReviewAsync(HttpClient client, Guid runId, Guid proposalMessageId) =>
        client.PostAsJsonAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review", new RequestClaudeCriticalReviewRequest(proposalMessageId));

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

    /// <summary>Seeds one owning Codex planning attempt (Completed/Proposed by default) and its
    /// provider-observed Proposal <see cref="CollaborationMessage"/>, bound to the given
    /// workspace/checkpoint — everything <see cref="CreateClaudeCriticalReviewAttemptCommandHandler"/>'s
    /// reviewed-Proposal validation chain requires, built directly against Domain, mirroring
    /// <c>GetAgentAttemptStatusEndpointTests</c>'s own seeding style rather than a command
    /// round-trip.</summary>
    private async Task<(Guid ProposalMessageId, Guid OwningAttemptId)> SeedCompletedCodexProposalAsync(
        Guid runId, Guid workspaceId, Guid checkpointId, bool ownerCompletesAsProposed = true)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var owningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now);
        owningAttempt.MarkAgentDispatched(now.AddSeconds(1));
        if (ownerCompletesAsProposed)
        {
            owningAttempt.CompleteAgent(AgentOutcome.Proposed, MatchingFingerprint, now.AddSeconds(2));
        }
        else
        {
            // Terminal but never a valid Proposal — Failed, not Running, so it never collides
            // with the run-wide "no active attempt" gate this handler also checks.
            owningAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(2));
        }

        dbContext.Attempts.Add(owningAttempt);

        var proposalMessage = CollaborationMessage.Record(
            Guid.NewGuid(), runId, owningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.", ValidProposalStructuredContentJson,
            CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(2));
        dbContext.CollaborationMessages.Add(proposalMessage);

        await dbContext.SaveChangesAsync();

        return (proposalMessage.Id, owningAttempt.Id);
    }

    private Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        bool claimRun = false,
        bool completeRun = false,
        bool includeWorkspace = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool includeCheckpoint = true,
        bool claudeObserved = false) =>
        SeedEligibleRunAsync(_factory, claimRun, completeRun, includeWorkspace, workspaceReady, leaseActive, includeCheckpoint, claudeObserved);

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        WebApplicationFactory<Program> factory,
        bool claimRun = false,
        bool completeRun = false,
        bool includeWorkspace = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool includeCheckpoint = true,
        bool claudeObserved = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Claude critical review project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the ledger proposal", now);
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

        await dbContext.SaveChangesAsync();

        if (claudeObserved)
        {
            var claude = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.ClaudeCli);
            claude.MarkDispatched(now);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        return (run.Id, workspaceId, checkpointId);
    }
}

/// <summary>
/// The real Api host with two narrow test seams: a fixed <see cref="IGitWorkspaceEvidenceReader"/>
/// (so a seeded checkpoint can be proven "current" without a real Git repository on disk), and
/// four background hosted services removed: <see cref="HostCapabilityReadinessSupervisor"/> and
/// <see cref="AgentAttemptSupervisor"/>/<see cref="ClaudeCriticalReviewSupervisor"/> would
/// otherwise race a test's own deterministic seeding of <c>HostCapabilitySnapshot</c> and
/// <c>Attempt</c> rows; <c>SimulatedRunSupervisor</c> polls for any run still
/// <see cref="RunLifecycle.Created"/> and claims it, exactly the run state
/// <c>CreateClaudeCriticalReviewAttemptCommandHandler</c> itself still accepts. Mirrors
/// <c>CodexPlanningApiWebApplicationFactory</c> exactly.
/// </summary>
public sealed class ClaudeCriticalReviewApiWebApplicationFactory : ApiWebApplicationFactory
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
            CodexPlanningTestHostSeams.RemoveHostedService<ClaudeCriticalReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}

/// <summary>
/// Same real host as <see cref="ClaudeCriticalReviewApiWebApplicationFactory"/>, except its fixed
/// evidence reader reports a fingerprint that deliberately never matches any checkpoint a test
/// seeds — proving <c>CreateClaudeCriticalReviewAttemptCommandHandler</c>'s fresh, pre-dispatch
/// evidence recheck (it never trusts a stored checkpoint row as still current) produces
/// "agent_attempts.checkpoint_not_current". Mirrors <c>CodexPlanningCheckpointDriftApiWebApplicationFactory</c>.
/// </summary>
public sealed class ClaudeCriticalReviewCheckpointDriftApiWebApplicationFactory : ApiWebApplicationFactory
{
    public const string DriftedFingerprint =
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new CodexPlanningTestHostSeams.FixedEvidenceReader(DriftedFingerprint));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ClaudeCriticalReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}
