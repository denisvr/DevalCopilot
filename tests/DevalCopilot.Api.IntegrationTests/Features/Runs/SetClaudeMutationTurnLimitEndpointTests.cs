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

public sealed class SetClaudeMutationTurnLimitEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/claude-mutation-turn-limit";

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

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, Guid runId, string json) =>
        client.PostAsync(Route(runId), new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> GetCockpitAsync(HttpClient client, Guid runId)
    {
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<int?> StoredLimitAsync(ApiWebApplicationFactory factory, Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (await dbContext.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).RequestedClaudeMaxTurns;
    }

    private static async Task<int> EventCountAsync(ApiWebApplicationFactory factory, Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return await dbContext.Events.CountAsync(item => item.RunId == runId && item.EventType == RunEventType.ClaudeMutationTurnLimitChanged);
    }

    [Fact]
    public async Task An_absent_credential_returns_401_and_writes_nothing()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { maxTurns = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await StoredLimitAsync(factory, runId));
        Assert.Equal(0, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_missing_run_returns_404()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { maxTurns = 5 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task Setting_a_limit_persists_it_records_one_human_event_and_projects_it_as_a_request_only(int maxTurns)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { maxTurns });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(maxTurns, document.RootElement.GetProperty("maxTurns").GetInt32());
        Assert.Equal(maxTurns, await StoredLimitAsync(factory, runId));

        var cockpit = await GetCockpitAsync(client, runId);
        var fact = cockpit.GetProperty("claudeMutationTurnLimit");
        Assert.Equal("Requested", fact.GetProperty("state").GetString());
        Assert.Equal(maxTurns, fact.GetProperty("maxTurns").GetInt32());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var recorded = await dbContext.Events.AsNoTracking()
            .SingleAsync(item => item.RunId == runId && item.EventType == RunEventType.ClaudeMutationTurnLimitChanged);
        Assert.Equal("{\"maxTurns\":" + maxTurns + "}", recorded.PayloadJson);
        Assert.Equal(ParticipantKind.Human, recorded.Actor.Kind);
        Assert.Null(recorded.AttemptId);
    }

    [Fact]
    public async Task A_new_run_projects_not_requested_and_an_explicit_null_clears_a_saved_request()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var initial = await GetCockpitAsync(client, runId);
        Assert.Equal("NotRequested", initial.GetProperty("claudeMutationTurnLimit").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("claudeMutationTurnLimit").GetProperty("maxTurns").ValueKind);

        (await client.PostAsJsonAsync(Route(runId), new { maxTurns = 12 })).EnsureSuccessStatusCode();
        var cleared = await PostRawAsync(client, runId, "{\"maxTurns\":null}");

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var document = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("maxTurns").ValueKind);
        Assert.Null(await StoredLimitAsync(factory, runId));
        var cockpit = await GetCockpitAsync(client, runId);
        Assert.Equal("NotRequested", cockpit.GetProperty("claudeMutationTurnLimit").GetProperty("state").GetString());
        Assert.Equal(2, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_same_value_set_is_accepted_and_still_records_its_event()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        (await client.PostAsJsonAsync(Route(runId), new { maxTurns = 8 })).EnsureSuccessStatusCode();
        var again = await client.PostAsJsonAsync(Route(runId), new { maxTurns = 8 });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(8, await StoredLimitAsync(factory, runId));
        Assert.Equal(2, await EventCountAsync(factory, runId));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"maxTurns\":0}")]
    [InlineData("{\"maxTurns\":-1}")]
    [InlineData("{\"maxTurns\":101}")]
    [InlineData("{\"maxTurns\":2147483648}")]
    [InlineData("{\"maxTurns\":-2147483649}")]
    [InlineData("{\"maxTurns\":1e400}")]
    [InlineData("{\"maxTurns\":3.5}")]
    [InlineData("{\"maxTurns\":5.0}")]
    [InlineData("{\"maxTurns\":\"5\"}")]
    [InlineData("{\"maxTurns\":\"\"}")]
    [InlineData("{\"maxTurns\":\"abc\"}")]
    [InlineData("{\"maxTurns\":true}")]
    [InlineData("{\"maxTurns\":[5]}")]
    [InlineData("{\"maxTurns\":{\"value\":5}}")]
    [InlineData("{\"maxTurns\":5,\"maxTurns\":6}")]
    [InlineData("{\"maxTurns\":5,\"MaxTurns\":6}")]
    [InlineData("{\"maxTurns\":null,\"maxTurns\":5}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("{\"maxTurns\":5")]
    public async Task Invalid_or_ambiguous_input_returns_400_and_writes_nothing(string json)
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        (await client.PostAsJsonAsync(Route(runId), new { maxTurns = 9 })).EnsureSuccessStatusCode();

        var response = await PostRawAsync(client, runId, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(9, await StoredLimitAsync(factory, runId));
        Assert.Equal(1, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_terminal_run_is_rejected_without_a_partial_write_even_for_a_same_value()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory, terminal: true);

        var response = await client.PostAsJsonAsync(Route(runId), new { maxTurns = 5 });
        var clear = await PostRawAsync(client, runId, "{\"maxTurns\":null}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, clear.StatusCode);
        Assert.Null(await StoredLimitAsync(factory, runId));
        Assert.Equal(0, await EventCountAsync(factory, runId));
    }

    [Fact]
    public async Task A_running_run_with_an_earlier_running_attempt_can_change_the_request_without_touching_that_attempt()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);
        Guid attemptId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var run = await dbContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.Claim(DateTimeOffset.UtcNow);
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, DateTimeOffset.UtcNow, null, null,
                AgentPermissionProfile.WorkspaceEditOnly, ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 7);
            attemptId = attempt.Id;
            dbContext.Attempts.Add(attempt);
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(Route(runId), new { maxTurns = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, await StoredLimitAsync(factory, runId));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var stored = await db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(7, stored.AgentRequestedMaxTurns);
        Assert.Equal(AttemptStatus.Running, stored.Status);
    }
}
