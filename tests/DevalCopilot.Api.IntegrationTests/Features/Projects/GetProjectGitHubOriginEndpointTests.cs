using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// The protected, bodyless repository-local GitHub origin route (ADR-0039) through the real host, MVC authentication, the
/// mediator, the real root and physical-identity inspectors and a disposable SQLite database; only the Git observer is a
/// fixed double here (the real adapter is proved against real Git elsewhere). Exact closed wire shapes, authentication,
/// the fixed 404, ignored client input and zero database writes.
/// </summary>
public sealed class GetProjectGitHubOriginEndpointTests : IDisposable
{
    private const string GitPath = @"C:\safe\git.exe";

    private readonly string _projectRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-origin-endpoint-{Guid.NewGuid():N}");

    public GetProjectGitHubOriginEndpointTests()
    {
        Directory.CreateDirectory(_projectRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
        {
            Directory.Delete(_projectRoot, recursive: true);
        }
    }

    private static string Route(Guid projectId) => $"/api/projects/{projectId}/github-origin";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task An_unknown_credential_returns_401()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Only_get_is_routed()
    {
        var observer = new FixedObserver(GitHubOriginObservation.NotConfigured);
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var route = Route(await SeedProjectAsync(factory));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Empty(observer.Invocations);
    }

    [Fact]
    public async Task An_unknown_project_is_the_fixed_not_found_problem_and_a_malformed_identity_is_not_routed()
    {
        var observer = new FixedObserver(GitHubOriginObservation.NotConfigured);
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);

        var unknown = await client.GetAsync(Route(Guid.NewGuid()));
        var body = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("urn:devalente:problem:not-found", body.GetProperty("type").GetString());
        Assert.Equal("projects.not_found", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/not-a-guid/github-origin")).StatusCode);
        Assert.Empty(observer.Invocations);
    }

    [Fact]
    public async Task Without_a_current_git_target_the_answer_is_unavailable_with_null_time_and_target_and_no_invocation()
    {
        var observer = new FixedObserver(GitHubOriginObservation.NotConfigured);
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory, seedFreshGit: false);

        var response = await client.GetAsync(Route(projectId));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(["observedAtUtc", "projectId", "source", "status", "target"], PropertyNames(root));
        Assert.Equal(projectId, root.GetProperty("projectId").GetGuid());
        Assert.Equal("RepositoryLocalConfig", root.GetProperty("source").GetString());
        Assert.Equal("Unavailable", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("observedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("target").ValueKind);
        Assert.Empty(observer.Invocations);
    }

    [Fact]
    public async Task Available_projects_the_closed_target_and_the_observation_time()
    {
        var observer = new FixedObserver(new GitHubOriginObservation(GitHubOriginStatus.Available, new GitHubOriginTarget("acme", "widgets")));
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory);
        var before = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(Route(projectId));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(["observedAtUtc", "projectId", "source", "status", "target"], PropertyNames(root));
        Assert.Equal("Available", root.GetProperty("status").GetString());
        Assert.InRange(root.GetProperty("observedAtUtc").GetDateTimeOffset(), before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var target = root.GetProperty("target");
        Assert.Equal(["host", "owner", "repository"], PropertyNames(target));
        Assert.Equal("github.com", target.GetProperty("host").GetString());
        Assert.Equal("acme", target.GetProperty("owner").GetString());
        Assert.Equal("widgets", target.GetProperty("repository").GetString());
        Assert.DoesNotContain("http", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GitPath.Replace("\\", "\\\\", StringComparison.Ordinal), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("devalcopilot-origin-endpoint", payload, StringComparison.OrdinalIgnoreCase);

        var invocation = Assert.Single(observer.Invocations);
        Assert.Equal(GitPath, invocation.Git);
        Assert.Equal(_projectRoot, invocation.Root, ignoreCase: true);
    }

    [Fact]
    public async Task Not_configured_has_a_time_and_a_null_target()
    {
        var observer = new FixedObserver(GitHubOriginObservation.NotConfigured);
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory);

        using var document = JsonDocument.Parse(await (await client.GetAsync(Route(projectId))).Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("NotConfigured", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("observedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("target").ValueKind);
    }

    [Fact]
    public async Task An_unresolved_stored_identity_is_unavailable_and_is_never_resolved_by_the_read()
    {
        var observer = new FixedObserver(GitHubOriginObservation.NotConfigured);
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory, resolveIdentity: false);
        var before = await TableFingerprintAsync(factory);

        using var document = JsonDocument.Parse(await (await client.GetAsync(Route(projectId))).Content.ReadAsStringAsync());

        Assert.Equal("Unavailable", document.RootElement.GetProperty("status").GetString());
        Assert.Empty(observer.Invocations);
        Assert.Equal(before, await TableFingerprintAsync(factory));
    }

    [Fact]
    public async Task Client_supplied_query_values_and_a_body_cannot_change_what_is_observed()
    {
        var observer = new FixedObserver(new GitHubOriginObservation(GitHubOriginStatus.Available, new GitHubOriginTarget("acme", "widgets")));
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route(projectId) + "?path=C:%5Cevil&remote=upstream&url=https://evil.example/x/y&host=evil.example")
        {
            Content = new StringContent("{\"path\":\"C:\\\\evil\",\"url\":\"https://evil.example/x/y\"}", Encoding.UTF8, "application/json"),
        });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("acme", document.RootElement.GetProperty("target").GetProperty("owner").GetString());
        var invocation = Assert.Single(observer.Invocations);
        Assert.Equal(_projectRoot, invocation.Root, ignoreCase: true);
    }

    [Fact]
    public async Task The_request_writes_nothing_to_the_database_and_never_updates_the_stored_identity()
    {
        var observer = new FixedObserver(new GitHubOriginObservation(GitHubOriginStatus.Available, new GitHubOriginTarget("acme", "widgets")));
        using var factory = new ObservedFactory(observer);
        using var client = CreateAuthenticatedClient(factory);
        var projectId = await SeedProjectAsync(factory);
        var before = await TableFingerprintAsync(factory);

        await client.GetAsync(Route(projectId));
        await client.GetAsync(Route(projectId));

        Assert.Equal(before, await TableFingerprintAsync(factory));
        Assert.Equal(2, observer.Invocations.Count);
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private async Task<Guid> SeedProjectAsync(ApiWebApplicationFactory host, bool seedFreshGit = true, bool resolveIdentity = true)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Origin endpoint fixture", _projectRoot, now);
        if (resolveIdentity)
        {
            var root = scope.ServiceProvider.GetRequiredService<IRepositoryRootPathInspector>().Inspect(_projectRoot);
            var identity = scope.ServiceProvider.GetRequiredService<IRepositoryPhysicalIdentityInspector>().Resolve(root.Candidate!);
            Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, identity.Outcome);
            project.RecordPhysicalIdentityResolved(identity.VolumeSerialNumber!.Value, identity.FileId!);
        }

        dbContext.Projects.Add(project);
        if (seedFreshGit)
        {
            var snapshot = await dbContext.HostCapabilitySnapshots.SingleAsync(candidate => candidate.Capability == Capability.Git);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, GitPath, null, "2.53.0", now, now.AddMinutes(5));
        }

        await dbContext.SaveChangesAsync();
        return project.Id;
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

            var builder = new StringBuilder();
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
                services.RemoveAll<IGitHubOriginObserver>();
                services.AddSingleton<IGitHubOriginObserver>(observer);
            });
        }
    }

    private sealed class FixedObserver(GitHubOriginObservation observation) : IGitHubOriginObserver
    {
        public List<(string Git, string Root)> Invocations { get; } = [];

        public Task<GitHubOriginObservation> ObserveAsync(
            string gitExecutablePath, RepositoryRootCandidate candidate, CancellationToken cancellationToken)
        {
            Invocations.Add((gitExecutablePath, candidate.CanonicalPath));
            return Task.FromResult(observation);
        }
    }
}
