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

/// <summary>The run-scoped advisory Codex account-usage warning (ADR-0026) through the real authenticated MVC pipeline: one protected
/// operation with a strict one-member body bounded to 8 KiB, one atomic human event, stable safe errors, concurrent writes that stay
/// truthful, and the cockpit's separate saved-setting fact, which states a threshold and nothing about the account.</summary>
public sealed class SetCodexAccountUsageWarningEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/codex-account-usage-warning";

    private static HttpClient CreateAuthenticatedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static async Task<Guid> SeedRunAsync(
        ApiWebApplicationFactory factory, bool terminal = false, RunExecutionMode mode = RunExecutionMode.ManualAgent)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, 1, mode, "Objective", now);
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

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, Guid runId, string json) =>
        client.PostAsync(Route(runId), new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> GetCockpitAsync(HttpClient client, Guid runId)
    {
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<CodexAccountUsageWarningReading> StoredAsync(ApiWebApplicationFactory factory, Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (await dbContext.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).ReadCodexAccountUsageWarningPercent();
    }

    private static async Task<int> EventCountAsync(ApiWebApplicationFactory factory, Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await dbContext.Events.CountAsync(item => item.RunId == runId && item.EventType == RunEventType.CodexAccountUsageWarningChanged);
    }

    [Fact]
    public async Task An_absent_or_wrong_credential_returns_401_for_both_operations_and_writes_nothing()
    {
        var factory = new ApiWebApplicationFactory();
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");
        var runId = await SeedRunAsync(factory);

        var first = await anonymous.PostAsJsonAsync(Route(runId), new { percent = 50 });
        var second = await wrong.PostAsJsonAsync(Route(runId), new { percent = 50 });
        var third = await anonymous.GetAsync(Route(runId));
        var fourth = await wrong.GetAsync(Route(runId));

        Assert.All(new[] { first, second, third, fourth }, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal(0, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_missing_run_returns_404()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { percent = 50 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(100)]
    public async Task Setting_a_percentage_persists_it_records_one_human_event_and_projects_a_threshold_only(int percent)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { percent });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(percent, document.RootElement.GetProperty("percent").GetInt32());
        Assert.Equal(percent, (await StoredAsync(factory, runId)).Value);

        var cockpit = await GetCockpitAsync(client, runId);
        var fact = cockpit.GetProperty("codexAccountUsageWarning");
        Assert.Equal("Configured", fact.GetProperty("state").GetString());
        Assert.Equal(percent, fact.GetProperty("percent").GetInt32());
        Assert.Equal(["state", "percent"], fact.EnumerateObject().Select(member => member.Name));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var recorded = await dbContext.Events.AsNoTracking()
            .SingleAsync(item => item.RunId == runId && item.EventType == RunEventType.CodexAccountUsageWarningChanged);
        Assert.Equal("{\"percent\":" + percent + "}", recorded.PayloadJson);
        Assert.Equal(ParticipantKind.Human, recorded.Actor.Kind);
        Assert.Null(recorded.AttemptId);
    }

    [Fact]
    public async Task A_new_run_is_not_configured_and_an_explicit_null_clears_a_saved_warning()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var initial = (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageWarning");
        Assert.Equal("NotConfigured", initial.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("percent").ValueKind);

        (await client.PostAsJsonAsync(Route(runId), new { percent = 60 })).EnsureSuccessStatusCode();
        var cleared = await PostRawAsync(client, runId, "{\"percent\":null}");

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal("NotConfigured", (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageWarning").GetProperty("state").GetString());
        Assert.Equal(2, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task The_warning_and_the_stop_are_saved_independently_with_no_required_ordering()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        (await client.PostAsJsonAsync($"/api/runs/{runId}/codex-account-usage-stop", new { percent = 20 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Route(runId), new { percent = 95 })).EnsureSuccessStatusCode();
        var cockpit = await GetCockpitAsync(client, runId);

        Assert.Equal(20, cockpit.GetProperty("codexAccountUsageStop").GetProperty("percent").GetInt32());
        Assert.Equal(95, cockpit.GetProperty("codexAccountUsageWarning").GetProperty("percent").GetInt32());

        (await client.PostAsJsonAsync(Route(runId), new { percent = 5 })).EnsureSuccessStatusCode();
        Assert.Equal(20, (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageStop").GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task A_same_value_set_is_accepted_and_still_records_its_event()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        (await client.PostAsJsonAsync(Route(runId), new { percent = 8 })).EnsureSuccessStatusCode();
        var again = await client.PostAsJsonAsync(Route(runId), new { percent = 8 });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, await EventCountAsync(factory, runId));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"percent\":0}")]
    [InlineData("{\"percent\":-1}")]
    [InlineData("{\"percent\":101}")]
    [InlineData("{\"percent\":2147483648}")]
    [InlineData("{\"percent\":1e400}")]
    [InlineData("{\"percent\":3.5}")]
    [InlineData("{\"percent\":5.0}")]
    [InlineData("{\"percent\":\"5\"}")]
    [InlineData("{\"percent\":\"\"}")]
    [InlineData("{\"percent\":true}")]
    [InlineData("{\"percent\":[5]}")]
    [InlineData("{\"percent\":{\"value\":5}}")]
    [InlineData("{\"percent\":5,\"percent\":6}")]
    [InlineData("{\"percent\":5,\"Percent\":6}")]
    [InlineData("{\"percent\":null,\"percent\":5}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("{\"percent\":5")]
    public async Task Invalid_or_ambiguous_input_returns_400_and_writes_nothing(string json)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        (await client.PostAsJsonAsync(Route(runId), new { percent = 9 })).EnsureSuccessStatusCode();

        var response = await PostRawAsync(client, runId, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(9, (await StoredAsync(factory, runId)).Value);
        Assert.Equal(1, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_terminal_run_is_rejected_without_a_partial_write_even_for_a_same_value()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory, terminal: true);

        var response = await client.PostAsJsonAsync(Route(runId), new { percent = 5 });
        var clear = await PostRawAsync(client, runId, "{\"percent\":null}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, clear.StatusCode);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal(0, await EventCountAsync(factory, runId));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("codex_account_usage_warning.run_not_editable", problem.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_simulated_run_which_never_admits_agent_work_is_refused_with_a_stable_code()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory, mode: RunExecutionMode.Simulated);

        var response = await client.PostAsJsonAsync(Route(runId), new { percent = 50 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("runs.execution_mode_not_admitted", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal(0, await EventCountAsync(factory, runId));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(3.5)]
    [InlineData(0)]
    [InlineData(4294967297L)]
    public async Task A_malformed_stored_value_projects_unknown_without_a_number_and_a_valid_set_or_clear_repairs_it(object stored)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {stored} WHERE Id = {runId}");
        }

        var unknown = (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageWarning");
        Assert.Equal("Unknown", unknown.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, unknown.GetProperty("percent").ValueKind);

        (await client.PostAsJsonAsync(Route(runId), new { percent = 33 })).EnsureSuccessStatusCode();
        Assert.Equal("Configured", (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageWarning").GetProperty("state").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {stored} WHERE Id = {runId}");
        }

        (await PostRawAsync(client, runId, "{\"percent\":null}")).EnsureSuccessStatusCode();
        Assert.Equal("NotConfigured", (await GetCockpitAsync(client, runId)).GetProperty("codexAccountUsageWarning").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Concurrent_writes_each_commit_one_event_and_the_final_value_matches_the_last_event()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        var requests = Enumerable.Range(1, 8).Select(index => index * 10).ToArray();

        var responses = await Task.WhenAll(requests.Select(percent => client.PostAsJsonAsync(Route(runId), new { percent })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var events = await dbContext.Events.AsNoTracking()
            .Where(item => item.RunId == runId && item.EventType == RunEventType.CodexAccountUsageWarningChanged)
            .OrderBy(item => item.Sequence)
            .ToListAsync();
        Assert.Equal(requests.Length, events.Count);
        var stored = (await dbContext.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).ReadCodexAccountUsageWarningPercent();
        Assert.Equal("{\"percent\":" + stored.Value + "}", events[^1].PayloadJson);
    }

    // The 8 KiB MVC request-body bound is proven through real Kestrel (the in-memory TestServer has no server size feature).

    private static (ApiWebApplicationFactory Factory, HttpClient Client) StartKestrel()
    {
        var factory = new ApiWebApplicationFactory();
        factory.UseKestrel();
        factory.StartServer();
        var client = factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return (factory, client);
    }

    [Fact]
    public async Task A_body_beyond_the_selected_bound_is_refused_by_the_real_host_before_any_setting_or_event_write()
    {
        var (factory, client) = StartKestrel();
        await using var _ = factory;
        using var __ = client;
        var runId = await SeedRunAsync(factory);
        var oversized = "{\"percent\":80,\"padding\":\"" + new string('x', 32 * 1024) + "\"}";

        var response = await PostRawAsync(client, runId, oversized);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal(0, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_body_just_inside_the_bound_is_accepted_by_the_real_host()
    {
        var (factory, client) = StartKestrel();
        await using var _ = factory;
        using var __ = client;
        var runId = await SeedRunAsync(factory);
        var padding = new string('x', 8 * 1024 - 64);
        var body = "{\"percent\":80,\"padding\":\"" + padding + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(body) < 8 * 1024);

        var response = await PostRawAsync(client, runId, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(80, (await StoredAsync(factory, runId)).Value);
    }

    [Fact]
    public async Task The_real_host_still_sets_clears_authenticates_and_enforces_the_strict_percent_converter()
    {
        var (factory, client) = StartKestrel();
        await using var _ = factory;
        using var __ = client;
        using var anonymous = factory.CreateDefaultClient();
        var runId = await SeedRunAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRawAsync(anonymous, runId, "{\"percent\":80}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRawAsync(client, runId, "{\"percent\":\"80\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRawAsync(client, runId, "{\"percent\":80.5}")).StatusCode);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);

        var set = await PostRawAsync(client, runId, "{\"percent\":80}");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(80, (await StoredAsync(factory, runId)).Value);
        var cleared = await PostRawAsync(client, runId, "{\"percent\":null}");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.True((await StoredAsync(factory, runId)).IsAbsent);
        Assert.Equal(2, await EventCountAsync(factory, runId));
    }
}
