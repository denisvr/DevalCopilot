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

/// <summary>A stored turn-limit value that is not a canonical whole number in range (a fraction, a number too
/// large for an int, text, zero, a negative, or above the maximum) through the real HTTP pipeline: never a valid
/// invented request, never a disclosed value, and never a failure for a healthy sibling attempt or run.</summary>
public sealed class ClaudeMutationTurnLimitMalformedStorageEndpointTests(CodexPlanningApiWebApplicationFactory factory)
    : IClassFixture<CodexPlanningApiWebApplicationFactory>
{
    public static IEnumerable<object[]> Malformed =>
        [[3.5], [4294967297L], ["abc"], [0], [-3], [101], [new byte[] { 0x37 }], ["blob:37"], ["b:37"], [double.PositiveInfinity]];

    private static void AssertValueNotDisclosed(object stored, string body)
    {
        var text = stored is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : stored is double.PositiveInfinity ? "Inf" : Convert.ToString(stored, System.Globalization.CultureInfo.InvariantCulture)!;
        // A short value such as "0", "abc" or "3.5" can occur in unrelated JSON (a GUID, a timestamp), so only a long stored value is
        // searched for; the null member assertions in each test cover the short ones.
        if (text.Length >= 8)
        {
            Assert.DoesNotContain(text, body, StringComparison.Ordinal);
        }
    }

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<(Guid RunId, Guid HealthyAttemptId, Guid MalformedAttemptId)> SeedRunWithTwoAttemptsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var malformed = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Malformed storage", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(runId, project.Id, 1, "Implement", now);
        run.Claim(now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        var healthyAttempt = Attempt.ClaimAgentImplementationWithAssignment(
            healthy, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
            ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 9);
        healthyAttempt.MarkAgentDispatched(now);
        healthyAttempt.CompleteImplementation(AgentOutcome.NoChangesProduced, null, now.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(healthyAttempt);
        dbContext.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
            malformed, runId, 2, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 65536, 131072, now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
            ClaudeMutationAdapterContract.ImplementationV2, 2, requestedMaxTurns: 5));
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), healthy, Guid.NewGuid(), 0));
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), malformed, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return (runId, healthy, malformed);
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<JsonElement> GetOkAsync(string url)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task History_lists_the_healthy_sibling_and_marks_only_the_malformed_attempt_invalid(object stored)
    {
        var (runId, healthyId, malformedId) = await SeedRunWithTwoAttemptsAsync();
        await ExecuteAsync($"UPDATE attempts SET AgentRequestedMaxTurns = {stored} WHERE Id = {malformedId}");

        var history = await GetOkAsync($"/api/runs/{runId}/agent-attempts");
        var items = history.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("attemptId").GetGuid());

        Assert.True(items[healthyId].GetProperty("identityValid").GetBoolean());
        Assert.False(items[malformedId].GetProperty("identityValid").GetBoolean());
        AssertValueNotDisclosed(stored, history.GetRawText());

        var healthyEvidence = await GetOkAsync($"/api/runs/{runId}/agent-attempts/{healthyId}/evidence");
        var malformedEvidence = await GetOkAsync($"/api/runs/{runId}/agent-attempts/{malformedId}/evidence");
        Assert.True(healthyEvidence.GetProperty("identityValid").GetBoolean());
        Assert.Equal("Requested", healthyEvidence.GetProperty("maxTurns").GetProperty("state").GetString());
        Assert.Equal(9, healthyEvidence.GetProperty("maxTurns").GetProperty("maxTurns").GetInt32());
        Assert.False(malformedEvidence.GetProperty("identityValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, malformedEvidence.GetProperty("maxTurns").ValueKind);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task The_cockpit_reports_unknown_for_a_malformed_latest_attempt_and_the_status_fails_closed_without_the_value(object stored)
    {
        var (runId, _, malformedId) = await SeedRunWithTwoAttemptsAsync();
        await ExecuteAsync($"UPDATE attempts SET AgentRequestedMaxTurns = {stored} WHERE Id = {malformedId}");

        var cockpit = await GetOkAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal("Unknown", cockpit.GetProperty("latestAgentAttempt").GetProperty("maxTurns").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("latestAgentAttempt").GetProperty("maxTurns").GetProperty("maxTurns").ValueKind);

        using var client = CreateClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("agent_attempts.invalid_assignment", body, StringComparison.Ordinal);
        AssertValueNotDisclosed(stored, body);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task A_malformed_run_request_is_unknown_in_status_and_cockpit_and_never_breaks_a_healthy_run(object stored)
    {
        var (runId, _, _) = await SeedRunWithTwoAttemptsAsync();
        var (healthyRunId, _, _) = await SeedRunWithTwoAttemptsAsync();
        await ExecuteAsync($"UPDATE runs SET RequestedClaudeMaxTurns = {stored} WHERE Id = {runId}");

        var cockpit = await GetOkAsync($"/api/runs/{runId}/cockpit");
        var status = await GetOkAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var healthy = await GetOkAsync($"/api/runs/{healthyRunId}/cockpit");

        Assert.Equal("Unknown", cockpit.GetProperty("claudeMutationTurnLimit").GetProperty("state").GetString());
        Assert.Equal("Unknown", status.GetProperty("runTurnLimitRequest").GetProperty("state").GetString());
        Assert.Equal("NotRequested", healthy.GetProperty("claudeMutationTurnLimit").GetProperty("state").GetString());
        AssertValueNotDisclosed(stored, cockpit.GetRawText());
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Setting_a_valid_request_repairs_a_malformed_run_value_through_the_api(object stored)
    {
        var (runId, _, _) = await SeedRunWithTwoAttemptsAsync();
        await ExecuteAsync($"UPDATE runs SET RequestedClaudeMaxTurns = {stored} WHERE Id = {runId}");

        using var client = CreateClient();
        var response = await client.PostAsync(
            $"/api/runs/{runId}/claude-mutation-turn-limit",
            new StringContent("{\"maxTurns\":25}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cockpit = await GetOkAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal("Requested", cockpit.GetProperty("claudeMutationTurnLimit").GetProperty("state").GetString());
        Assert.Equal(25, cockpit.GetProperty("claudeMutationTurnLimit").GetProperty("maxTurns").GetInt32());
    }
}
