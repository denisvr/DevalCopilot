using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.EnvironmentReadiness;

public sealed class ProviderRuntimePreflightEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private const string Route = "/api/environment/provider-runtimes";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_request_returns_only_safe_provider_preflight_fields()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var runtimes = document.RootElement;
        Assert.Equal(2, runtimes.GetArrayLength());
        Assert.Equal("Codex", runtimes[0].GetProperty("provider").GetString());
        Assert.Equal("ClaudeCode", runtimes[1].GetProperty("provider").GetString());

        foreach (var runtime in runtimes.EnumerateArray())
        {
            Assert.Equal("Unknown", runtime.GetProperty("authentication").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("modelCatalog").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("reasoningEffort").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("permissionMode").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("contextUsage").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("compaction").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("sessions").GetString());
            Assert.Equal("Unknown", runtime.GetProperty("accountUsage").GetString());
            Assert.False(runtime.TryGetProperty("resolvedExecutablePath", out _));
            Assert.False(runtime.TryGetProperty("rawOutput", out _));
            Assert.False(runtime.TryGetProperty("environment", out _));
            Assert.False(runtime.TryGetProperty("credentials", out _));
        }
    }

    [Fact]
    public async Task Provider_refresh_reuses_the_existing_host_capability_refresh_operation()
    {
        using var client = CreateAuthenticatedClient();

        var refresh = await client.PostAsync("/api/environment/capabilities/CodexCli/refresh", content: null);
        var preflight = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
    }

    [Fact]
    public async Task Project_run_summaries_do_not_expose_local_executable_or_provider_sensitive_details()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/projects/run-summaries");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("resolvedExecutablePath", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("rawOutput", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("environment", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("credentials", payload, StringComparison.Ordinal);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }
}
