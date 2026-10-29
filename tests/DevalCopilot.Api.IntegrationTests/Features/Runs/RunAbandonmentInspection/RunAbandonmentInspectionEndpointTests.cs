using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RunAbandonmentInspection;

/// <summary>
/// ADR-0071's one protected, bodyless and queryless recorded read through the real host and the real production wiring: authentication,
/// verbs and the MVC authorization inventory, the strict body and query refusals before dispatch with the fixed non-echoing errors, the
/// fixed 404 and validation, the closed wire of all three answer states (canonical UTC text, exact Int64 sequence strings that no number
/// can round), Unavailable over damaged storage, the absence of any write and the actual OpenAPI contract. Runs are closed by the real
/// abandon POST.
/// </summary>
public sealed class RunAbandonmentInspectionEndpointTests : RunAbandonmentInspectionApiTestBase
{
    private static readonly string[] AnswerMembers = ["runId", "status", "abandonment"];

    private static readonly string[] RecordedMembers = ["projectId", "reason", "abandonedAtUtc", "eventId", "eventSequence"];

    // ---- authentication, methods and the MVC authorization inventory -------------------------------------------------------------------

    [Fact]
    public async Task The_read_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var seeded = await SeedAbandonedAsync();
        var route = Route(seeded.RunId);
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
    public void The_route_exists_once_as_an_authorized_get_and_no_run_route_is_anonymous()
    {
        var all = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var endpoint = Assert.Single(
            all, candidate => candidate.RoutePattern.RawText?.Contains("abandonment-inspection", StringComparison.Ordinal) == true);
        Assert.Equal("api/runs/{runId:guid}/abandonment-inspection", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());

        // The live status and command routes keep their own authorization: nothing here added an anonymous endpoint.
        Assert.DoesNotContain(
            all.Where(candidate => candidate.RoutePattern.RawText?.StartsWith("api/runs", StringComparison.Ordinal) == true),
            candidate => candidate.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0);
        Assert.Contains(
            all, candidate => candidate.RoutePattern.RawText == "api/runs/{runId:guid}/abandonment"
                && candidate.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0);
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
        var seeded = await SeedAbandonedAsync();
        var before = await FingerprintAsync();

        // The nil run would be a validation failure if the request were dispatched, so the body refusal proves refusal before dispatch.
        foreach (var route in new[] { Route(Guid.Empty), Route(Guid.NewGuid()), Route(seeded.RunId) })
        {
            foreach (var (name, shape) in BodyShapes())
            {
                using var client = AuthenticatedClient();
                using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, route)));
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{name} {route}");
                var error = Assert.Single(JsonNode.Parse(text)!["errors"]!.AsArray())!;
                Assert.Equal(RunAbandonmentInspectionErrors.BodyNotAllowedCode, error["code"]!.GetValue<string>());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task A_readable_body_byte_without_a_declared_length_or_transfer_encoding_is_refused_before_dispatch()
    {
        // The nil run would be a validation failure if the request were dispatched; no content length and no transfer encoding are declared,
        // so only the one-byte peek can see the body.
        var context = await Host.Server.SendAsync(http =>
        {
            http.Request.Method = "GET";
            http.Request.Path = $"/api/runs/{Guid.Empty}/abandonment-inspection";
            http.Request.Headers.Authorization = $"Bearer {ApiWebApplicationFactory.ValidSecret}";
            http.Request.Body = new MemoryStream("x"u8.ToArray());
            http.Request.ContentLength = null;
            http.Request.Headers.Remove("Transfer-Encoding");
        });

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        using var reader = new StreamReader(context.Response.Body);
        var error = Assert.Single(JsonNode.Parse(await reader.ReadToEndAsync())!["errors"]!.AsArray())!;
        Assert.Equal(RunAbandonmentInspectionErrors.BodyNotAllowedCode, error["code"]!.GetValue<string>());
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
        var seeded = await SeedAbandonedAsync();

        foreach (var route in new[] { Route(Guid.Empty, "?" + query), Route(Guid.NewGuid(), "?" + query), Route(seeded.RunId, "?" + query) })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(RunAbandonmentInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.Equal("This request does not accept these query parameters.", error.GetProperty("detail").GetString());
            Assert.DoesNotContain(query, response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_lone_question_mark_is_an_empty_query_and_reads_normally()
    {
        var seeded = await SeedAbandonedAsync();

        var response = await GetAsync(Route(seeded.RunId, "?"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
    }

    // ---- fixed refusals --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_and_a_run_without_its_registered_project_are_one_fixed_404_that_repeats_no_value()
    {
        var seeded = await SeedAbandonedAsync();
        var orphan = await SeedAbandonedAsync();
        await ExecuteAsync($"DELETE FROM projects WHERE Id = {orphan.ProjectId}");
        var unknown = Guid.NewGuid();

        var responses = new List<(HttpStatusCode Status, JsonElement Body, string Text)>
        {
            await GetAsync(Route(unknown)),
            await GetAsync(Route(orphan.RunId)),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(RunAbandonmentInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            Assert.Equal("The requested run abandonment inspection was not found.", error.GetProperty("detail").GetString());

            // The shared Problem Details contract names the request path in its own instance member; nothing else repeats a value.
            var content = Normalize(response.Text);
            Assert.DoesNotContain(unknown.ToString(), content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(orphan.RunId.ToString(), content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(orphan.ProjectId.ToString(), content, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(Normalize(responses[0].Text), Normalize(responses[1].Text));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Route(seeded.RunId))).Status);
    }

    [Fact]
    public async Task The_nil_run_identity_is_a_fixed_validation_failure_and_a_non_guid_route_is_not_routed()
    {
        var response = await GetAsync(Route(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(RunAbandonmentInspectionErrors.InvalidRunCode, error.GetProperty("code").GetString());
        Assert.Equal(RunAbandonmentInspectionErrors.InvalidRunMessage, error.GetProperty("detail").GetString());

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/not-a-guid/abandonment-inspection")).StatusCode);
    }

    // ---- the closed wire ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_production_written_closure_is_the_closed_graph_with_canonical_strings(bool running)
    {
        var seeded = await SeedAbandonedAsync(running);
        var before = await FingerprintAsync();
        var stored = await WithDbAsync(async db => (
            Run: await db.Runs.FindAsync(seeded.RunId),
            Event: await Task.FromResult(db.Events.Single(runEvent => runEvent.RunId == seeded.RunId && runEvent.EventType == "run.abandoned"))));

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(AnswerMembers, Names(response.Body));
        Assert.Equal(seeded.RunId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        var recorded = response.Body.GetProperty("abandonment");
        Assert.Equal(RecordedMembers, Names(recorded));
        Assert.Equal(seeded.ProjectId, recorded.GetProperty("projectId").GetGuid());
        Assert.Equal(Reason, recorded.GetProperty("reason").GetString());
        Assert.Equal(Time(stored.Run!.AbandonedAtUtc!.Value), recorded.GetProperty("abandonedAtUtc").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", recorded.GetProperty("abandonedAtUtc").GetString());
        Assert.Equal(stored.Event.Id, recorded.GetProperty("eventId").GetGuid());
        Assert.Equal(JsonValueKind.String, recorded.GetProperty("eventSequence").ValueKind);
        Assert.Equal(stored.Event.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), recorded.GetProperty("eventSequence").GetString());
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("9223372036854775807")]
    public async Task A_sequence_beyond_the_safe_javascript_integer_is_an_exact_decimal_string(string sequence)
    {
        var seeded = await SeedAbandonedAsync();
        await ExecuteAsync(
            $"UPDATE events SET Sequence = {long.Parse(sequence, System.Globalization.CultureInfo.InvariantCulture)} WHERE RunId = {seeded.RunId} AND EventType = 'run.abandoned'");

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var member = response.Body.GetProperty("abandonment").GetProperty("eventSequence");
        Assert.Equal(JsonValueKind.String, member.ValueKind);
        Assert.Equal(sequence, member.GetString());
        Assert.Contains($"\"eventSequence\":\"{sequence}\"", response.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-08 15:00:00+03:00", "2026-10-08T12:00:00.0000000Z")]
    [InlineData("2026-10-08 06:30:00.1234567-05:30", "2026-10-08T12:00:00.1234567Z")]
    [InlineData("0001-01-01 00:00:00+00:00", "0001-01-01T00:00:00.0000000Z")]
    public async Task A_supported_offset_or_the_default_instant_is_normalized_to_seven_digit_utc_text(string stored, string wire)
    {
        var seeded = await SeedAbandonedAsync();
        await ExecuteAsync(
            $"UPDATE runs SET AbandonedAtUtc = {stored}, LastAdvancedAtUtc = {stored} WHERE Id = {seeded.RunId}");
        await ExecuteAsync(
            $"UPDATE events SET OccurredAtUtc = {stored} WHERE RunId = {seeded.RunId} AND EventType = 'run.abandoned'");

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(wire, response.Body.GetProperty("abandonment").GetProperty("abandonedAtUtc").GetString());
    }

    [Fact]
    public async Task A_multiline_unicode_reason_is_returned_exactly()
    {
        var reason = string.Concat("First line\nSecond ", (char)0xE9, " line ", char.ConvertFromUtf32(0x1F600), " end");
        var seeded = await SeedAbandonedAsync(reason: reason);

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal(reason, response.Body.GetProperty("abandonment").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_newer_run_without_an_abandonment_is_NotRecorded_with_a_null_abandonment_beside_the_recorded_earlier_one()
    {
        var earlier = await SeedAbandonedAsync();
        var newer = await SeedRunAsync(running: false, existingProject: earlier.ProjectId);

        var recorded = await GetAsync(Route(earlier.RunId));
        var absent = await GetAsync(Route(newer.RunId));

        Assert.Equal("Recorded", recorded.Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, absent.Status);
        Assert.Equal(AnswerMembers, Names(absent.Body));
        Assert.Equal(newer.RunId, absent.Body.GetProperty("runId").GetGuid());
        Assert.Equal("NotRecorded", absent.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, absent.Body.GetProperty("abandonment").ValueKind);
    }

    [Fact]
    public async Task A_run_of_another_lifecycle_is_NotRecorded_and_makes_no_claim_about_eligibility()
    {
        var seeded = await SeedRunAsync(running: true);
        await ExecuteAsync($"UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {seeded.RunId}");

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal("NotRecorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(AnswerMembers, Names(response.Body));
        Assert.DoesNotContain("ligible", response.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("blob_time")]
    [InlineData("second_event")]
    [InlineData("missing_event")]
    public async Task Damaged_or_incomplete_evidence_is_Unavailable_with_a_null_abandonment_never_absent_and_never_a_fault(string damage)
    {
        var seeded = await SeedAbandonedAsync();
        switch (damage)
        {
            case "payload":
                await ExecuteAsync($"UPDATE events SET PayloadJson = '{{' WHERE RunId = {seeded.RunId} AND EventType = 'run.abandoned'");
                break;
            case "blob_time":
                await ExecuteAsync($"UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = {seeded.RunId}");
                break;
            case "second_event":
                await ExecuteAsync(
                    $"""
                    INSERT INTO events (Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc)
                    SELECT {Guid.NewGuid()}, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc
                    FROM events WHERE RunId = {seeded.RunId} AND EventType = 'run.abandoned'
                    """);
                break;
            default:
                await ExecuteAsync($"DELETE FROM events WHERE RunId = {seeded.RunId} AND EventType = 'run.abandoned'");
                break;
        }

        var response = await GetAsync(Route(seeded.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(AnswerMembers, Names(response.Body));
        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("abandonment").ValueKind);
        Assert.DoesNotContain(Reason, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_real_database_fault_is_the_normal_failure_boundary_never_Unavailable_or_NotRecorded()
    {
        var seeded = await SeedAbandonedAsync();
        await ExecuteAsync($"ALTER TABLE events RENAME TO events_hidden");

        var response = await GetAsync(Route(seeded.RunId));

        // The test host's own failure boundary answers; the read neither relabels the fault nor answers any of its three statuses.
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal(500, response.Body.GetProperty("status").GetInt32());
        Assert.False(response.Body.TryGetProperty("abandonment", out _));
        Assert.False(response.Body.TryGetProperty("runId", out _));
        Assert.DoesNotContain(Reason, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_reads_change_no_durable_fact_and_the_read_is_the_only_call()
    {
        var seeded = await SeedAbandonedAsync();
        var before = await FingerprintAsync();

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal("Recorded", (await GetAsync(Route(seeded.RunId))).Body.GetProperty("status").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(Route(Guid.NewGuid()))).Status);
        Assert.Equal(before, await FingerprintAsync());
    }

    // ---- OpenAPI ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_one_bodyless_queryless_route_and_its_closed_responses()
    {
        var generator = Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var paths = document["paths"]!.AsObject();

        var operation = paths["/api/runs/{runId}/abandonment-inspection"]!["get"]!;
        Assert.Single(paths, item => item.Key.Contains("abandonment-inspection", StringComparison.Ordinal));
        Assert.Equal(
            [("runId", "path")],
            operation["parameters"]!.AsArray().Select(parameter => (parameter!["name"]!.GetValue<string>(), parameter["in"]!.GetValue<string>())));
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetRunAbandonmentInspectionResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(AnswerMembers.Order(StringComparer.Ordinal), PropertiesOf(document, "GetRunAbandonmentInspectionResponse"));
        Assert.Equal(RecordedMembers.Order(StringComparer.Ordinal), PropertiesOf(document, "RunAbandonmentInspectionRecordedResponse"));
        var sequence = document["components"]!["schemas"]!["RunAbandonmentInspectionRecordedResponse"]!["properties"]!["eventSequence"]!;
        Assert.Equal("string", sequence["type"]!.GetValue<string>());
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
