using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.Features.Runs.StartSimulatedRun;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0028 over the real protected HTTP operation and file-backed SQLite: the owner's optional smaller budgets are
/// persisted exactly, shown by the cockpit, and every invalid explicit value is refused without writing anything.</summary>
public sealed class CreateManualRunBudgetEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> RegisterProjectAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Budget intake", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json) =>
        client.PostAsync("/api/runs/manual", new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<(int Runs, int Events, int NextNumber)> CountsAsync(Guid projectId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var runIds = await db.Runs.Where(run => run.ProjectId == projectId).Select(run => run.Id).ToListAsync();
        return (
            runIds.Count,
            await db.Events.CountAsync(runEvent => runIds.Contains(runEvent.RunId)),
            await db.Projects.Where(project => project.Id == projectId).Select(project => project.NextExecutionNumber).SingleAsync());
    }

    private static async Task<GetRunCockpitResponse> CockpitAsync(HttpClient client, Guid runId) =>
        (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;

    [Theory]
    [InlineData(null, null, 16, 120)]
    [InlineData(4, null, 4, 120)]
    [InlineData(null, 40, 16, 40)]
    [InlineData(4, 40, 4, 40)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(16, 120, 16, 120)]
    public async Task Typed_requests_persist_each_budget_independently_and_the_cockpit_shows_them(
        int? attempts, int? minutes, int expectedAttempts, int expectedMinutes)
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await client.PostAsJsonAsync(
            "/api/runs/manual", new CreateManualRunRequest(projectId, "Budgeted", attempts, minutes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
        var cockpit = await CockpitAsync(client, created.RunId);
        Assert.Equal(expectedAttempts, cockpit.MaximumAgentAttempts);
        Assert.Equal(0, cockpit.AgentAttemptsUsed);
        Assert.False(cockpit.AgentInvocationTimeBudget.IsLegacyUnknown);
        Assert.Equal((long)TimeSpan.FromMinutes(expectedMinutes).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Equal((long)TimeSpan.FromMinutes(expectedMinutes).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.RemainingMilliseconds);
        Assert.Equal(0, cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
    }

    [Theory]
    [InlineData("""{"projectId":"{P}","objective":"Raw"}""", 16, 120)]
    [InlineData("""{"projectId":"{P}","objective":"Raw","maximumAgentAttempts":null,"maximumAgentInvocationMinutes":null}""", 16, 120)]
    [InlineData("""{"projectId":"{P}","objective":"Raw","maximumAgentAttempts":3}""", 3, 120)]
    [InlineData("""{"projectId":"{P}","objective":"Raw","maximumAgentInvocationMinutes":7}""", 16, 7)]
    [InlineData("""{"projectId":"{P}","objective":"Raw","maximumAgentAttempts":3,"maximumAgentInvocationMinutes":null}""", 3, 120)]
    public async Task Raw_json_with_omitted_or_null_members_selects_each_default_independently(
        string template, int expectedAttempts, int expectedMinutes)
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await PostRawAsync(client, template.Replace("{P}", projectId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
        var cockpit = await CockpitAsync(client, created.RunId);
        Assert.Equal(expectedAttempts, cockpit.MaximumAgentAttempts);
        Assert.Equal((long)TimeSpan.FromMinutes(expectedMinutes).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
    }

    [Theory]
    [InlineData("\"maximumAgentAttempts\":0")]
    [InlineData("\"maximumAgentAttempts\":-1")]
    [InlineData("\"maximumAgentAttempts\":17")]
    [InlineData("\"maximumAgentAttempts\":2147483648")]
    [InlineData("\"maximumAgentAttempts\":99999999999999999999")]
    [InlineData("\"maximumAgentAttempts\":3.5")]
    [InlineData("\"maximumAgentAttempts\":4.0")]
    [InlineData("\"maximumAgentAttempts\":1e1")]
    [InlineData("\"maximumAgentAttempts\":\"4\"")]
    [InlineData("\"maximumAgentAttempts\":true")]
    [InlineData("\"maximumAgentAttempts\":[4]")]
    [InlineData("\"maximumAgentInvocationMinutes\":0")]
    [InlineData("\"maximumAgentInvocationMinutes\":-5")]
    [InlineData("\"maximumAgentInvocationMinutes\":121")]
    [InlineData("\"maximumAgentInvocationMinutes\":2147483648")]
    [InlineData("\"maximumAgentInvocationMinutes\":1.5")]
    [InlineData("\"maximumAgentInvocationMinutes\":\"30\"")]
    [InlineData("\"maximumAgentInvocationMinutes\":false")]
    [InlineData("\"maximumAgentAttempts\":4,\"maximumAgentInvocationMinutes\":121")]
    [InlineData("\"maximumAgentAttempts\":17,\"maximumAgentInvocationMinutes\":30")]
    public async Task An_invalid_explicit_budget_is_refused_and_creates_nothing(string members)
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await PostRawAsync(client, $$"""{"projectId":"{{projectId}}","objective":"Refused",{{members}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((0, 0, 1), await CountsAsync(projectId));
    }

    [Fact]
    public async Task The_accepted_boundaries_are_inclusive_and_a_refusal_never_advances_the_execution_number()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var refused = await client.PostAsJsonAsync(
            "/api/runs/manual", new CreateManualRunRequest(projectId, "Over", 17, 121));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal((0, 0, 1), await CountsAsync(projectId));

        var accepted = await client.PostAsJsonAsync(
            "/api/runs/manual", new CreateManualRunRequest(projectId, "Limits", 16, 120));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, (await accepted.Content.ReadFromJsonAsync<CreateManualRunResponse>())!.ExecutionNumber);
        Assert.Equal((1, 1, 2), await CountsAsync(projectId));
    }

    [Fact]
    public async Task A_blocked_budgeted_creation_is_a_conflict_that_changes_nothing()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();
        (await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "First", 5, 50)))
            .EnsureSuccessStatusCode();

        var blocked = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "Second", 1, 1));

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("runs.intent_blocked", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal((1, 1, 2), await CountsAsync(projectId));
    }

    [Fact]
    public async Task The_simulated_operation_accepts_no_budget_and_keeps_the_fixed_defaults()
    {
        using var client = CreateAuthenticatedClient();
        var projectId = await RegisterProjectAsync();

        var response = await client.PostAsJsonAsync("/api/runs/simulated", new StartSimulatedRunRequest(projectId, "Demo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var runId = (await response.Content.ReadFromJsonAsync<StartSimulatedRunResponse>())!.RunId;
        var cockpit = await CockpitAsync(client, runId);
        Assert.Equal(16, cockpit.MaximumAgentAttempts);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
    }

    [Fact]
    public async Task Budgeted_creation_still_requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/runs/manual", new CreateManualRunRequest(Guid.NewGuid(), "Objective", 2, 20));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
