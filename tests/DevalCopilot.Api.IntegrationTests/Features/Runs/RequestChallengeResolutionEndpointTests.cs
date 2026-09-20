using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestChallengeResolution;
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
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/challenge-resolution</c> against the real
/// Api host: authentication, the golden path, and a representative subset of the distinct
/// conflict/not-found codes <c>CreateChallengeResolutionAttemptCommandHandler</c> can produce.
/// Mirrors <c>RequestClaudeCriticalReviewEndpointTests</c>'s hosting/seeding style, scoped down
/// to what is genuinely specific to this new stage — the exhaustive workspace/lease/checkpoint
/// eligibility permutations are already proven at the handler-unit level in
/// <c>CreateChallengeResolutionAttemptCommandHandlerTests</c> and do not need duplicating end to
/// end through a real HTTP host for every branch.
/// </summary>
public sealed class RequestChallengeResolutionEndpointTests : IDisposable
{
    private static readonly string MatchingFingerprint = ChallengeResolutionApiWebApplicationFactory.MatchingFingerprint;

    private static readonly string ValidProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly ChallengeResolutionApiWebApplicationFactory _factory = new();

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
            $"/api/runs/{Guid.NewGuid()}/agent-attempts/challenge-resolution", new RequestChallengeResolutionRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await PostResolutionAsync(client, Guid.NewGuid(), Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_run_is_not_running()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: false);
        using var client = CreateAuthenticatedClient();

        var response = await PostResolutionAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("runs.not_running", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_challenged_review_attempt()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        using var client = CreateAuthenticatedClient();

        var response = await PostResolutionAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_attempts.challenged_review_not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_workspace_source_has_drifted_since_the_checkpoint_was_captured()
    {
        await using var driftFactory = new ChallengeResolutionCheckpointDriftApiWebApplicationFactory();
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(driftFactory, claimRun: true, codexObserved: true);
        var (reviewAttemptId, _, _) = await SeedChallengedReviewAsync(driftFactory, runId, workspaceId, checkpointId);

        using var client = driftFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await PostResolutionAsync(client, runId, reviewAttemptId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_not_current", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        Assert.DoesNotContain(ChallengeResolutionCheckpointDriftApiWebApplicationFactory.DriftedFingerprint, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_conflict_when_this_challenged_review_already_has_a_successful_resolution()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        var (reviewAttemptId, _, challengeIds) = await SeedChallengedReviewAsync(_factory, runId, workspaceId, checkpointId);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            var priorResolution = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 3, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now);
            priorResolution.MarkAgentDispatched(now.AddSeconds(1));
            priorResolution.CompleteAgent(AgentOutcome.Resolved, MatchingFingerprint, now.AddSeconds(2));
            dbContext.Attempts.Add(priorResolution);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), priorResolution.Id, challengeIds[0], sequence: 1));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await PostResolutionAsync(client, runId, reviewAttemptId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.already_resolved", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Succeeds_and_returns_the_attempt_identity_only()
    {
        var (runId, workspaceId, checkpointId) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        var (reviewAttemptId, _, _) = await SeedChallengedReviewAsync(_factory, runId, workspaceId, checkpointId);

        using var client = CreateAuthenticatedClient();
        var response = await PostResolutionAsync(client, runId, reviewAttemptId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestChallengeResolutionResponse>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.AttemptId);

        // The success body carries only the attempt identity — never the checkpoint fingerprint,
        // the workspace path, the resolved Codex executable path, or the proposal/challenge
        // content this run was seeded with.
        AssertNoDisclosure(body);
    }

    private static Task<HttpResponseMessage> PostResolutionAsync(HttpClient client, Guid runId, Guid challengedReviewAttemptId) =>
        client.PostAsJsonAsync(
            $"/api/runs/{runId}/agent-attempts/challenge-resolution", new RequestChallengeResolutionRequest(challengedReviewAttemptId));

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

    /// <summary>Seeds a complete, valid Challenged Claude critical-review chain directly against
    /// Domain — the owning Codex planning attempt, its Proposal, a completed Claude
    /// critical-review attempt bound to the given workspace/checkpoint, and two Challenges — the
    /// exact evidence <c>CreateChallengeResolutionAttemptCommandHandler</c> requires. Mirrors
    /// <c>RequestClaudeCriticalReviewEndpointTests.SeedCompletedCodexProposalAsync</c>'s
    /// direct-Domain seeding style.</summary>
    private async Task<(Guid ReviewAttemptId, Guid ProposalMessageId, List<Guid> ChallengeIds)> SeedChallengedReviewAsync(
        WebApplicationFactory<Program> factory, Guid runId, Guid workspaceId, Guid checkpointId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now);
        planningAttempt.MarkAgentDispatched(now.AddSeconds(1));
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, MatchingFingerprint, now.AddSeconds(2));
        dbContext.Attempts.Add(planningAttempt);

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.", ValidProposalStructuredContentJson,
            CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(2));
        dbContext.CollaborationMessages.Add(proposal);

        var reviewAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, MatchingFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(3));
        reviewAttempt.MarkAgentDispatched(now.AddSeconds(4));
        reviewAttempt.CompleteAgent(AgentOutcome.Challenged, MatchingFingerprint, now.AddSeconds(5));
        dbContext.Attempts.Add(reviewAttempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), reviewAttempt.Id, proposal.Id, sequence: 0));

        var challengeIds = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var challenge = CollaborationMessage.Record(
                Guid.NewGuid(), runId, reviewAttempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge, proposal.Id,
                $"Challenge {index + 1} summary",
                JsonSerializer.Serialize(new
                {
                    disputedItem = $"Disputed item {index + 1}",
                    materialImpact = $"Material impact {index + 1}",
                    reasoning = $"Reasoning {index + 1}",
                    alternativeOrQuestion = $"Alternative or question {index + 1}",
                }),
                CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(5));
            dbContext.CollaborationMessages.Add(challenge);
            challengeIds.Add(challenge.Id);
        }

        await dbContext.SaveChangesAsync();

        return (reviewAttempt.Id, proposal.Id, challengeIds);
    }

    private Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        bool claimRun = false, bool codexObserved = false) =>
        SeedEligibleRunAsync(_factory, claimRun, codexObserved);

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid CheckpointId)> SeedEligibleRunAsync(
        WebApplicationFactory<Program> factory, bool claimRun = false, bool codexObserved = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Challenge resolution project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve the challenged ledger proposal", now);
        if (claimRun)
        {
            run.Claim(now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), MatchingFingerprint, []);
        dbContext.GitCheckpoints.Add(checkpoint);

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
        dbContext.RepositoryMutationLeases.Add(lease);

        await dbContext.SaveChangesAsync();

        if (codexObserved)
        {
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        return (run.Id, workspace.Id, checkpoint.Id);
    }
}

/// <summary>
/// The real Api host with two narrow test seams: a fixed <see cref="IGitWorkspaceEvidenceReader"/>
/// and the same set of background hosted services removed as
/// <c>ClaudeCriticalReviewApiWebApplicationFactory</c>, plus
/// <see cref="ChallengeResolutionSupervisor"/> — otherwise it would race a test's own
/// deterministic seeding of <c>Attempt</c> rows. Mirrors
/// <c>ClaudeCriticalReviewApiWebApplicationFactory</c> exactly.
/// </summary>
public sealed class ChallengeResolutionApiWebApplicationFactory : ApiWebApplicationFactory
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
            CodexPlanningTestHostSeams.RemoveHostedService<ChallengeResolutionSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}

/// <summary>
/// Same real host as <see cref="ChallengeResolutionApiWebApplicationFactory"/>, except its fixed
/// evidence reader reports a fingerprint that deliberately never matches any checkpoint a test
/// seeds — proving <c>CreateChallengeResolutionAttemptCommandHandler</c>'s fresh, pre-dispatch
/// evidence recheck produces "agent_attempts.checkpoint_not_current". Mirrors
/// <c>ClaudeCriticalReviewCheckpointDriftApiWebApplicationFactory</c>.
/// </summary>
public sealed class ChallengeResolutionCheckpointDriftApiWebApplicationFactory : ApiWebApplicationFactory
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
            CodexPlanningTestHostSeams.RemoveHostedService<ChallengeResolutionSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}
