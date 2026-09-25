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
/// Extends <see cref="AgentTokenUsageEndpointTests"/>'s own still-running/undispatched fail-closed
/// proof from provider-reported token usage to host-measured process-execution evidence: a tampered
/// row whose persisted <c>AgentProcessOutcome</c>/<c>AgentProcessExitCode</c>/<c>AgentProcessDuration</c>
/// already look well-formed must never surface through a per-role status endpoint, the run cockpit's
/// <c>latestAgentAttempt</c> projection, or the collaboration-evidence drill-down endpoint while the
/// attempt is still <see cref="AttemptStatus.Running"/> or was never dispatched. Neither shape is
/// reachable through this application's own recording path, so each row is produced by seeding a
/// genuine dispatched, terminal attempt through the real Domain API and then corrupting only its
/// status/dispatch columns via raw SQL — mirroring <c>AgentTokenUsageEndpointTests</c>'s own
/// tampering pattern.
/// </summary>
public sealed class AgentProcessExecutionEvidenceTrustEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task A_still_running_attempts_well_formed_process_fields_are_unknown_everywhere()
    {
        var (runId, attemptId, messageId) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE attempts
                   SET Status = {nameof(AttemptStatus.Running)}, AgentOutcome = NULL, CompletedAtUtc = NULL
                   WHERE Id = {attemptId}");
        }

        var statusBody = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using var status = JsonDocument.Parse(statusBody);
        AssertUnknownProcessEvidenceWithVisibleTimeout(status.RootElement.GetProperty("processExecution"));

        var cockpitBody = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var cockpit = JsonDocument.Parse(cockpitBody);
        AssertUnknownProcessEvidenceWithVisibleTimeout(cockpit.RootElement.GetProperty("latestAgentAttempt").GetProperty("processExecution"));

        var evidenceBody = await GetOkBodyAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        using var evidence = JsonDocument.Parse(evidenceBody);
        Assert.Equal("HasEvidence", evidence.RootElement.GetProperty("evidenceStatus").GetString());
        AssertUnknownProcessEvidenceWithVisibleTimeout(evidence.RootElement.GetProperty("processExecution"));
    }

    // The configured timeout is a separate, always-known-at-claim-time value, never gated behind
    // process-evidence trust: it stays visible even while the outcome/exitCode/duration fields are
    // now correctly reported as unknown for a tampered non-terminal row.
    private static void AssertUnknownProcessEvidenceWithVisibleTimeout(JsonElement processExecution)
    {
        Assert.Equal(JsonValueKind.Null, processExecution.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, processExecution.GetProperty("exitCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, processExecution.GetProperty("durationMilliseconds").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, processExecution.GetProperty("timeoutMilliseconds").ValueKind);
    }

    [Fact]
    public async Task An_undispatched_attempts_well_formed_process_fields_are_unknown_in_status_and_cockpit()
    {
        var (runId, attemptId, _) = await SeedLinkedProposalAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentDispatchedAtUtc = NULL WHERE Id = {attemptId}");
        }

        var statusBody = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using var status = JsonDocument.Parse(statusBody);
        AssertUnknownProcessEvidenceWithVisibleTimeout(status.RootElement.GetProperty("processExecution"));

        var cockpitBody = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var cockpit = JsonDocument.Parse(cockpitBody);
        AssertUnknownProcessEvidenceWithVisibleTimeout(cockpit.RootElement.GetProperty("latestAgentAttempt").GetProperty("processExecution"));
    }

    private async Task<string> GetOkBodyAsync(string path)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    // Mirrors GetCollaborationMessageEvidenceEndpointTests's own SeedLinkedProposalAsync shape
    // exactly (a Codex Planner attempt claiming Proposal), a proven combination that satisfies both
    // the collaboration-evidence handler's role/provider coherence check and the Proposal contract's
    // own content shape.
    private async Task<(Guid RunId, Guid AttemptId, Guid MessageId)> SeedLinkedProposalAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Process evidence trust", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Process evidence trust run", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);

        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
        attempt.MarkAgentDispatched(now.AddSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(3));

        dbContext.Attempts.Add(attempt);
        dbContext.CollaborationMessages.Add(message);
        await dbContext.SaveChangesAsync();

        return (run.Id, attempt.Id, message.Id);
    }
}
