using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.EnvironmentReadiness;

public sealed class GetGitHubStoredAuthenticationEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Route = "/api/environment/github-stored-authentication";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
    }

    [Fact]
    public async Task Only_get_is_routed()
    {
        using var client = CreateAuthenticatedClient(factory);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(Route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Route)).StatusCode);
    }

    [Fact]
    public async Task Without_a_current_gh_target_the_answer_is_cli_unavailable_with_exactly_four_wire_properties()
    {
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(["context", "host", "observedAtUtc", "status"], root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("github.com", root.GetProperty("host").GetString());
        Assert.Equal("StoredDefaultConfig", root.GetProperty("context").GetString());
        Assert.Equal("CliUnavailable", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("observedAtUtc").ValueKind);
    }

    [Fact]
    public async Task A_client_supplied_host_or_body_is_ignored_and_the_fixed_host_is_always_reported()
    {
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(Route + "?host=evil.example&hostname=evil.example");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("github.com", document.RootElement.GetProperty("host").GetString());
    }

    [Theory]
    [InlineData(GitHubStoredAuthenticationStatus.NoActiveAccount)]
    [InlineData(GitHubStoredAuthenticationStatus.Authenticated)]
    [InlineData(GitHubStoredAuthenticationStatus.IssueReported)]
    [InlineData(GitHubStoredAuthenticationStatus.Unknown)]
    public async Task An_observed_status_is_projected_with_the_fixed_context_and_a_time_only_when_observed(
        GitHubStoredAuthenticationStatus status)
    {
        var observedAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);
        var observer = new FixedObserver(new GitHubStoredAuthenticationObservation(status, observedAt));
        using var observedFactory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(observedFactory);
        await SeedFreshGitHubTargetAsync(observedFactory);

        var response = await client.GetAsync(Route);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(["context", "host", "observedAtUtc", "status"], root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("github.com", root.GetProperty("host").GetString());
        Assert.Equal("StoredDefaultConfig", root.GetProperty("context").GetString());
        Assert.Equal(status.ToString(), root.GetProperty("status").GetString());
        if (status == GitHubStoredAuthenticationStatus.Unknown)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("observedAtUtc").ValueKind);
        }
        else
        {
            Assert.Equal(observedAt, root.GetProperty("observedAtUtc").GetDateTimeOffset());
        }

        Assert.Equal(@"C:\safe\gh.exe", Assert.Single(observer.Paths));
        Assert.DoesNotContain("gh.exe", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("login", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_request_writes_nothing_to_the_database()
    {
        var observer = new FixedObserver(new GitHubStoredAuthenticationObservation(GitHubStoredAuthenticationStatus.Authenticated, DateTimeOffset.UtcNow));
        using var observedFactory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(observedFactory);
        await SeedFreshGitHubTargetAsync(observedFactory);
        var before = await TableFingerprintAsync(observedFactory);

        await client.GetAsync(Route);
        await client.GetAsync(Route);

        Assert.Equal(before, await TableFingerprintAsync(observedFactory));
        Assert.Equal(2, observer.Paths.Count);
    }

    private static async Task SeedFreshGitHubTargetAsync(ApiWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var snapshot = await dbContext.HostCapabilitySnapshots.SingleAsync(candidate => candidate.Capability == Capability.GitHubCli);
        snapshot.MarkDispatched(now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\gh.exe", null, "2.50.0", now, now.AddMinutes(5));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Every row of every table, as text, so any write (an insert, update or delete) changes the result.</summary>
    private static async Task<string> TableFingerprintAsync(ApiWebApplicationFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var builder = new System.Text.StringBuilder();
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The table names come from sqlite_master of the disposable test database.
                command.CommandText = $"SELECT * FROM \"{table}\"";
#pragma warning restore CA2100
                await using var reader = await command.ExecuteReaderAsync();
                builder.Append(table).Append(':');
                while (await reader.ReadAsync())
                {
                    for (var column = 0; column < reader.FieldCount; column++)
                    {
                        builder.Append(reader.IsDBNull(column) ? "<null>" : Convert.ToString(reader.GetValue(column))).Append('|');
                    }

                    builder.Append(';');
                }
            }

            return builder.ToString();
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static HttpClient CreateAuthenticatedClient(ApiWebApplicationFactory host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private sealed class ObservedFactory(FixedObserver observer) : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGitHubStoredAuthenticationObserver>();
                services.AddSingleton<IGitHubStoredAuthenticationObserver>(observer);
            });
        }
    }

    private sealed class FixedObserver(GitHubStoredAuthenticationObservation observation) : IGitHubStoredAuthenticationObserver
    {
        public List<string> Paths { get; } = [];

        public Task<GitHubStoredAuthenticationObservation> ObserveAsync(string executablePath, CancellationToken cancellationToken)
        {
            Paths.Add(executablePath);
            return Task.FromResult(observation);
        }
    }
}
