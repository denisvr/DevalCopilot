using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestImplementation;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// <c>POST …/agent-attempts/implementation</c> with optional direct human guidance: same route and response shape, the
/// guidance normalized, bounded and never echoed, recorded on the exact claimed attempt, and exposed by the status
/// projection. Run against the real HTTP pipeline and a real SQLite file.
/// </summary>
public sealed class RequestImplementationDirectGuidanceEndpointTests : IDisposable
{
    private const string Guidance = "Prefer the existing helper SENTINEL-5520.\nKeep the API unchanged.";

    private static readonly string Fingerprint = CodeReviewApiWebApplicationFactory.MatchingFingerprint;
    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string Url(Guid runId) => $"/api/runs/{runId}/agent-attempts/implementation";

    private static StringContent Raw(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<(Guid RunId, Guid ProposalId)> SeedAcceptedPlanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Implementation API", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the plan", now);
        run.Claim(now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, project.Id.ToByteArray(), now);
        var capability = await db.HostCapabilitySnapshots.SingleOrDefaultAsync(item => item.Capability == Capability.ClaudeCli);
        if (capability is null)
        {
            capability = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
            capability.MarkDispatched(now);
            capability.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
            db.HostCapabilitySnapshots.Add(capability);
        }
        else if (capability.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(capability.ResolvedExecutablePath))
        {
            capability.MarkDispatched(now);
            capability.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.0.0", now, now.AddMinutes(5));
        }

        var planning = Attempt.ClaimAgent(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 1);
        planning.MarkAgentDispatched(now);
        planning.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planning.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Implement the ledger.",
            JsonSerializer.Serialize(new { scope = "Ledger", implementationSteps = "Add the table.", risks = "None.", verificationPlan = "Tests.", escalationPoints = "None." }),
            CollaborationMessageProvenance.ProviderObserved, now);
        var review = Attempt.ClaimAgentCriticalReview(Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now, 2);
        review.MarkAgentDispatched(now);
        review.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted the plan.",
            JsonSerializer.Serialize(new { rationale = "The plan is complete." }), CollaborationMessageProvenance.ProviderObserved, now);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(lease);
        db.Attempts.AddRange(planning, review);
        db.CollaborationMessages.AddRange(proposal, acceptance);
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, proposal.Id, 0));
        await db.SaveChangesAsync();
        return (run.Id, proposal.Id);
    }

    private async Task<int> ImplementationAttemptCountAsync(Guid runId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Attempts
            .CountAsync(a => a.RunId == runId && a.AgentResponseContract == AgentResponseContract.ImplementationReport);
    }

    [Fact]
    public async Task A_guided_post_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new RequestImplementationRequest(Guid.NewGuid(), Guidance));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_guided_request_keeps_the_response_shape_never_echoes_the_text_and_is_projected_exactly()
    {
        var (runId, proposalId) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();

        var post = await client.PostAsync(
            Url(runId),
            Raw(JsonSerializer.Serialize(new { planProposalMessageId = proposalId, guidance = "  " + Guidance.Replace("\n", "\r\n", StringComparison.Ordinal) + "  " })));
        var body = await post.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        using var created = JsonDocument.Parse(body);
        Assert.Equal(["attemptId", "attemptNumber"], created.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var attemptId = created.RootElement.GetProperty("attemptId").GetGuid();

        var status = JsonDocument.Parse(await (await client.GetAsync(Url(runId))).Content.ReadAsStringAsync());
        Assert.Equal("Provided", status.RootElement.GetProperty("directGuidance").GetProperty("state").GetString());
        Assert.Equal(Guidance, status.RootElement.GetProperty("directGuidance").GetProperty("text").GetString());
        var evidence = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence")).Content.ReadAsStringAsync());
        Assert.Equal(Guidance, evidence.RootElement.GetProperty("directGuidance").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("omitted")]
    public async Task A_null_or_omitted_guidance_preserves_the_existing_behavior(string form)
    {
        var (runId, proposalId) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();
        var json = form == "null"
            ? $"{{\"planProposalMessageId\":\"{proposalId}\",\"guidance\":null}}"
            : $"{{\"planProposalMessageId\":\"{proposalId}\"}}";

        var post = await client.PostAsync(Url(runId), Raw(json));

        Assert.True(post.StatusCode == HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        var status = JsonDocument.Parse(await (await client.GetAsync(Url(runId))).Content.ReadAsStringAsync());
        Assert.Equal("NotRecorded", status.RootElement.GetProperty("directGuidance").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\\there SENTINEL-7101")]
    [InlineData("SENTINEL-7102 contains the api key marker")]
    public async Task Invalid_guidance_is_a_400_that_never_echoes_it_and_claims_nothing(string escapedGuidance)
    {
        var (runId, proposalId) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();

        var response = await client.PostAsync(
            Url(runId), Raw($"{{\"planProposalMessageId\":\"{proposalId}\",\"guidance\":\"{escapedGuidance}\"}}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("agent_attempts.direct_guidance_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        Assert.Equal(0, await ImplementationAttemptCountAsync(runId));
    }

    [Fact]
    public async Task Over_long_and_oversized_requests_are_rejected_before_any_claim()
    {
        var (runId, proposalId) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();

        var overlong = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(proposalId, new string('x', 601)));
        var oversized = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(proposalId, new string('x', 9 * 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.True(oversized.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode.ToString());
        Assert.Equal(0, await ImplementationAttemptCountAsync(runId));
    }

    [Fact]
    public async Task An_unresolved_plan_with_guidance_is_the_same_safe_conflict_as_without_it()
    {
        var (runId, _) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();

        var plain = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(Guid.NewGuid()));
        var guided = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(Guid.NewGuid(), Guidance));

        Assert.Equal(plain.StatusCode, guided.StatusCode);
        Assert.DoesNotContain("SENTINEL", await guided.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await ImplementationAttemptCountAsync(runId));
    }

    [Fact]
    public async Task A_second_guided_request_while_the_first_attempt_runs_is_a_conflict_that_leaves_the_first_snapshot_intact()
    {
        var (runId, proposalId) = await SeedAcceptedPlanAsync();
        using var client = AuthenticatedClient();
        var first = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(proposalId, Guidance));
        var second = await client.PostAsJsonAsync(Url(runId), new RequestImplementationRequest(proposalId, "SENTINEL-LATER different text"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var status = JsonDocument.Parse(await (await client.GetAsync(Url(runId))).Content.ReadAsStringAsync());
        Assert.Equal(Guidance, status.RootElement.GetProperty("directGuidance").GetProperty("text").GetString());
        Assert.Equal(1, await ImplementationAttemptCountAsync(runId));
    }
}
