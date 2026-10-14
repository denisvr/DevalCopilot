using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
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
/// ADR-0047 requests that cannot reach a delivery's handler logic, against the production host and file-backed SQLite: authentication,
/// the single routed method, the real-host MVC authorization inventory, the fixed 404, the pre-dispatch query and body refusals and the
/// no-fact answer for a run with nothing recorded. No delivery runs here; each case proves the scripted observer is never invoked and
/// that nothing is written.
/// </summary>
public sealed class GitHubCheckRunOutputObservationAdmissionTests : GitHubCheckRunOutputObservationTestBase
{
    private (LocalCommitHost Host, ScriptedOutputObserver Observer) StartScriptedHost()
    {
        var observer = new ScriptedOutputObserver
        {
            Metadata = () => Probe(null, null),
            Output = () => Probe(null, null),
        };
        var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubCheckRunOutputObserver>();
                services.AddSingleton<IGitHubCheckRunOutputObserver>(observer);
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
    public async Task The_route_requires_authentication_routes_only_get_and_is_protected_in_the_real_authorization_inventory()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ObserveOutputAsync(host, runId, authenticated: false)).Status);
        using (var wrong = host.CreateClient())
        {
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync($"/api/runs/{runId}/{OutputRoute}{Query()}")).StatusCode);
        }

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var route = $"/api/runs/{runId}/{OutputRoute}{Query()}";
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var inspection = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.Contains("github-check-run-output-observation", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("api/runs/{runId:guid}/github-check-run-output-observation", inspection.RoutePattern.RawText);
        Assert.Equal(["GET"], inspection.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(inspection.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(inspection.Metadata.GetOrderedMetadata<IAllowAnonymous>());

        // No anonymous additions: the only endpoint of the whole host that opts out of default-deny is the health probe.
        Assert.Equal(
            ["health"],
            endpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0)
                .Select(endpoint => endpoint.RoutePattern.RawText!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_unknown_run_with_a_valid_query_is_the_fixed_404_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var before = await DatabaseFingerprintAsync();

        var unknown = await ObserveOutputAsync(host, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("github_check_run_output_observation.not_found", ErrorCode(unknown));
        Assert.Equal("urn:devalente:problem:not-found", unknown.Root.GetProperty("type").GetString());
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_run_with_no_target_and_no_binding_is_not_recorded_echoes_only_the_validated_identifier_and_invokes_nothing()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveOutputAsync(host, runId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(OutputResponseKeys, KeysOf(reading.Root));
        Assert.Equal("NotRecorded", StatusOf(reading));
        Assert.Equal(runId, reading.Root.GetProperty("runId").GetGuid());
        Assert.Equal(CheckId, reading.Root.GetProperty("checkRunId").GetString());
        AssertFacts(reading, source: false, target: false, binding: false, observation: false);
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    public static TheoryData<string, string> InvalidQueries() => new()
    {
        { "no query at all", string.Empty },
        { "bare question mark", "?" },
        { "key without a value", "?checkRunId" },
        { "empty value", "?checkRunId=" },
        { "duplicate key", "?checkRunId=CR_1&checkRunId=CR_2" },
        { "duplicate equal key", "?checkRunId=CR_1&checkRunId=CR_1" },
        { "duplicate key in another case", "?checkRunId=CR_1&CheckRunId=CR_2" },
        { "extra key", "?checkRunId=CR_1&owner=sentinel-owner" },
        { "extra empty key", "?checkRunId=CR_1&x" },
        { "extra key first", "?owner=sentinel-owner&checkRunId=CR_1" },
        { "differently spelled key", "?CheckRunId=CR_1" },
        { "upper case key", "?CHECKRUNID=CR_1" },
        { "another key only", "?id=CR_1" },
        { "leading space", "?checkRunId=%20CR_1" },
        { "trailing space", "?checkRunId=CR_1%20" },
        { "inner space", "?checkRunId=CR%201" },
        { "plus decoded to a space", "?checkRunId=CR+1" },
        { "dot", "?checkRunId=CR.1" },
        { "colon", "?checkRunId=CR:1" },
        { "comma", "?checkRunId=CR,1" },
        { "percent escape of a letter kept decoded", "?checkRunId=CR%FF" },
        { "newline", "?checkRunId=CR%0A1" },
        { "tab", "?checkRunId=CR%091" },
        { "null character", "?checkRunId=CR%001" },
        { "backslash", "?checkRunId=CR%5C1" },
        { "quote", "?checkRunId=CR%221" },
        { "non ascii letter", "?checkRunId=CR_%C3%A9" },
        { "full width letter", "?checkRunId=%EF%BC%A1" },
        { "257 characters", "?checkRunId=" + new string('a', 257) },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task A_missing_duplicate_extra_or_invalid_query_is_refused_before_dispatch_for_any_run_and_never_echoed(
        string description, string query)
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();

        var known = await ObserveOutputAsync(host, runId, query: query);
        var unknown = await ObserveOutputAsync(host, Guid.NewGuid(), query: query);

        foreach (var refused in new[] { known, unknown })
        {
            Assert.True(refused.Status == HttpStatusCode.BadRequest, $"{description}: {refused.Status} {refused.Body}");
            Assert.Equal("github_check_run_output_observation.invalid_request", ErrorCode(refused));
            Assert.DoesNotContain("sentinel-owner", refused.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("CR_2", refused.Body, StringComparison.Ordinal);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("CR_kwDOCheck001")]
    [InlineData("_-/+=")]
    [InlineData("0")]
    public async Task A_valid_identifier_in_the_contract_alphabet_reaches_the_handler_and_is_echoed_exactly(string checkRunId)
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var reading = await ObserveOutputAsync(host, runId, query: Query(checkRunId));

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("NotRecorded", StatusOf(reading));
        Assert.Equal(checkRunId, reading.Root.GetProperty("checkRunId").GetString());
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_valid_identifier_of_exactly_256_characters_is_admitted_and_257_is_refused()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var longest = await ObserveOutputAsync(host, runId, query: Query(new string('a', 256)));
        var tooLong = await ObserveOutputAsync(host, runId, query: Query(new string('a', 257)));

        Assert.Equal(HttpStatusCode.OK, longest.Status);
        Assert.Equal(new string('a', 256), longest.Root.GetProperty("checkRunId").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
        Assert.Equal("github_check_run_output_observation.invalid_request", ErrorCode(tooLong));
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_request_body_is_refused_with_a_fixed_error_before_dispatch_for_any_run_and_nothing_is_invoked_or_written()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();
        var before = await DatabaseFingerprintAsync();
        const string Sentinel = "sentinel-owner-evil";

        var declared = await ObserveOutputAsync(
            host, runId, new StringContent($"{{\"owner\":\"{Sentinel}\",\"url\":\"https://evil.example/x/y\"}}", Encoding.UTF8, "application/json"));
        var chunked = await ObserveOutputAsync(host, runId, new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")) { Position = 0 }));
        using var undeclaredContent = new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}")));
        var unsized = await ObserveOutputAsync(host, runId, undeclaredContent);
        var unknownRunWithBody = await ObserveOutputAsync(host, Guid.NewGuid(), new StringContent($"{{\"o\":\"{Sentinel}\"}}"));

        foreach (var refused in new[] { declared, chunked, unsized, unknownRunWithBody })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_check_run_output_observation.body_not_allowed", ErrorCode(refused));
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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{OutputRoute}{Query()}")
        {
            Content = new StreamContent(new UnsizedStream([])),
        };
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);
        var reading = new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.BadRequest, reading.Status);
        Assert.Equal("github_check_run_output_observation.body_not_allowed", ErrorCode(reading));
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_body_beside_an_invalid_query_is_refused_as_a_body_first_and_a_valid_body_free_invalid_query_as_an_invalid_request()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var both = await ObserveOutputAsync(host, runId, new StringContent("{}"), query: "?checkRunId=bad%20id");
        var queryOnly = await ObserveOutputAsync(host, runId, query: "?checkRunId=bad%20id");

        Assert.Equal("github_check_run_output_observation.body_not_allowed", ErrorCode(both));
        Assert.Equal("github_check_run_output_observation.invalid_request", ErrorCode(queryOnly));
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task An_unauthenticated_request_with_a_body_or_an_invalid_query_is_unauthorized_first_and_nothing_is_invoked()
    {
        var (host, observer) = StartScriptedHost();
        var runId = await SeedBareRunAsync();

        var withBody = await ObserveOutputAsync(host, runId, new StringContent("{}"), authenticated: false);
        var invalid = await ObserveOutputAsync(host, runId, authenticated: false, query: "?checkRunId=bad%20id");

        Assert.Equal(HttpStatusCode.Unauthorized, withBody.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.Status);
        Assert.Empty(observer.Calls);
    }
}
