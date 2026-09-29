using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestCodexPlanningAttempt;
using DevalCopilot.Api.Features.Runs.RequestCodexPlanningRepairAttempt;
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
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/{sourceAttemptId}/codex-plan-repair</c>
/// against the real Api host: authentication, the safe fixed errors for every ineligible source,
/// the golden path, lineage in the Planner status, the still-available ordinary request, and that
/// nothing from the source's sealed response, artifact locations, or the workspace leaks. Each test
/// owns its factory because the Codex capability snapshot is one host-scoped row.
/// </summary>
public sealed class RequestCodexPlanningRepairAttemptEndpointTests : IDisposable
{
    private const string SealedResponsePath = @"runs\repair-secret-run\attempts\a\final.sealed";
    private const string SealedResponseHash = "sha256:repair-secret-hash";
    private const string WorkspacePath = @"C:\workspaces\repair-secret-workspace";

    private static readonly string Fingerprint = CodexPlanningApiWebApplicationFactory.MatchingFingerprint;

    private readonly CodexPlanningApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string RepairUrl(Guid runId, Guid sourceAttemptId) =>
        $"/api/runs/{runId}/agent-attempts/{sourceAttemptId}/codex-plan-repair";

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(RepairUrl(Guid.NewGuid(), Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync(RepairUrl(Guid.NewGuid(), Guid.NewGuid()), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_the_same_safe_not_found_for_an_unknown_source_and_another_runs_source()
    {
        var (runId, _) = await SeedRunWithInvalidSourceAsync();
        var (_, foreignSourceId) = await SeedRunWithInvalidSourceAsync(observeCodex: false);
        using var client = CreateAuthenticatedClient();

        var unknown = await client.PostAsync(RepairUrl(runId, Guid.NewGuid()), content: null);
        var foreign = await client.PostAsync(RepairUrl(runId, foreignSourceId), content: null);

        foreach (var response in new[] { unknown, foreign })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("agent_attempts.repair_source_not_found", body, StringComparison.Ordinal);
            AssertNoDisclosure(body);
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(1, await dbContext.Attempts.CountAsync(a => a.RunId == runId));
    }

    [Fact]
    public async Task Succeeds_with_the_new_attempt_identity_and_source_only_and_links_the_repair()
    {
        var (runId, sourceId) = await SeedRunWithInvalidSourceAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync(RepairUrl(runId, sourceId), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonSerializer.Deserialize<RequestCodexPlanningRepairAttemptResponse>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.AttemptNumber);
        Assert.Equal(sourceId, payload.RepairSourceAttemptId);
        Assert.Equal(3, JsonDocument.Parse(body).RootElement.EnumerateObject().Count());
        AssertNoDisclosure(body);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await dbContext.Attempts.SingleAsync(a => a.Id == payload.AttemptId);
        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AgentRole.Planner, repair.AgentRole);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.RunId == runId));
    }

    [Fact]
    public async Task A_second_repair_of_the_same_source_and_a_repair_of_the_repair_are_safe_conflicts()
    {
        var (runId, sourceId) = await SeedRunWithInvalidSourceAsync();
        using var client = CreateAuthenticatedClient();
        var first = await client.PostAsync(RepairUrl(runId, sourceId), content: null);
        var repairId = (await first.Content.ReadFromJsonAsync<RequestCodexPlanningRepairAttemptResponse>())!.AttemptId;

        var again = await client.PostAsync(RepairUrl(runId, sourceId), content: null);
        var ofRepair = await client.PostAsync(RepairUrl(runId, repairId), content: null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var againBody = await again.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.repair_already_requested", againBody, StringComparison.Ordinal);
        AssertNoDisclosure(againBody);
        Assert.Equal(HttpStatusCode.Conflict, ofRepair.StatusCode);
        var ofRepairBody = await ofRepair.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.repair_of_repair_forbidden", ofRepairBody, StringComparison.Ordinal);
        AssertNoDisclosure(ofRepairBody);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(2, await dbContext.Attempts.CountAsync(a => a.RunId == runId));
    }

    [Fact]
    public async Task Keeps_the_ordinary_planning_request_available_and_unlinked()
    {
        var (runId, sourceId) = await SeedRunWithInvalidSourceAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync($"/api/runs/{runId}/agent-attempts/codex-plan", content: null);
        var payload = await response.Content.ReadFromJsonAsync<RequestCodexPlanningAttemptResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var ordinary = await dbContext.Attempts.SingleAsync(a => a.Id == payload!.AttemptId);
        Assert.Null(ordinary.AgentRepairSourceAttemptId);

        // The source is no longer the latest Agent attempt, so a repair of it is now refused.
        var late = await client.PostAsync(RepairUrl(runId, sourceId), content: null);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Contains("agent_attempts.repair_source_not_latest", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planner_status_reports_lineage_only_after_a_repair_and_never_discloses_the_source_response()
    {
        var (runId, sourceId) = await SeedRunWithInvalidSourceAsync();
        using var client = CreateAuthenticatedClient();

        var before = await client.GetStringAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using (var beforeDocument = JsonDocument.Parse(before))
        {
            Assert.Equal("InvalidStructuredOutput", beforeDocument.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, beforeDocument.RootElement.GetProperty("repairSourceAttemptId").ValueKind);
            Assert.Equal(JsonValueKind.Null, beforeDocument.RootElement.GetProperty("repairSourceAttemptNumber").ValueKind);
        }

        await client.PostAsync(RepairUrl(runId, sourceId), content: null);
        var after = await client.GetStringAsync($"/api/runs/{runId}/agent-attempts/codex-plan");

        using var afterDocument = JsonDocument.Parse(after);
        Assert.Equal(2, afterDocument.RootElement.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(sourceId, afterDocument.RootElement.GetProperty("repairSourceAttemptId").GetGuid());
        Assert.Equal(1, afterDocument.RootElement.GetProperty("repairSourceAttemptNumber").GetInt32());
        AssertNoDisclosure(before);
        AssertNoDisclosure(after);
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain(SealedResponsePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain("repair-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SealedResponseHash, body, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspacePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\safe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    private async Task<(Guid RunId, Guid SourceAttemptId)> SeedRunWithInvalidSourceAsync(bool observeCodex = true)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Repair project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", now);
        run.Claim(now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, WorkspacePath + Guid.NewGuid().ToString("N"), "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);

        var source = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
        source.MarkAgentDispatched(now);
        source.CompleteAgent(
            AgentOutcome.InvalidStructuredOutput, Fingerprint, now,
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1)));

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.Add(source);
        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, source.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            SealedResponsePath, SealedResponseHash, 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
        await dbContext.SaveChangesAsync();

        if (observeCodex)
        {
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        return (run.Id, source.Id);
    }
}
