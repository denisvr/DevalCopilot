using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>GET /api/runs/{runId}/collaboration-messages/{messageId}/evidence</c> against the
/// real Api host: authentication, the safe 404 for an unknown run/message pair, the explicit
/// <c>NoAgentEvidence</c> and <c>AttemptLinkBroken</c> bodies, the populated bounded evidence
/// projection for the exact linked attempt, the bounded artifact cap, and that the response never
/// exposes any excluded process/session/storage detail.
/// </summary>
public sealed class GetCollaborationMessageEvidenceEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/collaboration-messages/{Guid.NewGuid()}/evidence");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_an_incorrect_session_secret()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-real-secret");

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/collaboration-messages/{Guid.NewGuid()}/evidence");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_message()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/collaboration-messages/{Guid.NewGuid()}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("collaboration_messages.not_found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_when_the_message_belongs_to_a_different_run()
    {
        var (ownerRunId, _) = await SeedRunAsync();
        var (otherRunId, _) = await SeedRunAsync();
        var messageId = await SeedLinkedProposalAsync(ownerRunId);
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{otherRunId}/collaboration-messages/{messageId}/evidence");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_an_explicit_no_agent_evidence_body_for_an_attemptless_message()
    {
        var (runId, _) = await SeedRunAsync();
        Guid messageId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, null, "A simulated proposal", ProposalContent,
                CollaborationMessageProvenance.Simulated, now);
            messageId = message.Id;
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("NoAgentEvidence", root.GetProperty("evidenceStatus").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attemptId").ValueKind);
        Assert.Empty(root.GetProperty("artifacts").EnumerateArray());
    }

    [Fact]
    public async Task Returns_an_explicit_attempt_link_broken_body_when_the_linked_attempt_row_is_missing()
    {
        var (runId, _) = await SeedRunAsync();
        var messageId = await SeedLinkedProposalAsync(runId, out var attemptId);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM attempts WHERE Id = {attemptId}");
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("AttemptLinkBroken", document.RootElement.GetProperty("evidenceStatus").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("attemptId").ValueKind);
    }

    [Fact]
    public async Task Returns_an_explicit_attempt_link_broken_body_when_the_linked_attempts_role_is_incoherent()
    {
        var (runId, _) = await SeedRunAsync();
        var messageId = await SeedLinkedProposalAsync(runId, out var attemptId);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            // Corrupts only the persisted attempt row's own role — the message's own
            // ActorAgentRole (Planner, captured at construction) is untouched.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentRole = {nameof(AgentRole.Implementer)} WHERE Id = {attemptId}");
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("AttemptLinkBroken", document.RootElement.GetProperty("evidenceStatus").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("attemptId").ValueKind);
    }

    [Fact]
    public async Task Returns_the_bounded_evidence_for_the_exact_linked_attempt()
    {
        var (runId, _) = await SeedRunAsync();
        var messageId = await SeedLinkedProposalAsync(runId, out var attemptId);

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("HasEvidence", root.GetProperty("evidenceStatus").GetString());
        Assert.Equal(attemptId, root.GetProperty("attemptId").GetGuid());
        Assert.Equal(1, root.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Agent", root.GetProperty("attemptKind").GetString());
        Assert.Equal("Completed", root.GetProperty("attemptStatus").GetString());
        Assert.Equal("Codex", root.GetProperty("agentProvider").GetString());
        Assert.Equal("Planner", root.GetProperty("agentRole").GetString());
        Assert.Equal("Proposal", root.GetProperty("agentResponseContract").GetString());
        Assert.Equal("Proposed", root.GetProperty("agentOutcome").GetString());
        Assert.True(root.TryGetProperty("startingGitCheckpointId", out var startingCheckpoint));
        Assert.NotEqual(JsonValueKind.Null, startingCheckpoint.ValueKind);
        Assert.Equal((long)TimeSpan.FromMinutes(10).TotalMilliseconds, root.GetProperty("processExecution").GetProperty("timeoutMilliseconds").GetInt64());
        var artifact = Assert.Single(root.GetProperty("artifacts").EnumerateArray());
        Assert.Equal("AgentFinalResponse", artifact.GetProperty("purpose").GetString());
        Assert.False(root.GetProperty("artifactsOmitted").GetBoolean());
        Assert.Equal(1, root.GetProperty("artifactTotalCount").GetInt32());

        AssertNoExcludedDisclosure(body);
    }

    [Fact]
    public async Task Bounds_the_returned_artifact_rows_and_reports_the_real_total()
    {
        var (runId, _) = await SeedRunAsync();
        Guid messageId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
            var message = CollaborationMessage.RecordAgent(
                attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(3));
            messageId = message.Id;
            dbContext.Attempts.Add(attempt);
            dbContext.CollaborationMessages.Add(message);
            // The unique (AttemptId, Purpose) index means one attempt can hold at most one
            // artifact per ArtifactPurpose member — every currently defined purpose is seeded
            // here to exercise the endpoint's bounded cap (5) with real, distinct-purpose
            // persisted rows.
            var purposes = Enum.GetValues<ArtifactPurpose>();
            for (var i = 0; i < purposes.Length; i++)
            {
                dbContext.Artifacts.Add(Artifact.Record(
                    Guid.NewGuid(), runId, attempt.Id, purposes[i],
                    "text/plain", $@"runs\r\attempts\a\{i}.sealed", $"sha256:{i}", 10, false,
                    ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted,
                    now.AddSeconds(i)));
            }
            await dbContext.SaveChangesAsync();

            using var client = CreateAuthenticatedClient();
            var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Assert.Equal(5, root.GetProperty("artifacts").GetArrayLength());
            Assert.True(root.GetProperty("artifactsOmitted").GetBoolean());
            Assert.Equal(purposes.Length, root.GetProperty("artifactTotalCount").GetInt32());
        }
    }

    private static void AssertNoExcludedDisclosure(string body)
    {
        Assert.DoesNotContain("ProcessExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessArguments", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessWorkingDirectory", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessApprovedRoot", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProviderSessionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AdapterContractVersion", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RelativeStoragePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContentHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".sealed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(Guid RunId, Guid ProjectId)> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Evidence drill-down", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Evidence drill-down run", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return (run.Id, project.Id);
    }

    private Task<Guid> SeedLinkedProposalAsync(Guid runId) => SeedLinkedProposalAsync(runId, out _);

    private Task<Guid> SeedLinkedProposalAsync(Guid runId, out Guid attemptId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
        attempt.MarkAgentDispatched(now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(3));
        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), runId, attempt.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            @"runs\r\attempts\a\final.sealed", "sha256:final", 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
        dbContext.SaveChanges();
        attemptId = attempt.Id;
        return Task.FromResult(message.Id);
    }
}
