using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
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
/// ADR-0045 requests that cannot reach a delivery's handler logic, against the production host and file-backed SQLite: authentication,
/// the single routed method, the real-host MVC authorization inventory, the fixed 404, the fixed pre-dispatch body refusal and the
/// no-fact answer for a run with nothing recorded. No delivery runs here; each case proves the scripted observer is never invoked and that
/// nothing is written.
/// </summary>
public sealed class GitHubCheckContextsObservationAdmissionTests : GitHubCheckContextsObservationTestBase
{
    private (LocalCommitHost Host, ScriptedContextsObserver Observer) StartScriptedHost()
    {
        var observer = new ScriptedContextsObserver { Answer = () => ContextsProbe() };
        var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubCheckContextsObserver>();
                services.AddSingleton<IGitHubCheckContextsObserver>(observer);
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

        Assert.Equal(HttpStatusCode.Unauthorized, (await ObserveContextsAsync(host, runId, authenticated: false)).Status);
        using (var wrong = host.CreateClient())
        {
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync($"/api/runs/{runId}/{ContextsRoute}")).StatusCode);
        }

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var route = $"/api/runs/{runId}/{ContextsRoute}";
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var contexts = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.Contains("github-check-contexts-observation", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("api/runs/{runId:guid}/github-check-contexts-observation", contexts.RoutePattern.RawText);
        Assert.Equal(["GET"], contexts.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(contexts.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(contexts.Metadata.GetOrderedMetadata<IAllowAnonymous>());

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

        var unknown = await ObserveContextsAsync(host, Guid.NewGuid());
        var withQuery = await ObserveContextsAsync(host, Guid.NewGuid(), query: "?owner=sentinel-owner&host=evil.example&first=500&after=abc");

        foreach (var reading in new[] { unknown, withQuery })
        {
            Assert.Equal(HttpStatusCode.NotFound, reading.Status);
            Assert.Equal("github_check_contexts_observation.not_found", ErrorCode(reading));
            Assert.Equal("urn:devalente:problem:not-found", reading.Root.GetProperty("type").GetString());
            Assert.DoesNotContain("sentinel-owner", reading.Body, StringComparison.Ordinal);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_run_with_no_target_and_no_binding_is_not_recorded_with_no_fact_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveContextsAsync(host, runId, query: "?owner=sentinel-owner");

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(ContextsResponseKeys, KeysOf(reading.Root));
        Assert.Equal("NotRecorded", StatusOf(reading));
        Assert.Equal(runId, reading.Root.GetProperty("runId").GetGuid());
        AssertFacts(reading, source: false, target: false, binding: false, observation: false);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_request_body_is_refused_with_a_fixed_error_before_dispatch_for_any_run_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();
        const string Sentinel = "sentinel-owner-evil";

        var declared = await ObserveContextsAsync(
            host, runId, new StringContent($"{{\"owner\":\"{Sentinel}\",\"url\":\"https://evil.example/x/y\"}}", Encoding.UTF8, "application/json"));
        var chunked = await ObserveContextsAsync(host, runId, new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")) { Position = 0 }));
        using var undeclaredContent = new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}")));
        var unsized = await ObserveContextsAsync(host, runId, undeclaredContent);
        var unknownRunWithBody = await ObserveContextsAsync(host, Guid.NewGuid(), new StringContent($"{{\"o\":\"{Sentinel}\"}}"));

        foreach (var refused in new[] { declared, chunked, unsized, unknownRunWithBody })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_check_contexts_observation.body_not_allowed", ErrorCode(refused));
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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{ContextsRoute}")
        {
            Content = new StreamContent(new UnsizedStream([])),
        };
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);
        var reading = new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.BadRequest, reading.Status);
        Assert.Equal("github_check_contexts_observation.body_not_allowed", ErrorCode(reading));
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unauthenticated_request_with_a_body_is_refused_as_unauthorized_before_the_body_rule_and_nothing_is_invoked()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var reading = await ObserveContextsAsync(host, runId, new StringContent("{}"), authenticated: false);

        Assert.Equal(HttpStatusCode.Unauthorized, reading.Status);
        Assert.Empty(observer.Calls);
    }
}
