using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class SetClaudeModelPreferenceEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/claude-model-preference";

    private static HttpClient CreateAuthenticatedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static async Task<Guid> SeedRunAsync(ApiWebApplicationFactory factory, bool terminal = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", now);
        if (terminal)
        {
            run.Claim(now);
            run.Complete(now.AddMinutes(1));
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static async Task<JsonElement> GetCockpitAsync(HttpClient client, Guid runId)
    {
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { requestedModel = "opus" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_run_returns_404()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { requestedModel = "opus" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Opus")]
    [InlineData("fable")]
    [InlineData("claude-opus-5-5")]
    [InlineData("")]
    public async Task An_alias_outside_the_closed_set_returns_400_and_writes_nothing(string alias)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = alias });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Null((await dbContext.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).RequestedClaudeModel);
        Assert.Empty(dbContext.Events.Where(item => item.EventType == RunEventType.ClaudeModelPreferenceChanged));
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("haiku")]
    public async Task Setting_an_alias_persists_it_records_one_event_and_projects_it_as_a_request_only(string alias)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = alias });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(alias, document.RootElement.GetProperty("requestedModel").GetString());

        var cockpit = await GetCockpitAsync(client, runId);
        Assert.Equal(alias, cockpit.GetProperty("requestedClaudeModel").GetString());
        // The Codex preference is a separate fact and is never touched by this operation.
        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("requestedCodexModel").ValueKind);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var recorded = await dbContext.Events.AsNoTracking()
            .SingleAsync(item => item.RunId == runId && item.EventType == RunEventType.ClaudeModelPreferenceChanged);
        Assert.Equal("{\"requestedModel\":\"" + alias + "\"}", recorded.PayloadJson);
    }

    [Fact]
    public async Task Clearing_the_alias_returns_null_and_the_cockpit_reports_no_request()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        (await client.PostAsJsonAsync(Route(runId), new { requestedModel = "opus" })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("requestedModel").ValueKind);
        var cockpit = await GetCockpitAsync(client, runId);
        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("requestedClaudeModel").ValueKind);
    }

    [Fact]
    public async Task A_terminal_run_is_rejected_without_a_partial_write()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory, terminal: true);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "opus" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Null((await dbContext.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).RequestedClaudeModel);
        Assert.Empty(dbContext.Events.Where(item => item.EventType == RunEventType.ClaudeModelPreferenceChanged));
    }

    [Fact]
    public async Task A_run_without_any_request_projects_null_for_both_the_run_and_its_latest_attempt()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var cockpit = await GetCockpitAsync(client, runId);

        Assert.Equal(JsonValueKind.Null, cockpit.GetProperty("requestedClaudeModel").ValueKind);
    }

    [Fact]
    public async Task The_cockpit_projects_the_latest_attempts_own_immutable_request_not_the_runs_current_one()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.Claim(now);
            var attempt = Attempt.ClaimAgentCriticalReviewWithModelRequest(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, ClaudeModelAlias.Sonnet, agentBudgetSlot: 1);
            dbContext.Attempts.Add(attempt);
            await dbContext.SaveChangesAsync();
        }

        (await client.PostAsJsonAsync(Route(runId), new { requestedModel = "haiku" })).EnsureSuccessStatusCode();

        var cockpit = await GetCockpitAsync(client, runId);
        Assert.Equal("haiku", cockpit.GetProperty("requestedClaudeModel").GetString());
        var latest = cockpit.GetProperty("latestAgentAttempt");
        Assert.Equal("sonnet", latest.GetProperty("requestedModel").GetString());
        Assert.False(latest.TryGetProperty("observedModel", out _));
    }
}
