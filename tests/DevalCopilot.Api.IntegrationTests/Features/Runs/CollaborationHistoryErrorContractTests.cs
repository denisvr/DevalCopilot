using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0035: every refusal of the collaboration-history route is the shared Problem Details contract (<c>ApiProblemDetails</c> with its
/// <c>errors</c> array, code, detail, JSON pointer and trace identifier) and never repeats a rejected value, whether it is out of
/// range, malformed, repeated or overflowing. The real OpenAPI document declares exactly that contract for 400, 404 and 409.
/// </summary>
public sealed class CollaborationHistoryErrorContractTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private const string ProblemMediaType = "application/problem+json";

    private async Task<(HttpResponseMessage Response, string Text)> GetAsync(Guid runId, string? query)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(Route(runId, query));
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static string WithoutTrace(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    private static void AssertValidationProblem(HttpResponseMessage response, string text, params (string Code, string Pointer)[] expected)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("urn:devalente:problem:validation", body.GetProperty("type").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        var errors = body.GetProperty("errors").EnumerateArray().ToList();
        Assert.Equal(
            expected.OrderBy(item => item.Code, StringComparer.Ordinal),
            errors.Select(error => (error.GetProperty("code").GetString()!, error.GetProperty("pointer").GetString()!))
                .OrderBy(item => item.Item1, StringComparer.Ordinal));
        Assert.All(errors, error => Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("detail").GetString())));
        Assert.DoesNotContain("ValidationProblemDetails", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not valid", text, StringComparison.Ordinal);
        Assert.DoesNotContain("entries", text, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string, string> RejectedScalars() => new()
    {
        { "limit=reviewer-invalid-scalar", "reviewer-invalid-scalar", "validation.invalid_limit", "#/limit" },
        { "limit=99999999999999999999", "99999999999999999999", "validation.invalid_limit", "#/limit" },
        { "limit=2147483648", "2147483648", "validation.invalid_limit", "#/limit" },
        { "limit=-2147483649", "-2147483649", "validation.invalid_limit", "#/limit" },
        { "limit=21", "", "validation.invalid_limit", "#/limit" },
        { "limit=86420", "86420", "validation.invalid_limit", "#/limit" },
        { "limit=0", "", "validation.invalid_limit", "#/limit" },
        { "limit=-1", "", "validation.invalid_limit", "#/limit" },
        { "limit=1.5", "1.5", "validation.invalid_limit", "#/limit" },
        { "limit=1e1", "1e1", "validation.invalid_limit", "#/limit" },
        { "limit=0x10", "0x10", "validation.invalid_limit", "#/limit" },
        { "limit=%2B5", "+5", "validation.invalid_limit", "#/limit" },
        { "limit=+5", "+5", "validation.invalid_limit", "#/limit" },
        { "limit=05", "", "validation.invalid_limit", "#/limit" },
        { "limit=%205", "", "validation.invalid_limit", "#/limit" },
        { "limit=5%20", "", "validation.invalid_limit", "#/limit" },
        { "limit=%EF%BC%95", "５", "validation.invalid_limit", "#/limit" },
        { "limit=%D9%A5", "٥", "validation.invalid_limit", "#/limit" },
        { "limit=%E2%82%AC", "€", "validation.invalid_limit", "#/limit" },
        { "limit=1&limit=2", "", "validation.invalid_limit", "#/limit" },
        { "beforeSequence=reviewer-invalid-scalar", "reviewer-invalid-scalar", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=0", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=-1", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=%2B1", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=007", "007", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=%201", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1%20", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1.0", "1.0", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1e3", "1e3", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1.5e3", "1.5e3", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=9007199254740993.0", "9007199254740993.0", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=9223372036854775808", "9223372036854775808", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=99999999999999999999999", "99999999999999999999999", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=0x10", "0x10", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=NaN", "NaN", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=%EF%BC%91%EF%BC%92", "１２", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=%D9%A1", "١", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1%0A", "", "validation.invalid_cursor", "#/beforeSequence" },
        { "beforeSequence=1&beforeSequence=2", "", "validation.invalid_cursor", "#/beforeSequence" },
    };

    [Theory]
    [MemberData(nameof(RejectedScalars))]
    public async Task A_rejected_scalar_is_the_shared_validation_problem_without_the_rejected_value(
        string query, string rejected, string code, string pointer)
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 3);

        var (response, text) = await GetAsync(runId, query);

        AssertValidationProblem(response, text, (code, pointer));
        if (rejected.Length > 0)
        {
            Assert.DoesNotContain(rejected, WithoutTrace(text), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Both_scalars_rejected_together_report_both_errors_and_neither_value()
    {
        var runId = await SeedRunAsync();

        var (response, text) = await GetAsync(runId, "limit=reviewer-invalid-limit&beforeSequence=99999999999999999999999");

        AssertValidationProblem(response, text, ("validation.invalid_limit", "#/limit"), ("validation.invalid_cursor", "#/beforeSequence"));
        var body = WithoutTrace(text);
        Assert.DoesNotContain("reviewer-invalid-limit", body, StringComparison.Ordinal);
        Assert.DoesNotContain("99999999999999999999999", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_scalar_on_an_unknown_run_is_still_a_safe_validation_problem()
    {
        var (response, text) = await GetAsync(Guid.NewGuid(), "beforeSequence=reviewer-invalid-scalar");

        AssertValidationProblem(response, text, ("validation.invalid_cursor", "#/beforeSequence"));
        Assert.DoesNotContain("reviewer-invalid-scalar", WithoutTrace(text), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_run_is_the_shared_not_found_problem()
    {
        var (response, text) = await GetAsync(Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("urn:devalente:problem:not-found", body.GetProperty("type").GetString());
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("runs.not_found", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task An_unavailable_history_is_the_shared_conflict_problem_with_one_fixed_code()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 2);
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {events[0].Id}");

        var (response, text) = await GetAsync(runId, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("urn:devalente:problem:conflict", body.GetProperty("type").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        var error = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal("collaboration_history.unavailable", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("limit=1", 1)]
    [InlineData("limit=20", 12)]
    [InlineData("limit=5&beforeSequence=9223372036854775807", 5)]
    public async Task Valid_scalars_are_accepted_unchanged(string query, int expectedEntries)
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 12);

        var (response, text) = await GetAsync(runId, query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedEntries, JsonDocument.Parse(text).RootElement.GetProperty("entries").GetArrayLength());
    }

    private static JsonObject PropertiesOf(JsonNode schema)
    {
        var merged = new JsonObject();
        foreach (var node in new[] { schema }.Concat(schema["allOf"]?.AsArray() ?? []))
        {
            foreach (var property in node?["properties"]?.AsObject() ?? [])
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        return merged;
    }

    private async Task<JsonNode> OpenApiAsync()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = await generator.GenerateAsync("v1");
        return JsonNode.Parse(document.ToJson())!;
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_string_cursor_the_closed_page_and_the_shared_400_404_and_409_schemas()
    {
        var document = await OpenApiAsync();

        var operation = document["paths"]!["/api/runs/{runId}/collaboration-history"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!);
        Assert.Equal(["beforeSequence", "limit", "runId"], parameters.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("query", parameters["beforeSequence"]["in"]!.GetValue<string>());
        Assert.Contains("string", parameters["beforeSequence"]["schema"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("integer", parameters["beforeSequence"]["schema"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("query", parameters["limit"]["in"]!.GetValue<string>());
        Assert.Contains("int32", parameters["limit"]["schema"]!.ToJsonString(), StringComparison.Ordinal);

        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404", "409"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetCollaborationHistoryResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var status in new[] { "400", "404", "409" })
        {
            var content = responses[status]!["content"]!.AsObject().Single();
            Assert.Contains("json", content.Key, StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", content.Value!.ToJsonString(), StringComparison.Ordinal);
        }

        var schemas = document["components"]!["schemas"]!.AsObject();
        var page = PropertiesOf(schemas["GetCollaborationHistoryResponse"]!);
        Assert.Equal(["entries", "hasMore", "nextBeforeSequence", "runId"], page.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("string", page["nextBeforeSequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("integer", page["nextBeforeSequence"]!.ToJsonString(), StringComparison.Ordinal);
        var entry = PropertiesOf(schemas["CollaborationHistoryEntryResponse"]!);
        Assert.Equal(
            ["actor", "contentStatus", "fields", "id", "occurredAtUtc", "protocolVersion", "provenance", "recipient", "runId", "sequence",
                "summary", "type"],
            entry.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("string", entry["sequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("integer", entry["sequence"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var name in new[] { "CollaborationHistoryParticipantResponse" })
        {
            Assert.Equal(["kind", "provider", "role"], PropertiesOf(schemas[name]!).Select(item => item.Key).Order(StringComparer.Ordinal));
        }

        Assert.Equal(["name", "value"], PropertiesOf(schemas["CollaborationHistoryFieldResponse"]!).Select(item => item.Key).Order(StringComparer.Ordinal));

        var problem = PropertiesOf(schemas["ApiProblemDetails"]!);
        Assert.Contains("errors", problem.Select(item => item.Key));
        Assert.Contains("traceId", problem.Select(item => item.Key));
        Assert.Contains("ApiError", problem["errors"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(
            ["code", "detail", "parameters", "pointer"], PropertiesOf(schemas["ApiError"]!).Select(item => item.Key).Order(StringComparer.Ordinal));
    }


    [Fact]
    public async Task The_existing_timeline_and_event_history_operations_are_unchanged()
    {
        var document = await OpenApiAsync();

        var timeline = document["paths"]!["/api/runs/{runId}/collaboration-timeline"]!["get"]!;
        Assert.Equal(["runId"], timeline["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()));
        Assert.Contains(
            "structuredContentJson",
            PropertiesOf(document["components"]!["schemas"]!["CollaborationMessageTimelineResponse"]!).Select(item => item.Key));
        var events = document["paths"]!["/api/runs/{runId}/event-history"]!["get"]!;
        Assert.Equal(["beforeSequence", "limit", "runId"], events["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
    }
}
