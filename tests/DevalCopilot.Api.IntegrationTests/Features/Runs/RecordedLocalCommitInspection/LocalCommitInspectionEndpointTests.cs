using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>
/// ADR-0070's one protected, bodyless recorded read through the real host and the real production wiring: authentication, verbs and the
/// MVC authorization inventory, the strict body and query refusals before dispatch with the fixed non-echoing errors, the fixed 404 and
/// validation, the closed wire of all three answer states and every operation state (the commit identity only for Completed, canonical
/// times and the allowlisted reasons), Unavailable over damaged storage, the absence of any write and Git call, and the actual OpenAPI
/// contract.
/// </summary>
public sealed class LocalCommitInspectionEndpointTests : LocalCommitInspectionApiTestBase
{
    private static readonly string[] AnswerMembers = ["runId", "status", "operation"];

    private static readonly string[] OperationMembers =
    [
        "operationId", "projectId", "workspaceId", "checkpointId", "checkpointNumber", "checkpointFingerprintSha256", "status", "branchName",
        "parentCommitSha", "treeSha", "commitSha", "changedPathCount", "totalBytes", "createdAtUtc", "executionStartedAtUtc", "completedAtUtc",
        "reason",
    ];

    public static TheoryData<LocalCommitInspectionApiShape> Shapes => new(Enum.GetValues<LocalCommitInspectionApiShape>());

    // ---- authentication, methods and the MVC authorization inventory -------------------------------------------------------------------

    [Fact]
    public async Task The_read_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        var route = Route(scene.RunId);
        using var anonymous = Host.CreateClient();
        using var wrong = Host.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        using var queryOnly = Host.CreateClient();
        using var client = AuthenticatedClient();
        var before = await FingerprintAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public void The_route_exists_once_as_an_authorized_get_and_no_local_commit_route_is_anonymous()
    {
        var all = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var endpoint = Assert.Single(
            all, candidate => candidate.RoutePattern.RawText?.Contains("local-commit-inspection", StringComparison.Ordinal) == true);
        Assert.Equal("api/runs/{runId:guid}/local-commit-inspection", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());

        // The live status, request and receipt routes keep their own authorization: nothing here added an anonymous endpoint.
        Assert.DoesNotContain(
            all.Where(candidate => candidate.RoutePattern.RawText?.StartsWith("api/runs", StringComparison.Ordinal) == true),
            candidate => candidate.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0);
    }

    // ---- bodies and queries are refused before dispatch --------------------------------------------------------------------------------

    private static IEnumerable<(string Name, Func<HttpRequestMessage, HttpRequestMessage> Shape)> BodyShapes() =>
    [
        ("declared length", request =>
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return request;
        }),
        ("chunked bytes", request =>
        {
            request.Content = new StreamContent(new MemoryStream("x"u8.ToArray()));
            request.Headers.TransferEncodingChunked = true;
            return request;
        }),
        ("empty chunked", request =>
        {
            request.Content = new StreamContent(new MemoryStream());
            request.Headers.TransferEncodingChunked = true;
            return request;
        }),
    ];

    [Fact]
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_even_for_a_nil_or_unknown_run()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        var before = await FingerprintAsync();

        // The nil run would be a validation failure if the request were dispatched, so the body refusal proves refusal before dispatch.
        foreach (var route in new[] { Route(Guid.Empty), Route(Guid.NewGuid()), Route(scene.RunId) })
        {
            foreach (var (name, shape) in BodyShapes())
            {
                using var client = AuthenticatedClient();
                using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, route)));
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{name} {route}");
                var error = Assert.Single(JsonNode.Parse(text)!["errors"]!.AsArray())!;
                Assert.Equal(LocalCommitInspectionErrors.BodyNotAllowedCode, error["code"]!.GetValue<string>());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, await FingerprintAsync());
        Assert.Empty(Tripwire.Calls);
    }

    [Fact]
    public async Task A_readable_body_byte_without_a_declared_length_or_transfer_encoding_is_refused_before_dispatch()
    {
        // The nil run would be a validation failure if the request were dispatched; no content length and no transfer encoding are declared,
        // so only the one-byte peek can see the body.
        var context = await Host.Server.SendAsync(http =>
        {
            http.Request.Method = "GET";
            http.Request.Path = $"/api/runs/{Guid.Empty}/local-commit-inspection";
            http.Request.Headers.Authorization = $"Bearer {ApiWebApplicationFactory.ValidSecret}";
            http.Request.Body = new MemoryStream("x"u8.ToArray());
            http.Request.ContentLength = null;
            http.Request.Headers.Remove("Transfer-Encoding");
        });

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        using var reader = new StreamReader(context.Response.Body);
        var error = Assert.Single(JsonNode.Parse(await reader.ReadToEndAsync())!["errors"]!.AsArray())!;
        Assert.Equal(LocalCommitInspectionErrors.BodyNotAllowedCode, error["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("=")]
    [InlineData("x")]
    [InlineData("x=")]
    [InlineData("a=1&b=2")]
    [InlineData("&")]
    [InlineData("runId=1")]
    [InlineData("RunId=")]
    [InlineData("runid=1")]
    [InlineData("x=1&x=2")]
    [InlineData("x=1&X=2")]
    [InlineData("access_token=secret")]
    public async Task Every_supplied_query_key_is_refused_with_the_fixed_400_before_dispatch_even_for_a_nil_or_unknown_run(string query)
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);

        foreach (var route in new[] { Route(Guid.Empty, "?" + query), Route(Guid.NewGuid(), "?" + query), Route(scene.RunId, "?" + query) })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalCommitInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.Equal("This request does not accept these query parameters.", error.GetProperty("detail").GetString());
            Assert.DoesNotContain(query, response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_lone_question_mark_is_an_empty_query_and_reads_normally()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);

        var response = await GetAsync(Route(scene.RunId, "?"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
    }

    // ---- fixed refusals --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_and_a_run_without_its_registered_project_are_one_fixed_404_that_repeats_no_value()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        var orphan = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        await ExecuteAsync($"DELETE FROM projects WHERE Id = {orphan.ProjectId}");
        var unknown = Guid.NewGuid();

        var responses = new List<(HttpStatusCode Status, System.Text.Json.JsonElement Body, string Text)>
        {
            await GetAsync(Route(unknown)),
            await GetAsync(Route(orphan.RunId)),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalCommitInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            Assert.Equal("The requested local commit inspection was not found.", error.GetProperty("detail").GetString());

            // The shared Problem Details contract names the request path in its own instance member; nothing else repeats a value.
            var content = Normalize(response.Text);
            Assert.DoesNotContain(unknown.ToString(), content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(orphan.RunId.ToString(), content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(orphan.ProjectId.ToString(), content, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(Normalize(responses[0].Text), Normalize(responses[1].Text));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Route(scene.RunId))).Status);
    }

    [Fact]
    public async Task The_nil_run_identity_is_a_fixed_validation_failure_and_a_non_guid_route_is_not_routed()
    {
        var response = await GetAsync(Route(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(LocalCommitInspectionErrors.InvalidRunCode, error.GetProperty("code").GetString());
        Assert.Equal(LocalCommitInspectionErrors.InvalidRunMessage, error.GetProperty("detail").GetString());

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/not-a-guid/local-commit-inspection")).StatusCode);
    }

    // ---- the closed wire ---------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Every_recorded_state_is_the_closed_graph_with_canonical_strings_and_the_commit_only_for_Completed(
        LocalCommitInspectionApiShape shape)
    {
        var scene = await SeedAsync(shape);
        var before = await FingerprintAsync();

        var response = await GetAsync(Route(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(AnswerMembers, Names(response.Body));
        Assert.Equal(scene.RunId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        var operation = response.Body.GetProperty("operation");
        Assert.Equal(OperationMembers, Names(operation));
        Assert.Equal(scene.OperationId, operation.GetProperty("operationId").GetGuid());
        Assert.Equal(scene.ProjectId, operation.GetProperty("projectId").GetGuid());
        Assert.Equal(scene.WorkspaceId, operation.GetProperty("workspaceId").GetGuid());
        Assert.Equal(scene.CheckpointId, operation.GetProperty("checkpointId").GetGuid());
        Assert.Equal(1, operation.GetProperty("checkpointNumber").GetInt32());
        Assert.Equal(LocalCommitInspectionApiSeed.Fingerprint, operation.GetProperty("checkpointFingerprintSha256").GetString());
        Assert.Equal(scene.Branch, operation.GetProperty("branchName").GetString());
        Assert.Equal(LocalCommitInspectionApiSeed.Parent, operation.GetProperty("parentCommitSha").GetString());
        Assert.Equal(LocalCommitInspectionApiSeed.Tree, operation.GetProperty("treeSha").GetString());
        Assert.Equal(1, operation.GetProperty("changedPathCount").GetInt32());
        Assert.Equal(10, operation.GetProperty("totalBytes").GetInt32());
        Assert.Equal(Time(LocalCommitInspectionApiSeed.Created), operation.GetProperty("createdAtUtc").GetString());

        var (status, started, finished) = shape switch
        {
            LocalCommitInspectionApiShape.Prepared => ("Prepared", false, false),
            LocalCommitInspectionApiShape.Executing => ("Executing", true, false),
            LocalCommitInspectionApiShape.Completed => ("Completed", true, true),
            LocalCommitInspectionApiShape.FailedStarted => ("Failed", true, true),
            LocalCommitInspectionApiShape.FailedUnstarted => ("Failed", false, true),
            LocalCommitInspectionApiShape.InterruptedStarted => ("Interrupted", true, true),
            LocalCommitInspectionApiShape.InterruptedUnstarted => ("Interrupted", false, true),
            LocalCommitInspectionApiShape.AttentionStarted => ("NeedsAttention", true, false),
            _ => ("NeedsAttention", false, false),
        };
        Assert.Equal(status, operation.GetProperty("status").GetString());
        Assert.Equal(started ? Time(LocalCommitInspectionApiSeed.Started) : null, operation.GetProperty("executionStartedAtUtc").GetString());
        Assert.Equal(finished ? Time(LocalCommitInspectionApiSeed.Finished) : null, operation.GetProperty("completedAtUtc").GetString());

        // Only Completed carries the commit identity; the prepared proposed commit never appears anywhere in the body otherwise.
        Assert.Equal(status == "Completed" ? scene.ProposedCommit : null, operation.GetProperty("commitSha").GetString());
        Assert.Equal(status == "Completed", response.Text.Contains(scene.ProposedCommit, StringComparison.Ordinal));

        var reason = operation.GetProperty("reason");
        Assert.Equal(["kind", "code"], Names(reason));
        var hasReason = status is "Failed" or "Interrupted" or "NeedsAttention";
        Assert.Equal(hasReason ? "Known" : "None", reason.GetProperty("kind").GetString());
        Assert.Equal(hasReason ? "local_commit.git_unprovable" : null, reason.GetProperty("code").GetString());

        Assert.Equal(before, await FingerprintAsync());
        Assert.Empty(Tripwire.Calls);
    }

    [Fact]
    public async Task An_unrecognized_reason_is_returned_with_no_code_and_no_raw_text()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.AttentionStarted, @"local_commit.native: C:\secret\repo\.git\index.lock exists");

        var response = await GetAsync(Route(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var reason = response.Body.GetProperty("operation").GetProperty("reason");
        Assert.Equal("Unrecognized", reason.GetProperty("kind").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reason.GetProperty("code").ValueKind);
        Assert.DoesNotContain("secret", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("index.lock", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_without_an_operation_is_NotRecorded_with_a_null_operation_beside_a_recorded_run()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        var later = await WithDbAsync(db => LocalCommitInspectionApiSeed.SeedRunWithoutOperationAsync(db, scene.ProjectId));

        var recorded = await GetAsync(Route(scene.RunId));
        var absent = await GetAsync(Route(later));

        Assert.Equal("Recorded", recorded.Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, absent.Status);
        Assert.Equal(AnswerMembers, Names(absent.Body));
        Assert.Equal(later, absent.Body.GetProperty("runId").GetGuid());
        Assert.Equal("NotRecorded", absent.Body.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, absent.Body.GetProperty("operation").ValueKind);
    }

    [Fact]
    public async Task A_damaged_operation_is_Unavailable_with_a_null_operation_never_absent_and_never_a_fault()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        await ExecuteAsync($"UPDATE local_commit_operations SET Status = 'Bogus' WHERE Id = {scene.OperationId}");

        var response = await GetAsync(Route(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(AnswerMembers, Names(response.Body));
        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, response.Body.GetProperty("operation").ValueKind);
        Assert.DoesNotContain(scene.ProposedCommit, response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bogus", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_real_database_fault_is_the_normal_failure_boundary_never_Unavailable_or_NotRecorded()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.Completed);
        await ExecuteAsync($"DROP TABLE local_commit_operations");

        var response = await GetAsync(Route(scene.RunId));

        // The test host's own failure boundary answers; the read neither relabels the fault nor answers any of its three statuses.
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal(500, response.Body.GetProperty("status").GetInt32());
        Assert.False(response.Body.TryGetProperty("operation", out _));
        Assert.False(response.Body.TryGetProperty("runId", out _));
        Assert.DoesNotContain(scene.ProposedCommit, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_reads_change_no_durable_fact_and_reach_no_git_adapter()
    {
        var scene = await SeedAsync(LocalCommitInspectionApiShape.AttentionStarted);
        var before = await FingerprintAsync();

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal("Recorded", (await GetAsync(Route(scene.RunId))).Body.GetProperty("status").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(Route(Guid.NewGuid()))).Status);
        Assert.Equal(before, await FingerprintAsync());
        Assert.Empty(Tripwire.Calls);
    }

    // ---- OpenAPI ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_one_bodyless_route_and_its_closed_responses()
    {
        var generator = Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var paths = document["paths"]!.AsObject();

        var operation = paths["/api/runs/{runId}/local-commit-inspection"]!["get"]!;
        Assert.Single(paths, item => item.Key.Contains("local-commit-inspection", StringComparison.Ordinal));
        Assert.Equal(
            [("runId", "path")],
            operation["parameters"]!.AsArray().Select(parameter => (parameter!["name"]!.GetValue<string>(), parameter["in"]!.GetValue<string>())));
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetLocalCommitInspectionResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(AnswerMembers.Order(StringComparer.Ordinal), PropertiesOf(document, "GetLocalCommitInspectionResponse"));
        Assert.Equal(OperationMembers.Order(StringComparer.Ordinal), PropertiesOf(document, "LocalCommitInspectionOperationResponse"));
        Assert.Equal(["code", "kind"], PropertiesOf(document, "LocalCommitInspectionReasonResponse"));
        Assert.Contains("int32", document["components"]!["schemas"]!["LocalCommitInspectionOperationResponse"]!["properties"]!["totalBytes"]!.ToJsonString());
    }

    private static IEnumerable<string> PropertiesOf(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!
            .AsObject().Select(item => item.Key).Order(StringComparer.Ordinal);
    }

    private static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }
}
