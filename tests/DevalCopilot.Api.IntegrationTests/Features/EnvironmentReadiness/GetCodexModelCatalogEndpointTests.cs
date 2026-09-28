using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.EnvironmentReadiness;

public sealed class GetCodexModelCatalogEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Route = "/api/environment/codex-model-catalog";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_request_with_no_vetted_codex_target_reports_unknown_without_an_empty_looking_success()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal("Unknown", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("retrievedAtUtc").ValueKind is JsonValueKind.Null);
        Assert.Empty(root.GetProperty("models").EnumerateArray());
    }

    [Fact]
    public async Task An_authenticated_request_never_exposes_a_raw_executable_path_or_environment_detail()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("executablePath", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scriptPath", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentials", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_authenticated_request_projects_observed_models_with_effort_fields()
    {
        using var observedFactory = new ObservedCatalogFactory();
        using var client = observedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        await using (var scope = observedFactory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(snapshot => snapshot.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal("Observed", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("models").GetArrayLength());
        var first = root.GetProperty("models")[0];
        Assert.Equal("gpt-6-sol", first.GetProperty("id").GetString());
        Assert.Equal("medium", first.GetProperty("defaultReasoningEffort").GetString());
        Assert.Contains("high", first.GetProperty("supportedReasoningEfforts").EnumerateArray().Select(effort => effort.GetString()));
        Assert.DoesNotContain("codex.exe", payload, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
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
