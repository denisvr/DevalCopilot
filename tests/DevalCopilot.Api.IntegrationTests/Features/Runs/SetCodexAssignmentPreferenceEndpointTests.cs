using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class SetCodexAssignmentPreferenceEndpointTests
{
    private static string Route(Guid runId) => $"/api/runs/{runId}/codex-assignment-preference";

    private static HttpClient CreateAuthenticatedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static async Task<Guid> SeedRunAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { requestedModel = (string?)null, requestedEffort = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_run_returns_404()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            Route(Guid.NewGuid()), new { requestedModel = (string?)null, requestedEffort = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_effort_supplied_without_a_model_fails_structural_validation()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = (string?)null, requestedEffort = "high" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Clearing_the_preference_succeeds_without_a_vetted_codex_target()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = (string?)null, requestedEffort = (string?)null });
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("requestedModel").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("requestedEffort").ValueKind);
    }

    [Fact]
    public async Task Setting_a_new_selection_without_a_vetted_codex_target_fails_closed()
    {
        var factory = new ApiWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);
        var runId = await SeedRunAsync(factory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "gpt-6-sol", requestedEffort = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_visible_model_and_supported_effort_are_accepted_and_persisted()
    {
        using var observedFactory = new ObservedCatalogFactory();
        using var client = CreateAuthenticatedClient(observedFactory);
        var runId = await SeedRunAsync(observedFactory);
        await MarkCodexVettedAsync(observedFactory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "gpt-6-sol", requestedEffort = "high" });
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("gpt-6-sol", document.RootElement.GetProperty("requestedModel").GetString());
        Assert.Equal("high", document.RootElement.GetProperty("requestedEffort").GetString());

        var cockpitResponse = await client.GetAsync($"/api/runs/{runId}/cockpit");
        var cockpitPayload = await cockpitResponse.Content.ReadAsStringAsync();
        using var cockpitDocument = JsonDocument.Parse(cockpitPayload);
        Assert.Equal("gpt-6-sol", cockpitDocument.RootElement.GetProperty("requestedCodexModel").GetString());
        Assert.Equal("high", cockpitDocument.RootElement.GetProperty("requestedCodexEffort").GetString());
    }

    [Fact]
    public async Task A_model_not_visible_in_the_catalog_is_rejected()
    {
        using var observedFactory = new ObservedCatalogFactory();
        using var client = CreateAuthenticatedClient(observedFactory);
        var runId = await SeedRunAsync(observedFactory);
        await MarkCodexVettedAsync(observedFactory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "unknown-model", requestedEffort = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task An_effort_not_supported_by_the_chosen_model_is_rejected()
    {
        using var observedFactory = new ObservedCatalogFactory();
        using var client = CreateAuthenticatedClient(observedFactory);
        var runId = await SeedRunAsync(observedFactory);
        await MarkCodexVettedAsync(observedFactory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "gpt-6-sol", requestedEffort = "not-a-real-effort" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task The_response_never_exposes_a_raw_executable_path_or_credential()
    {
        using var observedFactory = new ObservedCatalogFactory();
        using var client = CreateAuthenticatedClient(observedFactory);
        var runId = await SeedRunAsync(observedFactory);
        await MarkCodexVettedAsync(observedFactory);

        var response = await client.PostAsJsonAsync(Route(runId), new { requestedModel = "gpt-6-sol", requestedEffort = "high" });
        var payload = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("executablePath", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task MarkCodexVettedAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(snapshot => snapshot.Capability == Capability.CodexCli);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        await dbContext.SaveChangesAsync();
    }

    private sealed class ObservedCatalogFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICodexModelCatalogAdapter>();
                services.AddSingleton<ICodexModelCatalogAdapter>(new ObservedCatalogAdapter());
            });
        }
    }

    private sealed class ObservedCatalogAdapter : ICodexModelCatalogAdapter
    {
        public Task<CodexModelCatalogObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
            Task.FromResult(new CodexModelCatalogObservation(true, DateTimeOffset.UtcNow,
            [
                new CodexModelCatalogEntry("gpt-6-sol", "GPT-6 Sol", ["medium", "high"], "medium"),
                new CodexModelCatalogEntry("gpt-6-mini", "GPT-6 Mini", ["low"], null),
            ]));
    }
}
