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

/// <summary>The Claude turn-limit request facts across the implementation status, the review-correction status,
/// the cockpit, and the historical attempt evidence, through the real HTTP pipeline.</summary>
public sealed class ClaudeMutationTurnLimitProjectionEndpointTests(CodexPlanningApiWebApplicationFactory factory)
    : IClassFixture<CodexPlanningApiWebApplicationFactory>
{
    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedImplementationAsync(
        string contractVersion, int? maxTurns, int? runRequest = null, bool claimRun = false)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Turn limit", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(runId, project.Id, 1, "Implement", now);
        if (claimRun)
        {
            run.Claim(now);
        }

        run.SetRequestedClaudeMaxTurns(runRequest);
        var attempt = Attempt.ClaimAgentImplementationWithAssignment(
            attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
            contractVersion == ClaudeMutationAdapterContract.ImplementationV2 ? contractVersion : ClaudeMutationAdapterContract.ImplementationV1,
            1, contractVersion == ClaudeMutationAdapterContract.ImplementationV2 ? maxTurns : null);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return (runId, attemptId);
    }

    private async Task ExecuteSqlAsync(FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static void AssertFact(JsonElement fact, string state, int? maxTurns)
    {
        Assert.Equal(state, fact.GetProperty("state").GetString());
        if (maxTurns is { } value)
        {
            Assert.Equal(value, fact.GetProperty("maxTurns").GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, fact.GetProperty("maxTurns").ValueKind);
        }
    }

    [Fact]
    public async Task A_current_v2_attempt_with_a_request_reports_requested_for_the_attempt_and_the_runs_later_request_separately()
    {
        var (runId, _) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV2, 12, runRequest: 40);

        var root = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");

        AssertFact(root.GetProperty("attemptTurnLimit"), "Requested", 12);
        AssertFact(root.GetProperty("runTurnLimitRequest"), "Requested", 40);
        Assert.Equal("acceptEdits", root.GetProperty("configuredPermissionMode").GetString());
        Assert.Equal(ClaudeMutationAdapterContract.ImplementationV2, root.GetProperty("adapterContractVersion").GetString());
    }

    [Fact]
    public async Task A_coherent_v2_attempt_without_a_request_is_not_requested()
    {
        var (runId, _) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV2, null);

        var root = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");

        AssertFact(root.GetProperty("attemptTurnLimit"), "NotRequested", null);
        AssertFact(root.GetProperty("runTurnLimitRequest"), "NotRequested", null);
    }

    [Fact]
    public async Task A_legacy_v1_attempt_is_not_recorded_never_an_observed_unlimited_capacity_and_keeps_its_configured_facts()
    {
        var (runId, _) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV1, null);

        var root = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");

        AssertFact(root.GetProperty("attemptTurnLimit"), "NotRecorded", null);
        Assert.Equal("acceptEdits", root.GetProperty("configuredPermissionMode").GetString());
        Assert.DoesNotContain("unlimited", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_limit_stored_beside_a_v1_contract_is_unknown_and_discloses_no_number()
    {
        var (runId, attemptId) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV1, null);
        await ExecuteSqlAsync($"UPDATE attempts SET AgentRequestedMaxTurns = 61 WHERE Id = {attemptId}");

        var root = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");

        AssertFact(root.GetProperty("attemptTurnLimit"), "Unknown", null);
    }

    [Fact]
    public async Task A_malformed_stored_attempt_limit_fails_the_status_closed_without_disclosing_the_value()
    {
        var (runId, attemptId) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV2, 5);
        await ExecuteSqlAsync($"UPDATE attempts SET AgentRequestedMaxTurns = 987654 WHERE Id = {attemptId}");

        using var client = CreateClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("agent_attempts.invalid_assignment", body, StringComparison.Ordinal);
        Assert.DoesNotContain("987654", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_attempt_the_status_still_reports_the_runs_saved_request_and_a_null_attempt_fact()
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(Guid.NewGuid(), "No attempt", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, project.Id, 1, "Objective", now);
            run.SetRequestedClaudeMaxTurns(33);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            await dbContext.SaveChangesAsync();
        }

        var implementation = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var correction = await GetAsync($"/api/runs/{runId}/agent-attempts/review-correction");

        Assert.False(implementation.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, implementation.GetProperty("attemptTurnLimit").ValueKind);
        AssertFact(implementation.GetProperty("runTurnLimitRequest"), "Requested", 33);
        Assert.False(correction.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, correction.GetProperty("attemptTurnLimit").ValueKind);
        AssertFact(correction.GetProperty("runTurnLimitRequest"), "Requested", 33);
    }

    [Fact]
    public async Task A_malformed_stored_run_request_is_unknown_never_a_number()
    {
        var (runId, _) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV2, null);
        await ExecuteSqlAsync($"UPDATE runs SET RequestedClaudeMaxTurns = 555555 WHERE Id = {runId}");

        var status = await GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var cockpit = await GetAsync($"/api/runs/{runId}/cockpit");

        AssertFact(status.GetProperty("runTurnLimitRequest"), "Unknown", null);
        AssertFact(cockpit.GetProperty("claudeMutationTurnLimit"), "Unknown", null);
        Assert.DoesNotContain("555555", status.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("555555", cockpit.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cockpit_and_the_historical_evidence_project_the_attempts_immutable_fact_not_the_runs_current_request()
    {
        var (runId, attemptId) = await SeedImplementationAsync(
            ClaudeMutationAdapterContract.ImplementationV2, 9, runRequest: 70, claimRun: true);

        var cockpit = await GetAsync($"/api/runs/{runId}/cockpit");
        var evidence = await GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");

        AssertFact(cockpit.GetProperty("claudeMutationTurnLimit"), "Requested", 70);
        AssertFact(cockpit.GetProperty("latestAgentAttempt").GetProperty("maxTurns"), "Requested", 9);
        Assert.True(evidence.GetProperty("identityValid").GetBoolean());
        AssertFact(evidence.GetProperty("maxTurns"), "Requested", 9);
        Assert.DoesNotContain("adapterContractVersion", evidence.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Historical_evidence_shows_not_recorded_for_a_legacy_attempt_and_no_fact_when_the_identity_is_invalid()
    {
        var (legacyRunId, legacyAttemptId) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV1, null);
        var (badRunId, badAttemptId) = await SeedImplementationAsync(ClaudeMutationAdapterContract.ImplementationV2, 5);
        await ExecuteSqlAsync($"UPDATE attempts SET AgentRequestedMaxTurns = 400 WHERE Id = {badAttemptId}");

        var legacy = await GetAsync($"/api/runs/{legacyRunId}/agent-attempts/{legacyAttemptId}/evidence");
        var bad = await GetAsync($"/api/runs/{badRunId}/agent-attempts/{badAttemptId}/evidence");

        AssertFact(legacy.GetProperty("maxTurns"), "NotRecorded", null);
        Assert.False(bad.GetProperty("identityValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, bad.GetProperty("maxTurns").ValueKind);
        Assert.DoesNotContain("400", bad.GetProperty("maxTurns").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attempt_that_is_not_a_claude_mutation_attempt_carries_no_turn_limit_fact()
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(Guid.NewGuid(), "Critical review", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, project.Id, 1, "Objective", now);
            run.Claim(now);
            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(Attempt.ClaimAgentCriticalReviewWithModelRequest(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, null, null, agentBudgetSlot: 1));
            await dbContext.SaveChangesAsync();
        }

        var cockpit = await GetAsync($"/api/runs/{runId}/cockpit");
        var evidence = await GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");

        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("latestAgentAttempt").GetProperty("maxTurns").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("maxTurns").ValueKind);
    }
}
