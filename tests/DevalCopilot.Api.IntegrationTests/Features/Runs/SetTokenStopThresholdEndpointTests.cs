using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class SetTokenStopThresholdEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/token-stop-threshold";

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
        run.Claim(now);
        if (terminal)
        {
            run.Complete(now.AddMinutes(1));
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static async Task<JsonElement> GetStopAsync(HttpClient client, Guid runId, string provider)
    {
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.GetProperty("tokenStops").EnumerateArray().Single(entry => entry.GetProperty("provider").GetString() == provider).Clone();
    }

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { provider = "Codex", thresholdTokens = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_credential_returns_401_and_writes_nothing()
    {
        var factory = new ApiWebApplicationFactory();
        var runId = await SeedRunAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        var response = await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Null((await dbContext.Runs.AsNoTracking().SingleAsync(item => item.Id == runId)).CodexTokenStopThreshold);
    }

    [Fact]
    public async Task A_missing_run_returns_404()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { provider = "Codex", thresholdTokens = 5 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Codex", 0L)]
    [InlineData("Codex", -5L)]
    [InlineData("ClaudeCode", Run.MaxTokenStopThreshold + 1)]
    [InlineData("codex", 5L)]
    [InlineData("Gemini", 5L)]
    [InlineData("", 5L)]
    public async Task An_invalid_provider_or_threshold_returns_400_and_writes_nothing(string provider, long threshold)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { provider, thresholdTokens = threshold });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var run = await dbContext.Runs.AsNoTracking().SingleAsync(item => item.Id == runId);
        Assert.Null(run.CodexTokenStopThreshold);
        Assert.Null(run.ClaudeTokenStopThreshold);
        Assert.Empty(dbContext.Events.Where(item => item.EventType == RunEventType.TokenStopThresholdChanged));
    }

    [Fact]
    public async Task A_threshold_beyond_the_signed_64_bit_range_is_rejected_at_the_boundary()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        using var content = new StringContent("{\"provider\":\"Codex\",\"thresholdTokens\":99999999999999999999999}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Route(runId), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Each_provider_persists_independently_and_is_projected_separately_from_the_advisory_warning()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var codex = await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = 1200 });
        var claude = await client.PostAsJsonAsync(Route(runId), new { provider = "ClaudeCode", thresholdTokens = 500_000 });

        Assert.Equal(HttpStatusCode.OK, codex.StatusCode);
        Assert.Equal(HttpStatusCode.OK, claude.StatusCode);
        using var body = JsonDocument.Parse(await claude.Content.ReadAsStringAsync());
        Assert.Equal("ClaudeCode", body.RootElement.GetProperty("provider").GetString());
        Assert.Equal(500_000, body.RootElement.GetProperty("thresholdTokens").GetInt64());

        var codexStop = await GetStopAsync(client, runId, "Codex");
        Assert.Equal(1200, codexStop.GetProperty("thresholdTokens").GetInt64());
        Assert.Equal("NoDispatchedHistory", codexStop.GetProperty("state").GetString());
        Assert.False(codexStop.GetProperty("claimBlocked").GetBoolean());
        var claudeStop = await GetStopAsync(client, runId, "ClaudeCode");
        Assert.Equal(500_000, claudeStop.GetProperty("thresholdTokens").GetInt64());

        // The advisory warning is a different setting: setting a stop never configures it.
        var cockpit = JsonDocument.Parse(await (await client.GetAsync($"/api/runs/{runId}/cockpit")).Content.ReadAsStringAsync()).RootElement;
        Assert.All(
            cockpit.GetProperty("tokenWarnings").EnumerateArray(),
            entry => Assert.Equal("NotConfigured", entry.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Clearing_returns_null_and_the_cockpit_reports_not_configured()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        (await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = 10 })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = (long?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("thresholdTokens").ValueKind);
        var stop = await GetStopAsync(client, runId, "Codex");
        Assert.Equal("NotConfigured", stop.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, stop.GetProperty("thresholdTokens").ValueKind);
    }

    [Fact]
    public async Task A_terminal_run_is_rejected_without_a_partial_write()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory, terminal: true);

        var response = await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = 10 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Null((await dbContext.Runs.AsNoTracking().SingleAsync(item => item.Id == runId)).CodexTokenStopThreshold);
        Assert.Empty(dbContext.Events.Where(item => item.EventType == RunEventType.TokenStopThresholdChanged));
    }

    [Fact]
    public async Task The_cockpit_blocks_at_exact_equality_from_recorded_evidence_and_re_evaluates_when_the_threshold_changes()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            attempt.MarkAgentDispatched(now);
            attempt.CompleteAgent(
                AgentOutcome.Accepted, new string('a', 64), now, processEvidence: TestProcessEvidence.CleanExit,
                tokenUsage: AgentTokenUsageEvidence.Create(500, 50, 5, 60, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion));
            dbContext.Attempts.Add(attempt);
            await dbContext.SaveChangesAsync();
        }

        (await client.PostAsJsonAsync(Route(runId), new { provider = "ClaudeCode", thresholdTokens = 616 })).EnsureSuccessStatusCode();
        var below = await GetStopAsync(client, runId, "ClaudeCode");
        Assert.Equal("BelowThresholdComplete", below.GetProperty("state").GetString());
        Assert.Equal(615, below.GetProperty("knownTokenCount").GetInt64());
        Assert.False(below.GetProperty("claimBlocked").GetBoolean());

        (await client.PostAsJsonAsync(Route(runId), new { provider = "ClaudeCode", thresholdTokens = 615 })).EnsureSuccessStatusCode();
        var equal = await GetStopAsync(client, runId, "ClaudeCode");
        Assert.Equal("ThresholdReached", equal.GetProperty("state").GetString());
        Assert.True(equal.GetProperty("claimBlocked").GetBoolean());
        Assert.Equal(1, equal.GetProperty("countedAttempts").GetInt32());
    }

    [Fact]
    public async Task Each_change_records_one_event_with_only_the_provider_and_threshold()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        (await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = 42 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Route(runId), new { provider = "Codex", thresholdTokens = (long?)null })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var payloads = await dbContext.Events.AsNoTracking()
            .Where(item => item.RunId == runId && item.EventType == RunEventType.TokenStopThresholdChanged)
            .OrderBy(item => item.Sequence)
            .Select(item => item.PayloadJson)
            .ToListAsync();
        Assert.Equal(
            ["{\"provider\":\"Codex\",\"thresholdTokens\":42}", "{\"provider\":\"Codex\",\"thresholdTokens\":null}"],
            payloads);
    }
}
