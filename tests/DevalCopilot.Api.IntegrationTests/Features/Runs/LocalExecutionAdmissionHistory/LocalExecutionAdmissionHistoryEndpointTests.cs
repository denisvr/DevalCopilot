using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>
/// ADR-0063's one protected, bodyless recorded admission-ledger read through the real host: the route and its authorization, the
/// complete authentication, method, body, query and fixed-error matrices, the closed wire of every answer (canonical Int64 strings,
/// UTC round-trip times, no operation, token, path or digest member), the NotRecorded, NotQueued, Recorded and Unavailable states over
/// cycles created by the production queue, immediate-arming and stop operations, the absence of any write for a read, and the actual
/// OpenAPI contract. Detailed ledger proof permutations belong to the Application tests.
/// </summary>
public sealed class LocalExecutionAdmissionHistoryEndpointTests(ApiWebApplicationFactory baseFactory)
    : LocalExecutionAdmissionHistoryApiTestBase(baseFactory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] ResponseMembers = ["admissions", "runId", "sequence", "status"];

    private static readonly string[] HeaderMembers =
    [
        "admissionMode", "advancementDeadlineUtc", "armedAtUtc", "endedAtUtc", "reasonCode", "recordedState", "sequenceId",
        "sequenceRevision", "updatedAtUtc",
    ];

    private static readonly string[] AdmissionMembers =
    [
        "admissionId", "admissionNumber", "enqueuedAtUtc", "kind", "ordinal", "passNumber", "reasonCode", "recipeName", "recipeOrdinal",
        "recordedState", "revision", "stepId", "updatedAtUtc", "waitingReason", "waitingReasonRecordedAtUtc",
    ];

    private static readonly string[] Forbidden =
    [
        "reservedOperationId", "operationId", "ownerToken", "preparationOwnerToken", "settingsSnapshot", "confirmation", "planInputs",
        "executablePath", "arguments", "workspacePath", "fingerprint", "checkpoint", "sha256",
    ];

    // ---- route, authentication and methods ----------------------------------------------------------------------------------------

    [Fact]
    public void Exactly_one_admission_history_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.EndsWith("/inspection/admissions", StringComparison.Ordinal) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/local-execution-sequence/inspection/admissions", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var (scene, _) = await QueuedAsync();
        var route = Admissions(scene.RunId);
        var before = await RecordedFactsAsync();

        using var anonymous = Client(authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        using var wrong = Client(authenticated: false);
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);
        using var queryOnly = Client(authenticated: false);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);

        using var client = Client();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        Assert.Equal(before, await RecordedFactsAsync());
    }

    // ---- bodies and queries ---------------------------------------------------------------------------------------------------------

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
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_even_for_an_unknown_run_and_nothing_is_written()
    {
        var (scene, _) = await QueuedAsync();
        var before = await RecordedFactsAsync();

        foreach (var route in new[] { Admissions(Guid.NewGuid()), Admissions(scene.RunId) })
        {
            foreach (var (name, shape) in BodyShapes())
            {
                using var client = Client();
                using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, route)));
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{name} {route}");
                using var document = JsonDocument.Parse(text);
                var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
                Assert.Equal(LocalExecutionInspectionErrors.BodyNotAllowedCode, error.GetProperty("code").GetString());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, await RecordedFactsAsync());
    }

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("x")]
    [InlineData("runId=1")]
    [InlineData("a=1&b=2")]
    [InlineData("&")]
    [InlineData("limit=10")]
    [InlineData("fromOffset=0")]
    public async Task The_route_accepts_no_query_key_and_refuses_before_dispatch_even_for_an_unknown_run_without_echoing_it(string query)
    {
        var (scene, _) = await QueuedAsync();

        foreach (var route in new[] { Admissions(Guid.NewGuid()), Admissions(scene.RunId) })
        {
            var response = await GetAsync($"{route}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalExecutionInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("limit", response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_bare_question_mark_is_no_query_and_a_malformed_run_identity_never_reaches_the_operation()
    {
        var (scene, _) = await QueuedAsync();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Admissions(scene.RunId) + "?")).Status);
        using var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/not-a-guid/local-execution-sequence/inspection/admissions")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_run_is_the_one_fixed_inspection_404_that_does_not_echo_the_identity()
    {
        var unknown = Guid.NewGuid();

        var response = await GetAsync(Admissions(unknown));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
        var problem = JsonNode.Parse(response.Text)!.AsObject();
        problem.Remove("traceId");
        problem.Remove("instance");
        Assert.DoesNotContain(unknown.ToString(), problem.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- the four closed states --------------------------------------------------------------------------------------------------------

    private static void AssertClosedEmpty(JsonElement body, string status, Guid runId)
    {
        Assert.Equal(ResponseMembers, Names(body));
        Assert.Equal(runId, body.GetProperty("runId").GetGuid());
        Assert.Equal(status, body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sequence").ValueKind);
        Assert.Empty(body.GetProperty("admissions").EnumerateArray());
    }

    [Fact]
    public async Task A_known_run_without_a_sequence_is_not_recorded_with_a_null_header_and_an_empty_list()
    {
        var scene = await SeedAsync();

        var response = await GetAsync(Admissions(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        AssertClosedEmpty(response.Body, "NotRecorded", scene.RunId);
    }

    [Fact]
    public async Task An_immediate_sequence_is_not_queued_never_an_empty_ledger()
    {
        var scene = await SeedAsync();
        using var client = Client();
        var availability = await client.GetAsync($"/api/runs/{scene.RunId}/local-execution-sequence/availability/{scene.PlanId}");
        var confirmation = JsonDocument.Parse(await availability.Content.ReadAsStringAsync()).RootElement.GetProperty("confirmationSha256").GetString()!;
        var armed = await client.PostAsync($"/api/runs/{scene.RunId}/local-execution-sequence", Json(Body(scene, confirmation)));
        Assert.Equal(HttpStatusCode.OK, armed.StatusCode);

        var response = await GetAsync(Admissions(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        AssertClosedEmpty(response.Body, "NotQueued", scene.RunId);
    }

    [Fact]
    public async Task A_queued_cycle_is_recorded_with_one_waiting_admission_whose_wire_is_closed_canonical_and_free_of_operation_members()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var before = await RecordedFactsAsync();

        var response = await GetAsync(Admissions(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(ResponseMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        var header = response.Body.GetProperty("sequence");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(sequenceId, header.GetProperty("sequenceId").GetGuid());
        Assert.Equal("WaitForAdmission", header.GetProperty("admissionMode").GetString());
        Assert.Equal("WaitingForAdmission", header.GetProperty("recordedState").GetString());
        Assert.Equal(JsonValueKind.Null, header.GetProperty("reasonCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("endedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, header.GetProperty("sequenceRevision").ValueKind);
        Assert.Matches(@"^[1-9][0-9]*$", header.GetProperty("sequenceRevision").GetString());
        var armed = DateTimeOffset.Parse(header.GetProperty("armedAtUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(armed.AddHours(2), DateTimeOffset.Parse(header.GetProperty("advancementDeadlineUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.EndsWith("Z", header.GetProperty("armedAtUtc").GetString(), StringComparison.Ordinal);

        var admission = Assert.Single(response.Body.GetProperty("admissions").EnumerateArray());
        Assert.Equal(AdmissionMembers, Names(admission));
        Assert.Equal(0, admission.GetProperty("ordinal").GetInt32());
        Assert.Equal("Implementation", admission.GetProperty("kind").GetString());
        Assert.Equal(0, admission.GetProperty("passNumber").GetInt32());
        Assert.Equal("Waiting", admission.GetProperty("recordedState").GetString());
        Assert.Equal("AwaitingAdmissionCheck", admission.GetProperty("waitingReason").GetString());
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("stepId").ValueKind);
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("recipeOrdinal").ValueKind);
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("recipeName").ValueKind);
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("reasonCode").ValueKind);
        Assert.Equal(JsonValueKind.String, admission.GetProperty("admissionNumber").ValueKind);
        Assert.Matches(@"^[1-9][0-9]*$", admission.GetProperty("admissionNumber").GetString());
        Assert.Matches(@"^[1-9][0-9]*$", admission.GetProperty("revision").GetString());
        foreach (var time in new[] { "waitingReasonRecordedAtUtc", "enqueuedAtUtc", "updatedAtUtc" })
        {
            Assert.EndsWith("Z", admission.GetProperty(time).GetString(), StringComparison.Ordinal);
        }

        foreach (var word in Forbidden)
        {
            Assert.DoesNotContain(word, response.Text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await RecordedFactsAsync());
        Assert.Equal(0, Evidence.Captures);
    }

    [Fact]
    public async Task A_stop_while_waiting_is_a_retired_admission_with_no_step_and_the_stopped_header()
    {
        var (scene, sequenceId) = await QueuedAsync();
        using var client = Client();
        var stopped = await client.PostAsync($"/api/runs/{scene.RunId}/local-execution-sequence/{sequenceId}/stop", null);
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        var response = await GetAsync(Admissions(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Stopped", response.Body.GetProperty("sequence").GetProperty("recordedState").GetString());
        Assert.Equal("stopped_before_claim", response.Body.GetProperty("sequence").GetProperty("reasonCode").GetString());
        Assert.NotEqual(JsonValueKind.Null, response.Body.GetProperty("sequence").GetProperty("endedAtUtc").ValueKind);
        var admission = Assert.Single(response.Body.GetProperty("admissions").EnumerateArray());
        Assert.Equal("Retired", admission.GetProperty("recordedState").GetString());
        Assert.Equal("stopped_before_claim", admission.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("stepId").ValueKind);
    }

    // ---- the actual OpenAPI contract ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_one_bodyless_queryless_route_and_the_closed_responses()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var path = document["paths"]!["/api/runs/{runId}/local-execution-sequence/inspection/admissions"]!.AsObject();

        Assert.Equal(["get"], path.Select(item => item.Key));
        var operation = path["get"]!;
        Assert.Null(operation["requestBody"]);
        Assert.Equal(
            ["runId:path"],
            operation["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>() + ":" + p["in"]!.GetValue<string>()));
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetLocalExecutionAdmissionHistoryResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(ResponseMembers, Properties(document, "GetLocalExecutionAdmissionHistoryResponse"));
        Assert.Equal(HeaderMembers, Properties(document, "LocalExecutionAdmissionHistoryHeaderResponse"));
        Assert.Equal(AdmissionMembers, Properties(document, "LocalExecutionAdmissionHistoryAdmissionResponse"));
        var schemas = document["components"]!["schemas"]!;
        Assert.Contains("string", schemas["LocalExecutionAdmissionHistoryAdmissionResponse"]!["properties"]!["admissionNumber"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("string", schemas["LocalExecutionAdmissionHistoryAdmissionResponse"]!["properties"]!["revision"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("string", schemas["LocalExecutionAdmissionHistoryHeaderResponse"]!["properties"]!["sequenceRevision"]!.ToJsonString(), StringComparison.Ordinal);
    }

    private static string[] Properties(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        return properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }
}
