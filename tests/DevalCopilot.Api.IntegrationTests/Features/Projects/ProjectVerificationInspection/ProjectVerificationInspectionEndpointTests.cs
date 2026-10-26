using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects.ProjectVerificationInspection;

/// <summary>
/// ADR-0069's three protected, bodyless reads against the real host over file-backed SQLite: complete authentication, method, body,
/// query, value, identity and fixed-error matrices, the explicit MVC authorization inventory, the closed wire of every payload, integrated
/// page and metadata reads, Unavailable over damaged storage, no write or artifact read for any metadata request, a second host over the
/// same database, and the actual OpenAPI contract. Window reads are proved in the sibling class.
/// </summary>
public sealed class ProjectVerificationInspectionEndpointTests(ApiWebApplicationFactory factory)
    : ProjectVerificationInspectionApiTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] PageMembers = ["hasMore", "items", "nextBeforeExecutionNumber", "projectId", "status"];

    private static readonly string[] ItemMembers = ["claimedAtUtc", "commandName", "executionId", "executionNumber"];

    private static readonly string[] DetailMembers = ["execution", "executionId", "projectId", "status"];

    private static readonly string[] ExecutionMembers =
    [
        "checkpointId", "checkpointNumber", "claimFingerprintSha256", "claimedAtUtc", "commandId", "commandName",
        "completedAtUtc", "completionFingerprintSha256", "dispatchedAtUtc", "executionNumber", "exitCode", "outcome",
        "standardError", "standardOutput", "status", "workspaceId",
    ];

    private static readonly string[] StreamMembers = ["artifactId", "byteLength", "captureOutcome", "status", "truncated"];

    private static readonly string[] Forbidden =
    [
        "workspacePath", "executablePath", "arguments", "timeoutSeconds", "relativeStoragePath", "contentHash", "--secret-argument",
        @"verify.exe", @"C:\\workspaces", @"C:\\repos", "verification-commands",
    ];

    private static void AssertNoForbiddenText(string text)
    {
        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- authentication, methods and the explicit MVC authorization inventory ---------------------------------------------------

    private IEnumerable<string> AllRoutes(Guid projectId, Guid executionId) =>
        [History(projectId), Detail(projectId, executionId), Window(projectId, executionId, "stdout")];

    [Fact]
    public async Task Every_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var (projectId, executions) = await SeedAsync(false);

        foreach (var route in AllRoutes(projectId, executions[0].Id))
        {
            using var anonymous = Host.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

            using var wrong = Host.CreateClient();
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

            using var queryOnly = Host.CreateClient();
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await queryOnly.GetAsync($"{route}{(route.Contains('?') ? '&' : '?')}access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);

            using var client = AuthenticatedClient();
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        }

        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public void Exactly_three_verification_history_routes_exist_each_an_authorized_get_with_no_anonymous_access()
    {
        var all = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var endpoints = all
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("verification-history", StringComparison.Ordinal) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "api/projects/{projectId:guid}/verification-history",
                "api/projects/{projectId:guid}/verification-history/{executionId:guid}",
                "api/projects/{projectId:guid}/verification-history/{executionId:guid}/artifact-window/{stream}",
            ],
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText));
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        }

        // Every other project route keeps its own authorization: nothing here added an anonymous endpoint.
        Assert.DoesNotContain(
            all.Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/projects", StringComparison.Ordinal) == true),
            endpoint => endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0);
    }

    // ---- bodies --------------------------------------------------------------------------------------------------------------

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
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_even_for_an_unknown_project()
    {
        var (projectId, executions) = await SeedAsync(false);
        var routes = new[]
        {
            History(Guid.NewGuid()), Detail(Guid.NewGuid(), Guid.NewGuid()), Window(Guid.NewGuid(), Guid.NewGuid(), "stdout"),
            History(projectId), Detail(projectId, executions[0].Id), Window(projectId, executions[0].Id, "stdout"),
        };
        foreach (var route in routes)
        {
            foreach (var (name, shape) in BodyShapes())
            {
                using var client = AuthenticatedClient();
                using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, route)));
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{name} {route}");
                using var document = JsonDocument.Parse(text);
                var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
                Assert.Equal(ProjectVerificationInspectionErrors.BodyNotAllowedCode, error.GetProperty("code").GetString());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(0, StoreCalls);
    }

    // ---- queries --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("x")]
    [InlineData("a=1&b=2")]
    [InlineData("&")]
    [InlineData("fromOffset=0")]
    [InlineData("beforeExecutionNumber=1")]
    [InlineData("maxBytes=64")]
    public async Task The_selected_execution_accepts_no_query_key_and_refuses_before_dispatch_even_for_an_unknown_project(string query)
    {
        var (projectId, executions) = await SeedAsync(false);

        foreach (var route in new[] { Detail(Guid.NewGuid(), Guid.NewGuid(), query), Detail(projectId, executions[0].Id, query) })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(ProjectVerificationInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("x")]
    [InlineData("&")]
    [InlineData("BeforeExecutionNumber=5")]
    [InlineData("beforeexecutionnumber=5")]
    [InlineData("BEFOREEXECUTIONNUMBER=5")]
    [InlineData("beforeExecutionNumber=5&beforeExecutionNumber=6")]
    [InlineData("beforeExecutionNumber=5&BeforeExecutionNumber=6")]
    [InlineData("beforeExecutionNumber=5&x=1")]
    [InlineData("fromOffset=0")]
    [InlineData("maxBytes=64")]
    [InlineData("pageSize=5")]
    public async Task The_history_page_accepts_only_the_exact_ordinal_cursor_key_once_and_refuses_everything_else_before_dispatch(string query)
    {
        var (projectId, _) = await SeedAsync(false);

        foreach (var route in new[] { History(Guid.NewGuid(), query), History(projectId, query) })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(ProjectVerificationInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("x=1")]
    [InlineData("FromOffset=0")]
    [InlineData("MAXBYTES=64")]
    [InlineData("fromOffset=0&fromOffset=1")]
    [InlineData("maxBytes=64&maxBytes=128")]
    [InlineData("fromOffset=0&maxBytes=64&beforeExecutionNumber=1")]
    [InlineData("fromOffset=0&x=1")]
    [InlineData("&")]
    public async Task A_window_accepts_only_the_exact_ordinal_offset_and_cap_keys_once_and_refuses_everything_else_before_dispatch(string query)
    {
        var (projectId, executions) = await SeedAsync(false);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "abcdef"u8.ToArray());

        foreach (var route in new[]
                 {
                     Window(Guid.NewGuid(), Guid.NewGuid(), "stdout", query), Window(projectId, executions[0].Id, "stdout", query),
                     Window(projectId, executions[0].Id, "stdin", query),
                 })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(ProjectVerificationInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
        }

        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public async Task A_url_encoded_exact_key_and_a_bare_question_mark_are_admitted()
    {
        var (projectId, executions) = await SeedAsync(false, false);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(History(projectId) + "?")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Detail(projectId, executions[0].Id) + "?")).Status);
        var encoded = await GetAsync(History(projectId, "%62eforeExecutionNumber=2"));
        Assert.Equal(HttpStatusCode.OK, encoded.Status);
        Assert.Equal(1, Assert.Single(encoded.Body.GetProperty("items").EnumerateArray()).GetProperty("executionNumber").GetInt32());
    }

    public static TheoryData<string, string, string> RejectedValues() => new()
    {
        { "beforeExecutionNumber=0", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=-1", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=abc", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=1.5", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=1e1", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=05", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=%2B5", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=%205", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=%EF%BC%95", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=2147483648", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "beforeExecutionNumber=99999999999999999999", "validation.invalid_before_execution_number", "#/beforeExecutionNumber" },
        { "fromOffset=-1", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=abc", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=1.5", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=1e1", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=0x10", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%2B5", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=05", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%205", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=%EF%BC%95", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=9007199254740992", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=9223372036854775807", "validation.invalid_offset", "#/fromOffset" },
        { "fromOffset=99999999999999999999", "validation.invalid_offset", "#/fromOffset" },
        { "maxBytes=63", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=65537", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=0", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=-1", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=1.5", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=abc", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=064", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=%2B64", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=2147483648", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=99999999999999999999", "validation.invalid_max_bytes", "#/maxBytes" },
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task An_invalid_value_of_an_admitted_key_is_refused_with_the_shared_validation_problem_never_clamped_and_never_echoed(
        string query, string code, string pointer)
    {
        var (projectId, executions) = await SeedAsync(false);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "abcdef"u8.ToArray());
        var route = query.StartsWith("beforeExecutionNumber", StringComparison.Ordinal)
            ? History(projectId, query)
            : Window(projectId, executions[0].Id, "stdout", query);

        var response = await GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("application/problem+json", response.MediaType);
        Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(pointer, error.GetProperty("pointer").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("detail").GetString()));
        Assert.DoesNotContain("ValidationProblemDetails", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not valid", response.Text, StringComparison.Ordinal);
        Assert.Equal(0, StoreCalls);
    }

    // ---- identity and the fixed 404 ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_project_and_an_absent_or_foreign_execution_are_the_same_fixed_non_echoing_404()
    {
        var (projectId, executions) = await SeedAsync(false);
        var (otherProject, others) = await SeedAsync(false);
        var unknown = Guid.NewGuid();
        var routes = new[]
        {
            History(unknown), Detail(unknown, executions[0].Id), Detail(projectId, unknown), Detail(projectId, others[0].Id),
            Detail(otherProject, executions[0].Id), Window(unknown, executions[0].Id, "stdout"), Window(projectId, unknown, "stderr"),
            Window(projectId, others[0].Id, "stdout"), Window(projectId, executions[0].Id, "stdin"),
            Window(projectId, executions[0].Id, "STDOUT"), Window(projectId, executions[0].Id, "output"),
            Window(projectId, executions[0].Id, "stdout%20"),
        };

        var responses = new List<(HttpStatusCode Status, JsonElement Body, string Text, string? MediaType)>();
        foreach (var route in routes)
        {
            responses.Add(await GetAsync(route));
        }

        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(ProjectVerificationInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknown.ToString(), projectId.ToString(), otherProject.ToString(), executions[0].Id.ToString(), others[0].Id.ToString(), "stdin", "STDOUT" })
            {
                Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
            }
        });
        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
        Assert.Equal(0, StoreCalls);
    }

    // ---- closed wire graphs --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_history_page_is_the_closed_graph_of_at_most_twenty_exact_items_with_a_cursor_only_when_more_exist()
    {
        var (projectId, executions) = await SeedAsync(Enumerable.Repeat(false, 45).ToArray());

        var first = await GetAsync(History(projectId));
        var second = await GetAsync(History(projectId, "beforeExecutionNumber=26"));
        var last = await GetAsync(History(projectId, "beforeExecutionNumber=6"));

        Assert.All(new[] { first, second, last }, response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal("application/json", response.MediaType);
            Assert.Equal(PageMembers, Names(response.Body));
            Assert.Equal(projectId, response.Body.GetProperty("projectId").GetGuid());
            Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
            Assert.All(response.Body.GetProperty("items").EnumerateArray(), item => Assert.Equal(ItemMembers, Names(item)));
            AssertNoForbiddenText(response.Text);
        });
        Assert.Equal(20, first.Body.GetProperty("items").GetArrayLength());
        Assert.True(first.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(26, first.Body.GetProperty("nextBeforeExecutionNumber").GetInt32());
        Assert.Equal(25, second.Body.GetProperty("items")[0].GetProperty("executionNumber").GetInt32());
        Assert.Equal(6, second.Body.GetProperty("nextBeforeExecutionNumber").GetInt32());
        Assert.Equal(5, last.Body.GetProperty("items").GetArrayLength());
        Assert.False(last.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, last.Body.GetProperty("nextBeforeExecutionNumber").ValueKind);
        var newest = first.Body.GetProperty("items")[0];
        Assert.Equal(executions[44].Id, newest.GetProperty("executionId").GetGuid());
        Assert.Equal(45, newest.GetProperty("executionNumber").GetInt32());
        Assert.Equal("Unit tests", newest.GetProperty("commandName").GetString());
        Assert.Equal(Time(Now.AddSeconds(45)), newest.GetProperty("claimedAtUtc").GetString());
        Assert.Equal(
            Enumerable.Range(1, 45).Reverse(),
            first.Body.GetProperty("items").EnumerateArray().Concat(second.Body.GetProperty("items").EnumerateArray())
                .Concat(last.Body.GetProperty("items").EnumerateArray()).Select(item => item.GetProperty("executionNumber").GetInt32()));
    }

    [Fact]
    public async Task An_empty_project_history_and_an_unavailable_page_are_closed_200_answers_with_no_cursor()
    {
        var empty = await SeedProjectAsync();
        var (damaged, executions) = await SeedAsync(false, false);
        await ExecuteAsync($"UPDATE verification_executions SET ClaimedAtUtc = 'garbage' WHERE Id = {executions[1].Id}");

        var none = await GetAsync(History(empty));
        var unavailable = await GetAsync(History(damaged));

        Assert.Equal(HttpStatusCode.OK, none.Status);
        Assert.Equal(PageMembers, Names(none.Body));
        Assert.Equal(("Recorded", 0, false), (none.Body.GetProperty("status").GetString(), none.Body.GetProperty("items").GetArrayLength(), none.Body.GetProperty("hasMore").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, unavailable.Status);
        Assert.Equal(PageMembers, Names(unavailable.Body));
        Assert.Equal(("Unavailable", 0, false), (unavailable.Body.GetProperty("status").GetString(), unavailable.Body.GetProperty("items").GetArrayLength(), unavailable.Body.GetProperty("hasMore").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("nextBeforeExecutionNumber").ValueKind);
    }

    [Fact]
    public async Task The_selected_execution_is_the_closed_graph_of_its_exact_metadata_and_two_descriptors()
    {
        var (projectId, executions) = await SeedAsync(true);
        var execution = executions[0];
        var stdout = await RecordAsync(execution.Id, VerificationOutputPurpose.StandardOutput, "out text"u8.ToArray(), true);

        var response = await GetAsync(Detail(projectId, execution.Id));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(DetailMembers, Names(response.Body));
        Assert.Equal((projectId, execution.Id, "Recorded"), (
            response.Body.GetProperty("projectId").GetGuid(), response.Body.GetProperty("executionId").GetGuid(),
            response.Body.GetProperty("status").GetString()));
        var body = response.Body.GetProperty("execution");
        Assert.Equal(ExecutionMembers, Names(body));
        Assert.Equal((1, execution.CommandId, "Unit tests"), (
            body.GetProperty("executionNumber").GetInt32(), body.GetProperty("commandId").GetGuid(), body.GetProperty("commandName").GetString()));
        Assert.Equal((execution.WorkspaceId, execution.CheckpointId, 1), (
            body.GetProperty("workspaceId").GetGuid(), body.GetProperty("checkpointId").GetGuid(), body.GetProperty("checkpointNumber").GetInt32()));
        Assert.Equal((Fingerprint, Fingerprint), (
            body.GetProperty("claimFingerprintSha256").GetString(), body.GetProperty("completionFingerprintSha256").GetString()));
        Assert.Equal(("Failed", "Exited", 3), (
            body.GetProperty("status").GetString(), body.GetProperty("outcome").GetString(), body.GetProperty("exitCode").GetInt32()));
        Assert.Equal(Time(Now.AddSeconds(1)), body.GetProperty("claimedAtUtc").GetString());
        Assert.Equal(Time(Now.AddSeconds(1).AddTicks(10_000_001)), body.GetProperty("dispatchedAtUtc").GetString());
        Assert.Equal(Time(Now.AddSeconds(1).AddTicks(30_000_004)), body.GetProperty("completedAtUtc").GetString());
        var outputStream = body.GetProperty("standardOutput");
        Assert.Equal(StreamMembers, Names(outputStream));
        Assert.Equal(("Recorded", stdout.Row.Id, 8L, "CapturedWithKnownTruncation", true), (
            outputStream.GetProperty("status").GetString(), outputStream.GetProperty("artifactId").GetGuid(), outputStream.GetProperty("byteLength").GetInt64(),
            outputStream.GetProperty("captureOutcome").GetString(), outputStream.GetProperty("truncated").GetBoolean()));
        var errorStream = body.GetProperty("standardError");
        Assert.Equal(StreamMembers, Names(errorStream));
        Assert.Equal("NotRecorded", errorStream.GetProperty("status").GetString());
        Assert.All(new[] { "artifactId", "byteLength", "captureOutcome", "truncated" }, name => Assert.Equal(JsonValueKind.Null, errorStream.GetProperty(name).ValueKind));
        AssertNoForbiddenText(response.Text);
        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public async Task A_running_execution_has_null_outcome_exit_code_completion_fingerprint_and_completion_time_on_the_wire()
    {
        var projectId = await SeedProjectAsync();
        Guid executionId = Guid.Empty;
        await WithDbAsync(async db =>
        {
            var workspace = GitWorkspace.Prepare(Guid.NewGuid(), projectId, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", Sha, "main", Now);
            db.GitWorkspaces.Add(workspace);
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Sha, Fingerprint, []);
            db.GitCheckpoints.Add(checkpoint);
            var command = VerificationCommand.Configure(Guid.NewGuid(), projectId, 1, "Live", @"C:\tools\verify.exe", [], 60, true, Now);
            db.VerificationCommands.Add(command);
            await db.SaveChangesAsync();
            var execution = VerificationExecution.Claim(Guid.NewGuid(), projectId, 1, workspace, checkpoint, command, Now);
            db.VerificationExecutions.Add(execution);
            await db.SaveChangesAsync();
            executionId = execution.Id;
            return 0;
        });

        var response = await GetAsync(Detail(projectId, executionId));

        var body = response.Body.GetProperty("execution");
        Assert.Equal(ExecutionMembers, Names(body));
        Assert.Equal("Running", body.GetProperty("status").GetString());
        Assert.All(
            new[] { "outcome", "exitCode", "completionFingerprintSha256", "dispatchedAtUtc", "completedAtUtc" },
            name => Assert.Equal(JsonValueKind.Null, body.GetProperty(name).ValueKind));
    }

    [Fact]
    public async Task A_member_with_incoherent_recorded_facts_is_a_closed_200_unavailable_never_absent_and_names_no_stored_value()
    {
        var (projectId, executions) = await SeedAsync(false, false);
        await ExecuteAsync($"UPDATE verification_executions SET Status = {Secret}, ExitCode = 'x' WHERE Id = {executions[1].Id}");

        var damaged = await GetAsync(Detail(projectId, executions[1].Id));
        var sibling = await GetAsync(Detail(projectId, executions[0].Id));

        Assert.Equal(HttpStatusCode.OK, damaged.Status);
        Assert.Equal(DetailMembers, Names(damaged.Body));
        Assert.Equal("Unavailable", damaged.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, damaged.Body.GetProperty("execution").ValueKind);
        Assert.DoesNotContain(Secret, damaged.Text, StringComparison.Ordinal);
        Assert.Equal("Recorded", sibling.Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_damaged_stream_descriptor_is_unavailable_on_the_wire_without_hiding_its_sibling_stream()
    {
        var (projectId, executions) = await SeedAsync(false);
        var stdout = await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "out"u8.ToArray());
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardError, "error"u8.ToArray());
        await ExecuteAsync($"UPDATE verification_output_artifacts SET ByteLength = 1.5 WHERE Id = {stdout.Row.Id}");

        var response = await GetAsync(Detail(projectId, executions[0].Id));

        var body = response.Body.GetProperty("execution");
        Assert.Equal("Unavailable", body.GetProperty("standardOutput").GetProperty("status").GetString());
        Assert.All(new[] { "artifactId", "byteLength", "captureOutcome", "truncated" },
            name => Assert.Equal(JsonValueKind.Null, body.GetProperty("standardOutput").GetProperty(name).ValueKind));
        Assert.Equal("Recorded", body.GetProperty("standardError").GetProperty("status").GetString());
        Assert.Equal(5, body.GetProperty("standardError").GetProperty("byteLength").GetInt64());
    }

    [Fact]
    public async Task An_older_manual_execution_beyond_the_latest_twenty_is_reachable_through_the_pages_and_its_metadata()
    {
        var (projectId, executions) = await SeedAsync(Enumerable.Repeat(false, 30).ToArray());
        var oldest = executions[1];

        var newest = await GetAsync(History(projectId));
        var page = await GetAsync(History(projectId, $"beforeExecutionNumber={newest.Body.GetProperty("nextBeforeExecutionNumber").GetInt32()}"));
        var detail = await GetAsync(Detail(projectId, oldest.Id));

        Assert.DoesNotContain(newest.Body.GetProperty("items").EnumerateArray(), item => item.GetProperty("executionId").GetGuid() == oldest.Id);
        Assert.Contains(page.Body.GetProperty("items").EnumerateArray(), item => item.GetProperty("executionId").GetGuid() == oldest.Id);
        Assert.Equal(2, detail.Body.GetProperty("execution").GetProperty("executionNumber").GetInt32());
        await WithDbAsync(async db =>
        {
            Assert.Empty(await db.AttemptVerificationEvidence.ToListAsync());
            Assert.Empty(await db.LocalExecutionSequenceSteps.ToListAsync());
            return 0;
        });
    }

    // ---- no writes, no artifact access, a second host ------------------------------------------------------------------------------

    [Fact]
    public async Task Metadata_and_history_requests_write_nothing_and_open_no_artifact_file()
    {
        var (projectId, executions) = await SeedAsync(false, true);
        await RecordAsync(executions[0].Id, VerificationOutputPurpose.StandardOutput, "abc"u8.ToArray());
        await RecordAsync(executions[1].Id, VerificationOutputPurpose.StandardError, "def"u8.ToArray());
        var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var before = await SnapshotAsync();

        await GetAsync(History(projectId));
        await GetAsync(History(projectId, "beforeExecutionNumber=2"));
        await GetAsync(Detail(projectId, executions[0].Id));
        await GetAsync(Detail(projectId, executions[1].Id));
        await GetAsync(Detail(projectId, Guid.NewGuid()));

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(files, Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(0, StoreCalls);
    }

    [Fact]
    public async Task A_second_host_over_the_same_database_returns_identical_bodies()
    {
        var (projectId, executions) = await SeedAsync(false, true, false);
        await RecordAsync(executions[1].Id, VerificationOutputPurpose.StandardError, "def"u8.ToArray());
        var first = new[]
        {
            (await GetAsync(History(projectId))).Text, (await GetAsync(Detail(projectId, executions[1].Id))).Text,
        };

        using var second = Host.WithWebHostBuilder(_ => { });
        using var client = second.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var reopened = new[]
        {
            await (await client.GetAsync(History(projectId))).Content.ReadAsStringAsync(),
            await (await client.GetAsync(Detail(projectId, executions[1].Id))).Content.ReadAsStringAsync(),
        };

        Assert.Equal(first, reopened);
    }

    // ---- OpenAPI ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_three_bodyless_routes_their_bounded_queries_and_the_closed_responses()
    {
        var generator = Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var paths = document["paths"]!.AsObject();

        var history = paths["/api/projects/{projectId}/verification-history"]!["get"]!;
        var detail = paths["/api/projects/{projectId}/verification-history/{executionId}"]!["get"]!;
        var window = paths["/api/projects/{projectId}/verification-history/{executionId}/artifact-window/{stream}"]!["get"]!;

        static IEnumerable<(string, string)> Parameters(JsonNode operation) => operation["parameters"]!.AsArray()
            .Select(parameter => (parameter!["name"]!.GetValue<string>(), parameter["in"]!.GetValue<string>())).Order();
        Assert.Equal([("beforeExecutionNumber", "query"), ("projectId", "path")], Parameters(history));
        Assert.Equal([("executionId", "path"), ("projectId", "path")], Parameters(detail));
        Assert.Equal(
            [("executionId", "path"), ("fromOffset", "query"), ("maxBytes", "query"), ("projectId", "path"), ("stream", "path")],
            Parameters(window));
        foreach (var (operation, response) in new[]
                 {
                     (history, "GetProjectVerificationHistoryResponse"), (detail, "GetProjectVerificationInspectionResponse"),
                     (window, "GetProjectVerificationOutputWindowResponse"),
                 })
        {
            Assert.Null(operation["requestBody"]);
            var responses = operation["responses"]!.AsObject();
            Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
            Assert.Contains(response, responses["200"]!.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        }

        Assert.Contains("int32", history["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "beforeExecutionNumber")!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("int64", window["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "fromOffset")!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("int32", window["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "maxBytes")!.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(PageMembers, PropertiesOf(document, "GetProjectVerificationHistoryResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(ItemMembers, PropertiesOf(document, "ProjectVerificationHistoryItemResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(DetailMembers, PropertiesOf(document, "GetProjectVerificationInspectionResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(ExecutionMembers, PropertiesOf(document, "ProjectVerificationInspectionExecutionResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(StreamMembers, PropertiesOf(document, "ProjectVerificationInspectionStreamResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["artifact", "executionId", "nextOffset", "projectId", "status", "stream", "text", "totalLength"],
            PropertiesOf(document, "GetProjectVerificationOutputWindowResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
    }
}
