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

public sealed class GetImplementationAttemptStatusEndpointTests(CodexPlanningApiWebApplicationFactory factory)
    : IClassFixture<CodexPlanningApiWebApplicationFactory>
{
    [Fact]
    public async Task Corrupt_assignment_metadata_returns_a_safe_failure_without_mutating_or_disclosing_it()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var corruptSentinel = new string('z', 129);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Projects.Add(Project.Register(projectId, "Corrupt assignment", @"C:\repos\corrupt", now));
            dbContext.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Check assignment", now));
            dbContext.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now));
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentRequestedModel = {corruptSentinel} WHERE Id = {attemptId}");
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("agent_attempts.invalid_assignment", body, StringComparison.Ordinal);
        Assert.DoesNotContain(corruptSentinel, body, StringComparison.Ordinal);
        using var verifyScope = factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedValue = await verifyContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.Id == attemptId)
            .Select(attempt => attempt.AgentRequestedModel)
            .SingleAsync();
        Assert.Equal(corruptSentinel, persistedValue);
    }

    [Fact]
    public async Task Returns_bounded_authoritative_assignment_metadata_without_execution_secrets()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var requestedModel = "claude-model-requested";
        var observedModel = "claude-model-observed";
        var requestedEffort = "high-requested";
        var observedEffort = "medium-observed";
        var contractVersion = "claude-implementation-v1";
        var forbiddenSentinel = "must-not-be-in-response";

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(projectId, "Assignment status", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, projectId, 1, "Implement assigned proposal", now);
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, requestedModel, requestedEffort,
                AgentPermissionProfile.WorkspaceEditOnly, contractVersion);
            attempt.MarkAgentDispatched(now);
            attempt.RecordAgentObservedAssignment(observedModel, observedEffort);
            attempt.CompleteImplementation(AgentOutcome.NoChangesProduced, null, now.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit);

            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposalId, 0));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.True(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(proposalId, root.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal("ClaudeCode", root.GetProperty("provider").GetString());
        Assert.Equal("Implementer", root.GetProperty("role").GetString());
        Assert.Equal(requestedModel, root.GetProperty("requestedModel").GetString());
        Assert.Equal(observedModel, root.GetProperty("observedModel").GetString());
        Assert.Equal(requestedEffort, root.GetProperty("requestedEffort").GetString());
        Assert.Equal(observedEffort, root.GetProperty("observedEffort").GetString());
        Assert.Equal("WorkspaceEditOnly", root.GetProperty("permissionProfile").GetString());
        Assert.Equal(contractVersion, root.GetProperty("adapterContractVersion").GetString());

        Assert.DoesNotContain(forbiddenSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\repos\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript", body, StringComparison.OrdinalIgnoreCase);
    }
}
