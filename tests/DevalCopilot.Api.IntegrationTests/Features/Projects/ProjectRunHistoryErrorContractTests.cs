using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0033: every refusal of the run-history route is the shared Problem Details contract (<c>ApiProblemDetails</c> with its
/// <c>errors</c> array, code, detail, JSON pointer and trace identifier) and never repeats a rejected value, whether the value is
/// numeric and out of range, not a number at all, or overflows 32 bits. The real OpenAPI document declares exactly that contract for
/// 400 and 404 beside the numeric public query parameters.
/// </summary>
public sealed class ProjectRunHistoryErrorContractTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string ProblemMediaType = "application/problem+json";

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> ProjectWithRunsAsync(int runs)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var project = Project.Register(Guid.NewGuid(), "Error contract", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.Projects.Add(project);
        for (var index = 0; index < runs; index++)
        {
            var number = project.ReserveExecutionNumber();
            db.Runs.Add(Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, number, RunExecutionMode.ManualAgent, $"Objective {number}", DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();
        return project.Id;
    }

    private async Task<(HttpResponseMessage Response, string Text)> GetAsync(Guid projectId, string? query)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync($"/api/projects/{projectId}/run-history{(query is null ? string.Empty : "?" + query)}");
        return (response, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The body without its trace identifier, so an assertion about rejected input cannot collide with an identifier.</summary>
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
            errors.Select(error => (error.GetProperty("code").GetString()!, error.GetProperty("pointer").GetString()!)).OrderBy(item => item.Item1, StringComparer.Ordinal));
        Assert.All(errors, error => Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("detail").GetString())));
        Assert.DoesNotContain("ValidationProblemDetails", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not valid", text, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string, string> RejectedScalars() => new()
    {
        { "limit=reviewer-invalid-scalar", "reviewer-invalid-scalar", "validation.invalid_limit", "#/limit" },
        { "limit=99999999999999999999", "99999999999999999999", "validation.invalid_limit", "#/limit" },
        { "limit=2147483648", "2147483648", "validation.invalid_limit", "#/limit" },
        { "limit=-2147483649", "-2147483649", "validation.invalid_limit", "#/limit" },
        { "limit=1.5", "1.5", "validation.invalid_limit", "#/limit" },
        { "limit=1e3", "1e3", "validation.invalid_limit", "#/limit" },
        { "limit=0x10", "0x10", "validation.invalid_limit", "#/limit" },
        { "limit=%E2%82%AC", "\u20AC", "validation.invalid_limit", "#/limit" },
        { "limit=86420", "86420", "validation.invalid_limit", "#/limit" },
        { "limit=-86420", "-86420", "validation.invalid_limit", "#/limit" },
        { "limit=0", "", "validation.invalid_limit", "#/limit" },
        { "beforeExecutionNumber=reviewer-invalid-scalar", "reviewer-invalid-scalar", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=99999999999999999999", "99999999999999999999", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=2147483648", "2147483648", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=-2147483649", "-2147483649", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=2.5", "2.5", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=-86420", "-86420", "validation.invalid_cursor", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=0", "", "validation.invalid_cursor", "#/beforeExecutionNumber" },
    };

    [Theory]
    [MemberData(nameof(RejectedScalars))]
    public async Task A_rejected_scalar_is_the_shared_validation_problem_without_the_rejected_value(
        string query, string rejected, string code, string pointer)
    {
        var projectId = await ProjectWithRunsAsync(3);

        var (response, text) = await GetAsync(projectId, query);

        AssertValidationProblem(response, text, (code, pointer));
        if (rejected.Length > 0)
        {
            Assert.DoesNotContain(rejected, WithoutTrace(text), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Both_scalars_rejected_together_report_both_errors_and_neither_value()
    {
        var projectId = await ProjectWithRunsAsync(3);

        var (response, text) = await GetAsync(projectId, "limit=reviewer-invalid-limit&beforeExecutionNumber=99999999999999999999");

        AssertValidationProblem(response, text, ("validation.invalid_limit", "#/limit"), ("validation.invalid_cursor", "#/beforeExecutionNumber"));
        var body = WithoutTrace(text);
        Assert.DoesNotContain("reviewer-invalid-limit", body, StringComparison.Ordinal);
        Assert.DoesNotContain("99999999999999999999", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_scalar_on_an_unknown_project_is_still_a_safe_validation_problem()
    {
        var (response, text) = await GetAsync(Guid.NewGuid(), "limit=reviewer-invalid-scalar");

        AssertValidationProblem(response, text, ("validation.invalid_limit", "#/limit"));
        Assert.DoesNotContain("reviewer-invalid-scalar", WithoutTrace(text), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_project_is_the_shared_not_found_problem()
    {
        var (response, text) = await GetAsync(Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("urn:devalente:problem:not-found", body.GetProperty("type").GetString());
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("projects.not_found", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("limit=")]
    [InlineData("beforeExecutionNumber=")]
    [InlineData("limit=&beforeExecutionNumber=")]
    public async Task Omitted_or_empty_scalars_keep_their_defaults(string query)
    {
        var projectId = await ProjectWithRunsAsync(12);

        var (response, text) = await GetAsync(projectId, query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(10, body.GetProperty("entries").GetArrayLength());
        Assert.Equal(12, body.GetProperty("entries")[0].GetProperty("executionNumber").GetInt32());
        Assert.True(body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, body.GetProperty("nextBeforeExecutionNumber").GetInt32());
    }

    [Theory]
    [InlineData("limit=1", 1)]
    [InlineData("limit=20", 12)]
    [InlineData("limit=+5", 5)]
    [InlineData("limit=%2B5", 5)]
    [InlineData("beforeExecutionNumber=2147483647&limit=3", 3)]
    public async Task Valid_numeric_scalars_are_unchanged(string query, int expectedEntries)
    {
        var projectId = await ProjectWithRunsAsync(12);

        var (response, text) = await GetAsync(projectId, query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedEntries, JsonDocument.Parse(text).RootElement.GetProperty("entries").GetArrayLength());
    }

    // A schema that extends another keeps its own members under allOf beside the reference to the base.
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
        var generator = factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = await generator.GenerateAsync("v1");
        return JsonNode.Parse(document.ToJson())!;
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_numeric_query_scalars_and_the_shared_400_and_404_schemas()
    {
        var document = await OpenApiAsync();

        var operation = document["paths"]!["/api/projects/{projectId}/run-history"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().ToDictionary(parameter => parameter!["name"]!.GetValue<string>(), parameter => parameter!);
        Assert.Equal(["beforeExecutionNumber", "limit", "projectId"], parameters.Keys.Order(StringComparer.Ordinal));
        foreach (var name in new[] { "beforeExecutionNumber", "limit" })
        {
            Assert.Equal("query", parameters[name]["in"]!.GetValue<string>());
            Assert.Contains("integer", parameters[name]["schema"]!.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("int32", parameters[name]["schema"]!.ToJsonString(), StringComparison.Ordinal);
        }

        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetProjectRunHistoryResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var status in new[] { "400", "404" })
        {
            var content = responses[status]!["content"]!.AsObject().Single();
            Assert.Contains("json", content.Key, StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", content.Value!.ToJsonString(), StringComparison.Ordinal);
        }

        var schemas = document["components"]!["schemas"]!.AsObject();
        var problem = PropertiesOf(schemas["ApiProblemDetails"]!);
        Assert.Contains("errors", problem.Select(item => item.Key));
        Assert.Contains("traceId", problem.Select(item => item.Key));
        Assert.Contains("ApiError", problem["errors"]!.ToJsonString(), StringComparison.Ordinal);
        var error = PropertiesOf(schemas["ApiError"]!);
        Assert.Equal(["code", "detail", "parameters", "pointer"], error.Select(item => item.Key).Order(StringComparer.Ordinal));
    }
}
