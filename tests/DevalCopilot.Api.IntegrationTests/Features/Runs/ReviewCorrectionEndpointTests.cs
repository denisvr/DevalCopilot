using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestReviewCorrection;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class ReviewCorrectionEndpointTests : IDisposable
{
    private static readonly string Fingerprint = CodeReviewApiWebApplicationFactory.MatchingFingerprint;
    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Post_requires_authentication()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/review-correction", new RequestReviewCorrectionRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_authentication()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/review-correction");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_run_returns_not_found_without_sensitive_details()
    {
        using var client = AuthenticatedClient();
        var post = await client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/review-correction", new RequestReviewCorrectionRequest(Guid.NewGuid()));
        var get = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/agent-attempts/review-correction");
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        AssertNoDisclosure(await post.Content.ReadAsStringAsync());
        AssertNoDisclosure(await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_returns_problem_details_for_a_non_running_run()
    {
        var seed = await SeedCorrectionChainAsync(claimRun: false);
        using var client = AuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/runs/{seed.RunId}/agent-attempts/review-correction", new RequestReviewCorrectionRequest(seed.ReviewId));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("runs.not_running", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Post_success_returns_identity_only_and_get_exposes_safe_false_then_true_shapes()
    {
        var seed = await SeedCorrectionChainAsync();
        using var client = AuthenticatedClient();
        var before = await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/review-correction");
        using (var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            Assert.False(beforeJson.RootElement.GetProperty("hasAttempt").GetBoolean());
            Assert.Equal(JsonValueKind.Null, beforeJson.RootElement.GetProperty("attemptId").ValueKind);
            Assert.Empty(beforeJson.RootElement.GetProperty("artifacts").EnumerateArray());
        }

        var post = await client.PostAsJsonAsync($"/api/runs/{seed.RunId}/agent-attempts/review-correction", new RequestReviewCorrectionRequest(seed.ReviewId));
        var postBody = await post.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        AssertNoDisclosure(postBody);
        using (var postJson = JsonDocument.Parse(postBody))
        {
            Assert.NotEqual(Guid.Empty, postJson.RootElement.GetProperty("attemptId").GetGuid());
            Assert.Equal(5, postJson.RootElement.GetProperty("attemptNumber").GetInt32());
        }

        var after = await client.GetAsync($"/api/runs/{seed.RunId}/agent-attempts/review-correction");
        var afterBody = await after.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterJson = JsonDocument.Parse(afterBody);
        Assert.True(afterJson.RootElement.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(seed.ReviewId, afterJson.RootElement.GetProperty("implementationReviewAttemptId").GetGuid());
        Assert.Equal("Running", afterJson.RootElement.GetProperty("status").GetString());
        Assert.Single(afterJson.RootElement.GetProperty("artifacts").EnumerateArray());
        AssertNoDisclosure(afterBody);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<(Guid RunId, Guid ReviewId)> SeedCorrectionChainAsync(bool claimRun = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Review correction API", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Correct the implementation", now);
        if (claimRun) run.Claim(now);
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

        var planning = Attempt.ClaimAgent(Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now);
        planning.MarkAgentDispatched(now);
        planning.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now);
        var proposal = CollaborationMessage.Record(Guid.NewGuid(), run.Id, planning.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Implement the requested correction.", JsonSerializer.Serialize(new { scope = "Correction", implementationSteps = "Apply the findings.", risks = "None known.", verificationPlan = "Run tests.", escalationPoints = "None." }), CollaborationMessageProvenance.ProviderObserved, now);
        var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now);
        acceptanceAttempt.MarkAgentDispatched(now);
        acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now);
        var acceptance = CollaborationMessage.Record(Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, proposal.Id,
            "Accepted the implementation plan.", JsonSerializer.Serialize(new { rationale = "The plan is complete." }), CollaborationMessageProvenance.ProviderObserved, now);
        var implementation = Attempt.ClaimAgentImplementation(Guid.NewGuid(), run.Id, 3, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now);
        implementation.MarkAgentDispatched(now);
        implementation.CompleteImplementation(AgentOutcome.Implemented, checkpoint.Id, now);
        var report = CollaborationMessage.Record(Guid.NewGuid(), run.Id, implementation.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id,
            "Implemented the requested correction.", JsonSerializer.Serialize(new { completedWork = "Updated the implementation.", verification = "Tests passed." }), CollaborationMessageProvenance.ProviderObserved, now);
        var review = Attempt.ClaimAgentCodeReview(Guid.NewGuid(), run.Id, 4, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, now);
        review.MarkAgentDispatched(now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Fingerprint, now);
        var finding = CollaborationMessage.Record(Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding, report.Id,
            "The implementation needs a bounded correction.", JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The branch is incomplete.", requiredChange = "Complete the branch." }), CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(1));

        db.Projects.Add(project); db.Runs.Add(run); db.GitWorkspaces.Add(workspace); db.GitCheckpoints.Add(checkpoint); db.RepositoryMutationLeases.Add(lease);
        db.Attempts.AddRange(planning, acceptanceAttempt, implementation, review); db.CollaborationMessages.AddRange(proposal, acceptance, report, finding);
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0));
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, proposal.Id, 0));
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementation.Id, acceptance.Id, 1));
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, report.Id, 0));
        db.SaveChanges();
        return (run.Id, review.Id);
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain("resolvedExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contentHash", body, StringComparison.OrdinalIgnoreCase);
    }
}
