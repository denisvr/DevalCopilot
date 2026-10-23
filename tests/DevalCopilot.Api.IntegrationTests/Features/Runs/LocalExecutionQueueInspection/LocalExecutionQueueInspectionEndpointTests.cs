using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// ADR-0064's one protected, bodyless and queryless outstanding-admission overview through the real host: the route and its
/// authorization, the complete authentication, method, body, query and fixed-error matrices, the closed wire of every answer (the
/// Empty and Recorded statuses, canonical Int64 strings, UTC round-trip times, no operation, token, path or digest member), cycles
/// created by the production queue and stop operations, the absence of any write for a read, and the actual OpenAPI contract. Every test
/// boots its own host and database because the reading is global to a database. Detailed proof permutations belong to the Application
/// tests.
/// </summary>
public sealed class LocalExecutionQueueInspectionEndpointTests : LocalExecutionAdmissionHistoryApiTestBase, IAsyncLifetime
{
    private const string Route = "/api/local-execution-queue/inspection";

    private static readonly string[] ResponseMembers = ["entries", "status"];
    private static readonly string[] EntryMembers = ["admission", "projectId", "projectName", "runId", "sequence"];

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
        "executablePath", "arguments", "workspacePath", "canonicalPath", "repositoryPath", "fingerprint", "checkpoint", "sha256",
    ];

    private readonly ApiWebApplicationFactory own;

    public LocalExecutionQueueInspectionEndpointTests() : this(new ApiWebApplicationFactory())
    {
    }

    private LocalExecutionQueueInspectionEndpointTests(ApiWebApplicationFactory own) : base(own)
    {
        this.own = own;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await own.DisposeAsync();
    }

    // ---- route, authentication and methods ----------------------------------------------------------------------------------------

    [Fact]
    public void Exactly_one_overview_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("local-execution-queue", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/local-execution-queue/inspection", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get_and_a_failed_request_writes_nothing()
    {
        await QueuedAsync();
        var before = await RecordedFactsAsync();

        using var anonymous = Client(authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        using var wrong = Client(authenticated: false);
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route)).StatusCode);
        using var queryOnly = Client(authenticated: false);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{Route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);

        using var client = Client();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(Route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(Route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(Route, null)).StatusCode);
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
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_and_nothing_is_written()
    {
        await QueuedAsync();
        var before = await RecordedFactsAsync();

        foreach (var (name, shape) in BodyShapes())
        {
            using var client = Client();
            using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, Route)));
            var text = await response.Content.ReadAsStringAsync();

            Assert.True(HttpStatusCode.BadRequest == response.StatusCode, name);
            using var document = JsonDocument.Parse(text);
            var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalExecutionInspectionErrors.BodyNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
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
    [InlineData("projectId=1")]
    [InlineData("includeEnded=true")]
    public async Task The_route_accepts_no_query_key_and_refuses_before_dispatch_without_echoing_it(string query)
    {
        var response = await GetAsync($"{Route}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(LocalExecutionInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
        Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("limit", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("includeEnded", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_bare_question_mark_is_no_query()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Route + "?")).Status);
    }

    // ---- the closed states -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_database_is_the_closed_empty_answer_with_exactly_status_and_an_empty_entry_list()
    {
        var response = await GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(ResponseMembers, Names(response.Body));
        Assert.Equal("Empty", response.Body.GetProperty("status").GetString());
        Assert.Empty(response.Body.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_queued_cycle_is_recorded_with_its_project_run_header_and_admission_on_a_closed_canonical_operation_free_wire()
    {
        var (scene, sequenceId) = await QueuedAsync();
        var before = await RecordedFactsAsync();

        var response = await GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(ResponseMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        var entry = Assert.Single(response.Body.GetProperty("entries").EnumerateArray());
        Assert.Equal(EntryMembers, Names(entry));
        Assert.Equal(scene.RunId, entry.GetProperty("runId").GetGuid());
        Assert.NotEqual(Guid.Empty, entry.GetProperty("projectId").GetGuid());
        Assert.Equal("Local execution sequence", entry.GetProperty("projectName").GetString());
        var header = entry.GetProperty("sequence");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(sequenceId, header.GetProperty("sequenceId").GetGuid());
        Assert.Equal("WaitForAdmission", header.GetProperty("admissionMode").GetString());
        Assert.Equal("WaitingForAdmission", header.GetProperty("recordedState").GetString());
        Assert.Equal(JsonValueKind.Null, header.GetProperty("reasonCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("endedAtUtc").ValueKind);
        Assert.Matches(@"^[1-9][0-9]*$", header.GetProperty("sequenceRevision").GetString());
        Assert.EndsWith("Z", header.GetProperty("armedAtUtc").GetString(), StringComparison.Ordinal);
        var admission = entry.GetProperty("admission");
        Assert.Equal(AdmissionMembers, Names(admission));
        Assert.Equal(0, admission.GetProperty("ordinal").GetInt32());
        Assert.Equal("Implementation", admission.GetProperty("kind").GetString());
        Assert.Equal("Waiting", admission.GetProperty("recordedState").GetString());
        Assert.Equal("AwaitingAdmissionCheck", admission.GetProperty("waitingReason").GetString());
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("stepId").ValueKind);
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("recipeName").ValueKind);
        Assert.Equal(JsonValueKind.Null, admission.GetProperty("reasonCode").ValueKind);
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
    public async Task Cycles_of_two_projects_are_listed_in_order_and_each_entry_equals_the_tail_of_the_per_run_ledger_read()
    {
        var (first, _) = await QueuedAsync();
        var (second, _) = await QueuedAsync();

        var response = await GetAsync(Route);

        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        var entries = response.Body.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal([first.RunId, second.RunId], entries.Select(entry => entry.GetProperty("runId").GetGuid()));
        Assert.Equal(2, entries.Select(entry => entry.GetProperty("projectId").GetGuid()).Distinct().Count());
        Assert.True(
            long.Parse(entries[0].GetProperty("admission").GetProperty("admissionNumber").GetString()!)
            < long.Parse(entries[1].GetProperty("admission").GetProperty("admissionNumber").GetString()!));
        foreach (var (entry, scene) in entries.Zip([first, second]))
        {
            var ledger = await GetAsync(Admissions(scene.RunId));
            Assert.Equal("Recorded", ledger.Body.GetProperty("status").GetString());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ledger.Body.GetProperty("sequence").GetRawText()), JsonNode.Parse(entry.GetProperty("sequence").GetRawText())));
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(ledger.Body.GetProperty("admissions").EnumerateArray().Last().GetRawText()),
                JsonNode.Parse(entry.GetProperty("admission").GetRawText())));
        }
    }

    [Fact]
    public async Task A_stopped_wait_is_a_retired_admission_that_is_no_longer_outstanding_and_the_answer_is_empty_not_idle()
    {
        var (scene, sequenceId) = await QueuedAsync();
        using var client = Client();
        var stopped = await client.PostAsync($"/api/runs/{scene.RunId}/local-execution-sequence/{sequenceId}/stop", null);
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        var response = await GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(ResponseMembers, Names(response.Body));
        Assert.Equal("Empty", response.Body.GetProperty("status").GetString());
        Assert.Empty(response.Body.GetProperty("entries").EnumerateArray());
    }

    // ---- the actual OpenAPI contract ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_one_bodyless_queryless_route_and_the_closed_responses()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var path = document["paths"]!["/api/local-execution-queue/inspection"]!.AsObject();

        Assert.Equal(["get"], path.Select(item => item.Key));
        var operation = path["get"]!;
        Assert.Null(operation["requestBody"]);
        Assert.Empty(operation["parameters"]?.AsArray() ?? []);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetLocalExecutionQueueInspectionResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(ResponseMembers, Properties(document, "GetLocalExecutionQueueInspectionResponse"));
        Assert.Equal(EntryMembers, Properties(document, "LocalExecutionQueueInspectionEntryResponse"));
        Assert.Equal(HeaderMembers, Properties(document, "LocalExecutionAdmissionHistoryHeaderResponse"));
        Assert.Equal(AdmissionMembers, Properties(document, "LocalExecutionAdmissionHistoryAdmissionResponse"));
        var schemas = document["components"]!["schemas"]!;
        Assert.Contains("string", schemas["LocalExecutionAdmissionHistoryAdmissionResponse"]!["properties"]!["admissionNumber"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("string", schemas["LocalExecutionAdmissionHistoryHeaderResponse"]!["properties"]!["sequenceRevision"]!.ToJsonString(), StringComparison.Ordinal);
    }

    private static string[] Properties(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        return properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }
}
