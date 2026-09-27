using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.EnvironmentReadiness;

public sealed class GetCodexAccountAllowanceEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Route = "/api/environment/codex-account-allowance";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_request_with_no_vetted_codex_target_reports_unknown_without_a_zero_valued_window()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal("Unknown", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("retrievedAtUtc").ValueKind is JsonValueKind.Null);
        Assert.Empty(root.GetProperty("buckets").EnumerateArray());
        Assert.DoesNotContain("usedPercent", payload, StringComparison.Ordinal);
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
    public async Task An_authenticated_request_projects_observed_buckets_and_optional_window_fields()
    {
        using var observedFactory = new ObservedAllowanceFactory();
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
        Assert.Equal(2, root.GetProperty("buckets").GetArrayLength());
        var first = root.GetProperty("buckets")[0];
        Assert.Equal("codex", first.GetProperty("limitId").GetString());
        Assert.Equal(42, first.GetProperty("primary").GetProperty("usedPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("primary").GetProperty("resetsAtUtc").ValueKind);
        Assert.DoesNotContain("codex.exe", payload, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private sealed class ObservedAllowanceFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICodexAccountAllowanceAdapter>();
                services.AddSingleton<ICodexAccountAllowanceAdapter>(new ObservedAllowanceAdapter());
            });
        }
    }

    private sealed class ObservedAllowanceAdapter : ICodexAccountAllowanceAdapter
    {
        public Task<CodexAccountAllowanceObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
            Task.FromResult(new CodexAccountAllowanceObservation(true, DateTimeOffset.UtcNow,
            [
                new CodexAllowanceBucket("codex", new CodexAllowanceWindow(42, 300, null), null),
                new CodexAllowanceBucket("gpt-5", new CodexAllowanceWindow(7, null, null), null),
            ]));
    }
}
