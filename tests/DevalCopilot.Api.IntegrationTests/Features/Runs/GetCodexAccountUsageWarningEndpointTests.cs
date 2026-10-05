using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The explicit advisory warning check (ADR-0026) through the real authenticated MVC pipeline and Program composition: a
/// protected GET that makes at most one strict observation, discloses only bounded percentages, identifiers and the host retrieval
/// time, writes nothing, is never reached by an ordinary cockpit read, and keeps the wire shape the generated client reads.</summary>
public sealed class GetCodexAccountUsageWarningEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/codex-account-usage-warning";

    private sealed class Rig
    {
        public required WebApplicationFactory<Program> Factory { get; init; }

        public required ScriptedAccountUsageAdapter Observer { get; init; }

        public required HttpClient Client { get; init; }
    }

    private static Rig CreateRig(Func<int, AccountUsageObservation>? respond = null)
    {
        var observer = new ScriptedAccountUsageAdapter { Respond = respond ?? (_ => AccountUsageObservation.Unavailable) };
        var factory = new ApiWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountUsageObserver>();
            services.AddSingleton<IAccountUsageObserver>(observer);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return new Rig { Factory = factory, Observer = observer, Client = client };
    }

    private static async Task<Guid> SeedRunAsync(
        WebApplicationFactory<Program> factory, int? warning, RunExecutionMode mode = RunExecutionMode.ManualAgent, bool launch = true)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, 1, mode, "Objective", now);
        run.Claim(now);
        run.SetCodexAccountUsageWarningPercent(warning);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        if (launch)
        {
            var snapshot = await dbContext.HostCapabilitySnapshots.SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli);
            if (snapshot is null)
            {
                snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, now);
                dbContext.HostCapabilitySnapshots.Add(snapshot);
            }

            if (snapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath))
            {
                snapshot.MarkDispatched(now);
                snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            }

            await dbContext.SaveChangesAsync();
        }
        return run.Id;
    }

    private static AccountUsageObservation Observation(int primary, int? secondary = null, bool providerReached = false) =>
        AccountUsageObservation.Create(
            DateTimeOffset.UtcNow,
            [new AccountUsageBucket(
                "codex", new AccountUsageWindow(primary, null), secondary is { } value ? new AccountUsageWindow(value, null) : null)],
            providerReached);

    private static async Task<(int Events, int Attempts)> CountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (await dbContext.Events.CountAsync(), await dbContext.Attempts.CountAsync());
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, Guid runId)
    {
        var response = await client.GetAsync(Route(runId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task A_below_check_returns_the_wire_shape_the_generated_client_reads_with_one_observation_and_no_writes()
    {
        var rig = CreateRig(_ => Observation(40, 10));
        var runId = await SeedRunAsync(rig.Factory, warning: 80);
        var before = await CountsAsync(rig.Factory);

        var body = await GetJsonAsync(rig.Client, runId);

        Assert.Equal(
            ["state", "reason", "thresholdPercent", "observedAtUtc", "windows", "providerReportedLimitReached"],
            body.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Below", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reason").ValueKind);
        Assert.Equal(80, body.GetProperty("thresholdPercent").GetInt32());
        Assert.True(DateTimeOffset.TryParse(body.GetProperty("observedAtUtc").GetString(), out _));
        Assert.False(body.GetProperty("providerReportedLimitReached").GetBoolean());
        var windows = body.GetProperty("windows").EnumerateArray().ToArray();
        Assert.Equal(2, windows.Length);
        Assert.Equal(["bucketId", "window", "usedPercent", "reachedThreshold"], windows[0].EnumerateObject().Select(member => member.Name));
        Assert.Equal(("codex", "Primary", 40, false), (windows[0].GetProperty("bucketId").GetString(), windows[0].GetProperty("window").GetString(), windows[0].GetProperty("usedPercent").GetInt32(), windows[0].GetProperty("reachedThreshold").GetBoolean()));
        Assert.Equal("Secondary", windows[1].GetProperty("window").GetString());
        Assert.Equal(1, rig.Observer.Calls);
        Assert.Equal(before, await CountsAsync(rig.Factory));
    }

    [Theory]
    [InlineData(80, 80, "Reached", "ThresholdReached")]
    [InlineData(100, 99, "Reached", "ThresholdReached")]
    public async Task A_reached_check_reports_the_reached_window_at_equality_and_above(int threshold, int used, string state, string reason)
    {
        var rig = CreateRig(_ => Observation(used, 1));
        var runId = await SeedRunAsync(rig.Factory, warning: Math.Min(threshold, used));

        var body = await GetJsonAsync(rig.Client, runId);

        Assert.Equal(state, body.GetProperty("state").GetString());
        Assert.Equal(reason, body.GetProperty("reason").GetString());
        Assert.True(body.GetProperty("windows").EnumerateArray().First().GetProperty("reachedThreshold").GetBoolean());
    }

    [Fact]
    public async Task A_provider_reported_reached_state_warns_and_an_unavailable_observation_is_unavailable_never_below()
    {
        var rig = CreateRig(call => call == 0 ? Observation(1, 1, providerReached: true) : AccountUsageObservation.Unavailable);
        var runId = await SeedRunAsync(rig.Factory, warning: 80);

        var reached = await GetJsonAsync(rig.Client, runId);
        var unavailable = await GetJsonAsync(rig.Client, runId);

        Assert.Equal("ProviderReportedLimitReached", reached.GetProperty("reason").GetString());
        Assert.True(reached.GetProperty("providerReportedLimitReached").GetBoolean());
        Assert.Equal("Unavailable", unavailable.GetProperty("state").GetString());
        Assert.Equal("EvidenceUnavailable", unavailable.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("observedAtUtc").ValueKind);
        Assert.Empty(unavailable.GetProperty("windows").EnumerateArray());
    }

    [Fact]
    public async Task No_setting_causes_no_observation_and_an_ordinary_cockpit_read_never_reaches_the_observer()
    {
        var rig = CreateRig(_ => Observation(1));
        var access = rig.Factory;
        var plain = await SeedRunAsync(access, warning: null);
        var configured = await SeedRunAsync(access, warning: 50);

        var notConfigured = await GetJsonAsync(rig.Client, plain);
        (await rig.Client.GetAsync($"/api/runs/{plain}/cockpit")).EnsureSuccessStatusCode();
        (await rig.Client.GetAsync($"/api/runs/{configured}/cockpit")).EnsureSuccessStatusCode();
        (await rig.Client.PostAsJsonAsync(Route(configured), new { percent = 60 })).EnsureSuccessStatusCode();
        (await rig.Client.PostAsJsonAsync(Route(configured), new { percent = (int?)null })).EnsureSuccessStatusCode();

        Assert.Equal("NotConfigured", notConfigured.GetProperty("state").GetString());
        Assert.Equal(0, rig.Observer.Calls);
    }

    [Fact]
    public async Task An_invalid_stored_setting_and_an_unadmitted_run_cause_no_observation()
    {
        var rig = CreateRig(_ => Observation(1));
        var access = rig.Factory;
        var invalid = await SeedRunAsync(access, warning: 50);
        var simulated = await SeedRunAsync(access, warning: null, mode: RunExecutionMode.Simulated);
        using (var scope = access.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {"abc"} WHERE Id = {invalid}");
        }

        var invalidBody = await GetJsonAsync(rig.Client, invalid);
        var simulatedResponse = await rig.Client.GetAsync(Route(simulated));
        var missing = await rig.Client.GetAsync(Route(Guid.NewGuid()));

        Assert.Equal("SettingInvalid", invalidBody.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, invalidBody.GetProperty("thresholdPercent").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, simulatedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, rig.Observer.Calls);
    }

    [Fact]
    public async Task The_get_takes_no_threshold_executable_or_prior_observation_from_the_caller()
    {
        var rig = CreateRig(_ => Observation(10));
        var runId = await SeedRunAsync(rig.Factory, warning: 80);

        var response = await rig.Client.GetAsync(
            Route(runId) + "?threshold=1&executable=C%3A%5Cevil.exe&percent=1&observation=reached&provider=claude");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Below", body.RootElement.GetProperty("state").GetString());
        Assert.Equal(80, body.RootElement.GetProperty("thresholdPercent").GetInt32());
        Assert.Equal(1, rig.Observer.Calls);
    }

    [Fact]
    public async Task The_response_is_never_cacheable()
    {
        var rig = CreateRig(_ => Observation(10));
        var runId = await SeedRunAsync(rig.Factory, warning: 80);

        var response = await rig.Client.GetAsync(Route(runId));

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
