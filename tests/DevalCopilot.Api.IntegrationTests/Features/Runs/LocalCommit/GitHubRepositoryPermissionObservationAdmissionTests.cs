using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0049 requests that cannot reach a delivery's handler logic, against the production host and file-backed SQLite: authentication,
/// the single routed method, the real-host MVC authorization inventory, the fixed 404, the fixed pre-dispatch query and body refusals and
/// the no-fact answer for a run with nothing recorded. No delivery runs here; each case proves the scripted permission observer is never
/// invoked and that nothing is written.
/// </summary>
public sealed class GitHubRepositoryPermissionObservationAdmissionTests : GitHubRepositoryPermissionObservationTestBase
{
    private (LocalCommitHost Host, ScriptedPermissionObserver Observer) StartScriptedHost()
    {
        var observer = new ScriptedPermissionObserver { Answer = () => PermissionProbe() };
        var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubRepositoryPermissionObserver>();
                services.AddSingleton<IGitHubRepositoryPermissionObserver>(observer);
            });
        return (host, observer);
    }

    private async Task<Guid> SeedBareRunAsync()
    {
        await using var db = OpenDb();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Observation", $@"C:\repos\{Guid.NewGuid():N}", now);
        var runId = Guid.NewGuid();
        db.Projects.Add(project);
        db.Runs.Add(Run.RecordIntent(runId, project.Id, project.ReserveExecutionNumber(), "Observe", now));
        await db.SaveChangesAsync();
        return runId;
    }

    [Fact]
    public async Task The_route_requires_authentication_routes_only_get_and_is_the_only_new_protected_route_in_the_real_authorization_inventory()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ObservePermissionAsync(host, runId, authenticated: false)).Status);
        using (var wrong = host.CreateClient())
        {
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync($"/api/runs/{runId}/{PermissionRoute}")).StatusCode);
        }

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var route = $"/api/runs/{runId}/{PermissionRoute}";
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var permission = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.Contains("github-repository-permission-observation", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("api/runs/{runId:guid}/github-repository-permission-observation", permission.RoutePattern.RawText);
        Assert.Equal(["GET"], permission.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(permission.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(permission.Metadata.GetOrderedMetadata<IAllowAnonymous>());

        // No anonymous additions: the only endpoint of the whole host that opts out of default-deny is the health probe.
        Assert.Equal(
            ["health"],
            endpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0)
                .Select(endpoint => endpoint.RoutePattern.RawText!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_404_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var before = await DatabaseFingerprintAsync();

        var unknown = await ObservePermissionAsync(host, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("github_repository_permission_observation.not_found", ErrorCode(unknown));
        Assert.Equal("urn:devalente:problem:not-found", unknown.Root.GetProperty("type").GetString());
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("?owner=sentinel-owner")]
    [InlineData("?owner=sentinel-owner&host=evil.example")]
    [InlineData("?a")]
    [InlineData("?a=")]
    [InlineData("?=x")]
    [InlineData("?runId=11111111-1111-1111-1111-111111111111")]
    [InlineData("?checkRunId=1")]
    [InlineData("?a=1&a=2")]
    [InlineData("?Owner=sentinel-owner")]
    [InlineData("?%6Fwner=sentinel-owner")]
    [InlineData("?x=%00")]
    public async Task Any_query_key_is_refused_with_a_fixed_error_before_dispatch_even_for_an_unknown_run_and_nothing_is_invoked_or_written(string query)
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        foreach (var id in new[] { runId, Guid.NewGuid() })
        {
            var refused = await ObservePermissionAsync(host, id, query: query);

            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_repository_permission_observation.invalid_request", ErrorCode(refused));
            Assert.DoesNotContain("sentinel-owner", refused.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("evil.example", refused.Body, StringComparison.Ordinal);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_query_key_is_refused_before_a_body_and_a_body_with_no_query_is_refused_as_a_body()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var both = await ObservePermissionAsync(host, runId, new StringContent("{}"), query: "?owner=x");
        var bodyOnly = await ObservePermissionAsync(host, runId, new StringContent("{}"));

        Assert.Equal("github_repository_permission_observation.invalid_request", ErrorCode(both));
        Assert.Equal("github_repository_permission_observation.body_not_allowed", ErrorCode(bodyOnly));
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_request_body_is_refused_with_a_fixed_error_before_dispatch_for_any_run_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();
        const string Sentinel = "sentinel-owner-evil";

        var declared = await ObservePermissionAsync(
            host, runId, new StringContent($"{{\"owner\":\"{Sentinel}\",\"url\":\"https://evil.example/x/y\"}}", Encoding.UTF8, "application/json"));
        var chunked = await ObservePermissionAsync(host, runId, new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")) { Position = 0 }));
        using var undeclaredContent = new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}")));
        var unsized = await ObservePermissionAsync(host, runId, undeclaredContent);
        var unknownRunWithBody = await ObservePermissionAsync(host, Guid.NewGuid(), new StringContent($"{{\"o\":\"{Sentinel}\"}}"));

        foreach (var refused in new[] { declared, chunked, unsized, unknownRunWithBody })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_repository_permission_observation.body_not_allowed", ErrorCode(refused));
            Assert.DoesNotContain(Sentinel, refused.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("evil.example", refused.Body, StringComparison.Ordinal);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_empty_chunked_request_with_no_declared_length_and_zero_bytes_is_refused_before_dispatch()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{PermissionRoute}")
        {
            Content = new StreamContent(new UnsizedStream([])),
        };
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);
        var reading = new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.BadRequest, reading.Status);
        Assert.Equal("github_repository_permission_observation.body_not_allowed", ErrorCode(reading));
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unauthenticated_request_with_a_body_or_a_query_is_refused_as_unauthorized_before_either_rule_and_nothing_is_invoked()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var body = await ObservePermissionAsync(host, runId, new StringContent("{}"), authenticated: false);
        var query = await ObservePermissionAsync(host, runId, authenticated: false, query: "?owner=x");

        Assert.Equal(HttpStatusCode.Unauthorized, body.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, query.Status);
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_run_with_no_target_and_no_binding_is_not_recorded_with_no_fact_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObservePermissionAsync(host, runId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(PermissionResponseKeys, KeysOf(reading.Root));
        Assert.Equal("NotRecorded", StatusOf(reading));
        Assert.Equal(runId, reading.Root.GetProperty("runId").GetGuid());
        AssertFacts(reading, source: false, target: false, binding: false, observation: false);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public void The_status_in_a_response_is_always_one_of_the_closed_eight_names_and_never_a_derived_capability_word()
    {
        var names = Enum.GetNames<GitHubRepositoryPermissionObservationStatus>();

        Assert.Equal(
            [
                "NotRecorded", "Unavailable", "BindingNotRecorded", "CliUnavailable", "Unknown", "IdentityChanged", "PermissionNotReturned",
                "PermissionReported",
            ],
            names);
        foreach (var forbidden in new[] { "CanPush", "Allowed", "Denied", "Ready", "Approved", "Eligible", "Granted" })
        {
            Assert.DoesNotContain(forbidden, names);
        }
    }
}
