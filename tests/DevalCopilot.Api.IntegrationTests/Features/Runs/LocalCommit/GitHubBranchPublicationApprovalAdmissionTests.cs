using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048 requests that cannot reach a delivery's handler logic, against the production host and file-backed SQLite: authentication, the
/// two routed methods, the real-host MVC authorization inventory, the fixed 404, the no-fact answer for a run with nothing recorded, and
/// every pre-dispatch refusal (query keys, a body of the wrong shape, spelling, size or content type, and any body on the read). Each case
/// proves nothing is written and that no stored, submitted or remote value is echoed.
/// </summary>
public sealed class GitHubBranchPublicationApprovalAdmissionTests : GitHubBranchPublicationApprovalTestBase
{
    private const string Sentinel = "sentinel-value-that-must-never-be-echoed";

    private static readonly string[] Names =
        ["approvalId", "operationId", "commitSha", "branchName", "targetId", "bindingId", "repositoryNodeId"];

    private static readonly string[] GuidNames = ["approvalId", "operationId", "targetId", "bindingId"];

    private static Dictionary<string, string> ValidValues() => new(StringComparer.Ordinal)
    {
        ["approvalId"] = Guid.NewGuid().ToString(),
        ["operationId"] = Guid.NewGuid().ToString(),
        ["commitSha"] = new string('a', 40),
        ["branchName"] = "devalcopilot/workspace/abc/1",
        ["targetId"] = Guid.NewGuid().ToString(),
        ["bindingId"] = Guid.NewGuid().ToString(),
        ["repositoryNodeId"] = NodeId,
    };

    private static string Quote(string value) => "\"" + value + "\"";

    private static string Json(IEnumerable<(string Name, string Raw)> members) =>
        "{" + string.Join(",", members.Select(member => $"\"{member.Name}\":{member.Raw}")) + "}";

    private static string ValidJson(Dictionary<string, string>? values = null) =>
        Json((values ?? ValidValues()).Select(pair => (pair.Key, Quote(pair.Value))));

    private (LocalCommitHost Host, Guid RunId) StartWithBareRun()
    {
        var host = StartHost(runSupervisor: false);
        var runId = SeedBareRunAsync().GetAwaiter().GetResult();
        return (host, runId);
    }

    private async Task<Guid> SeedBareRunAsync()
    {
        await using var db = OpenDb();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Approval", $@"C:\repos\{Guid.NewGuid():N}", now);
        var runId = Guid.NewGuid();
        db.Projects.Add(project);
        db.Runs.Add(Run.RecordIntent(runId, project.Id, project.ReserveExecutionNumber(), "Approve", now));
        await db.SaveChangesAsync();
        return runId;
    }

    [Fact]
    public async Task The_route_requires_authentication_routes_only_get_and_post_and_is_protected_in_the_real_authorization_inventory()
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();
        var route = $"/api/runs/{runId}/{ApprovalRoute}";

        using (var anonymous = host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(route, new StringContent(ValidJson(), Encoding.UTF8, "application/json"))).StatusCode);
        }

        using (var wrong = host.CreateClient())
        {
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.PostAsync(route, new StringContent(ValidJson(), Encoding.UTF8, "application/json"))).StatusCode);
        }

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(before, await DatabaseFingerprintAsync());

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var approvalEndpoints = endpoints.Where(endpoint =>
            endpoint.RoutePattern.RawText?.Contains("github-branch-publication-approval", StringComparison.OrdinalIgnoreCase) == true).ToList();
        Assert.Equal(2, approvalEndpoints.Count);
        Assert.All(approvalEndpoints, endpoint =>
        {
            Assert.Equal("api/runs/{runId:guid}/github-branch-publication-approval", endpoint.RoutePattern.RawText);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        });
        Assert.Equal(
            ["GET", "POST"],
            approvalEndpoints.Select(endpoint => Assert.Single(endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods))
                .Order(StringComparer.Ordinal));

        // No anonymous additions: the only endpoint of the whole host that opts out of default-deny is the health probe.
        Assert.Equal(
            ["health"],
            endpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0)
                .Select(endpoint => endpoint.RoutePattern.RawText!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_404_for_the_read_and_the_write_and_nothing_is_written()
    {
        var host = StartHost(runSupervisor: false);
        var before = await DatabaseFingerprintAsync();

        var read = await GetApprovalAsync(host, Guid.NewGuid());
        var write = await PostApprovalAsync(host, Guid.NewGuid(), ValidJson());

        foreach (var unknown in new[] { read, write })
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
            Assert.Equal("github_branch_publication_approval.not_found", ErrorCode(unknown));
            Assert.Equal("urn:devalente:problem:not-found", unknown.Root.GetProperty("type").GetString());
        }

        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_run_with_nothing_recorded_reads_as_not_recorded_with_no_fact_and_the_write_is_unavailable()
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();

        var read = await GetApprovalAsync(host, runId);
        var write = await PostApprovalAsync(host, runId, ValidJson());

        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal(GetKeys, KeysOf(read.Root));
        Assert.Equal("NotRecorded", StatusOf(read));
        Assert.Equal(runId, read.Root.GetProperty("runId").GetGuid());
        foreach (var member in new[] { "source", "target", "binding", "approval" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, read.Root.GetProperty(member).ValueKind);
        }

        Assert.Equal(HttpStatusCode.Conflict, write.Status);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(write));
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    public static TheoryData<string, string> InvalidBodies()
    {
        var data = new TheoryData<string, string>
        {
            { "empty body", string.Empty },
            { "whitespace only", "   " },
            { "null", "null" },
            { "array", "[]" },
            { "string", "\"x\"" },
            { "number", "123" },
            { "empty object", "{}" },
            { "unterminated object", ValidJson()[..^1] },
            { "trailing garbage", ValidJson() + " x" },
            { "two objects", ValidJson() + ValidJson() },
            { "trailing comma", ValidJson()[..^1] + ",}" },
            { "comment", ValidJson()[..^1] + "/*x*/}" },
            { "byte order mark", "\ufeff" + ValidJson() },
        };

        var valid = ValidValues();
        data.Add("unknown extra member", Json(valid.Select(pair => (pair.Key, Quote(pair.Value))).Append(("host", Quote(Sentinel)))));
        data.Add("caller supplied owner", Json(valid.Select(pair => (pair.Key, Quote(pair.Value))).Append(("owner", Quote(Sentinel)))));
        data.Add("caller supplied timestamp", Json(valid.Select(pair => (pair.Key, Quote(pair.Value))).Append(("approvedAtUtc", Quote(Sentinel)))));
        foreach (var name in Names)
        {
            var others = valid.Where(pair => pair.Key != name).Select(pair => (pair.Key, Quote(pair.Value)));
            data.Add($"missing {name}", Json(others));
            data.Add($"duplicate {name}", Json(valid.Select(pair => (pair.Key, Quote(pair.Value))).Append((name, Quote(valid[name])))));
            data.Add($"{name} in another case", Json(valid.Select(pair => (pair.Key == name ? char.ToUpperInvariant(name[0]) + name[1..] : pair.Key, Quote(pair.Value)))));
            data.Add($"{name} all upper case", Json(valid.Select(pair => (pair.Key == name ? name.ToUpperInvariant() : pair.Key, Quote(pair.Value)))));
            data.Add($"{name} spelled with an escape", Json(valid.Select(pair => (pair.Key == name ? "\\u" + ((int)name[0]).ToString("x4") + name[1..] : pair.Key, Quote(pair.Value)))));
            foreach (var (kind, raw) in new[] { ("a number", "1"), ("null", "null"), ("true", "true"), ("an object", "{}"), ("an array", "[]") })
            {
                data.Add($"{name} as {kind}", Json(valid.Select(pair => (pair.Key, pair.Key == name ? raw : Quote(pair.Value)))));
            }
        }

        foreach (var name in GuidNames)
        {
            var guid = Guid.NewGuid();
            foreach (var (kind, text) in new[]
                     {
                         ("without hyphens", guid.ToString("N")), ("in braces", guid.ToString("B")), ("in parentheses", guid.ToString("P")),
                         ("empty", Guid.Empty.ToString()), ("blank", string.Empty), ("padded", " " + guid + " "), ("text", Sentinel),
                     })
            {
                data.Add($"{name} {kind}", ValidJson(new Dictionary<string, string>(valid) { [name] = text }));
            }
        }

        foreach (var (kind, text) in new[]
                 {
                     ("sha in upper case", new string('A', 40)), ("sha of 39", new string('a', 39)), ("sha of 41", new string('a', 41)),
                     ("sha not hex", new string('g', 40)), ("sha blank", string.Empty), ("sha padded", " " + new string('a', 40)),
                 })
        {
            data.Add(kind, ValidJson(new Dictionary<string, string>(valid) { ["commitSha"] = text }));
        }

        foreach (var (kind, text) in new[]
                 {
                     ("node with a space", "R_a b"), ("node empty", string.Empty), ("node over 256", new string('n', 257)),
                     ("node with a dot", "R.dot"), ("node with a non-ASCII letter", "R_\u00e9"),
                 })
        {
            data.Add(kind, ValidJson(new Dictionary<string, string>(valid) { ["repositoryNodeId"] = text }));
        }

        data.Add("branch empty", ValidJson(new Dictionary<string, string>(valid) { ["branchName"] = string.Empty }));
        data.Add("branch over 512", ValidJson(new Dictionary<string, string>(valid) { ["branchName"] = new string('b', 513) }));
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task A_body_outside_the_contract_is_the_fixed_invalid_request_without_dispatch_or_writes_and_is_never_echoed(
        string name, string body)
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();

        var refused = await PostApprovalAsync(host, runId, body);

        Assert.True(refused.Status == HttpStatusCode.BadRequest, $"{name}: {refused.Status} {refused.Body}");
        Assert.True(ErrorCode(refused) == "github_branch_publication_approval.invalid_request", $"{name}: {refused.Body}");
        Assert.DoesNotContain(Sentinel, refused.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("devalcopilot/workspace", refused.Body, StringComparison.Ordinal);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("?x=1")]
    [InlineData("?x")]
    [InlineData("?=")]
    [InlineData("?approvalId=00000000-0000-0000-0000-000000000001")]
    [InlineData("?a=1&a=2")]
    public async Task A_query_key_on_either_route_is_the_fixed_invalid_request_and_nothing_is_dispatched_or_written(string query)
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();

        var write = await PostApprovalAsync(host, runId, ValidJson(), query);
        var read = await GetApprovalAsync(host, runId, query: query);

        foreach (var refused in new[] { write, read })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_branch_publication_approval.invalid_request", ErrorCode(refused));
        }

        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task A_non_json_content_type_keeps_the_standard_415_and_writes_nothing(string contentType)
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();

        var refused = await PostApprovalAsync(host, runId, ValidJson(), contentType: contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData(8193, false)]
    [InlineData(20000, false)]
    [InlineData(8193, true)]
    [InlineData(1_000_000, true)]
    public async Task A_body_over_eight_kibibytes_is_the_fixed_413_declared_or_chunked_and_writes_nothing(int length, bool chunked)
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();
        var json = ValidJson();
        var padded = json + new string(' ', length - json.Length);
        using var content = chunked
            ? new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes(padded)))
            : (HttpContent)new StringContent(padded, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var refused = await PostApprovalContentAsync(host, runId, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.Status);
        Assert.Equal("github_branch_publication_approval.body_too_large", ErrorCode(refused));
        Assert.Equal(413, refused.Root.GetProperty("status").GetInt32());
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_body_of_exactly_eight_kibibytes_passes_the_size_gate_declared_or_chunked(bool chunked)
    {
        var (host, runId) = StartWithBareRun();
        var json = ValidJson();
        var padded = json + new string(' ', 8192 - json.Length);
        using var content = chunked
            ? new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes(padded)))
            : (HttpContent)new StringContent(padded, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var reached = await PostApprovalContentAsync(host, runId, content);

        // The bare run has no scope, so the only refusal left is the handler's own: the body was admitted whole.
        Assert.Equal(HttpStatusCode.Conflict, reached.Status);
        Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(reached));
    }

    [Fact]
    public async Task A_read_that_carries_any_body_is_the_fixed_body_not_allowed_before_dispatch()
    {
        var (host, runId) = StartWithBareRun();
        var before = await DatabaseFingerprintAsync();

        var withLength = await GetApprovalAsync(host, runId, new StringContent("{}", Encoding.UTF8, "application/json"));
        var chunked = await GetApprovalAsync(host, runId, new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}"))));
        var emptyChunked = await GetApprovalAsync(host, runId, new StreamContent(new UnsizedStream([])));

        foreach (var refused in new[] { withLength, chunked, emptyChunked })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_branch_publication_approval.body_not_allowed", ErrorCode(refused));
        }

        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Equal("NotRecorded", StatusOf(await GetApprovalAsync(host, runId)));
    }

    [Fact]
    public async Task The_fixed_problem_details_name_the_published_types_and_repeat_nothing_the_caller_sent()
    {
        var (host, runId) = StartWithBareRun();

        var invalid = await PostApprovalAsync(host, runId, "{\"x\":\"" + Sentinel + "\"}");
        var read = await GetApprovalAsync(host, Guid.NewGuid());

        Assert.Equal("urn:devalente:problem:validation", invalid.Root.GetProperty("type").GetString());
        Assert.Equal("urn:devalente:problem:not-found", read.Root.GetProperty("type").GetString());
        Assert.DoesNotContain(Sentinel, invalid.Body, StringComparison.Ordinal);
    }
}
