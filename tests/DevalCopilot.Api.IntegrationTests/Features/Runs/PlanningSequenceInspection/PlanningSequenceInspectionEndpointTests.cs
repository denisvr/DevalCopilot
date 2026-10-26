using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequenceInspection;

/// <summary>
/// ADR-0068's two protected, bodyless reads against the real host over one REAL recorded five-step planning sequence: complete
/// authentication, method, body, query, identity and fixed-error matrices; the closed wire of every payload; representative integrated
/// index and selected-step reads of every fixed role; the absence of any write, artifact read or provider call for any request; and the
/// actual OpenAPI contract. Storage damage and revision limits are proved in the sibling class, which owns its own databases.
/// </summary>
public sealed class PlanningSequenceInspectionEndpointTests(PlanningSequenceInspectionApiFixture fixture)
    : IClassFixture<PlanningSequenceInspectionApiFixture>
{
    private static readonly string[] IndexMembers = ["runId", "sequence", "status", "steps"];

    private static readonly string[] HeaderMembers =
    [
        "armedAtUtc", "checkpointId", "endedAtUtc", "fingerprintSha256", "reasonCode", "recordedState", "revision", "sequenceId",
        "stopRequestedAtUtc", "updatedAtUtc", "workspaceId",
    ];

    private static readonly string[] StepMembers =
    [
        "attemptId", "inputProposalMessageId", "ordinal", "predecessorAttemptId", "preparationDeadlineUtc", "preparationReservedAtUtc",
        "provider", "reasonCode", "recordedState", "responseContract", "revision", "role", "stepId", "updatedAtUtc",
    ];

    private static readonly string[] StepResponseMembers = ["agent", "runId", "sequenceId", "status", "step", "stepId"];

    private static readonly string[] AgentMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] Forbidden =
    [
        "objectiveSha256", "objective", "detailCode", "preparationOwnerToken", "ownerToken", "workspacePath", "branchName", "summary",
        "structuredContent", "Add the ledger table", "Revised ledger", "Challenge 1", "devalcopilot-planning-sequence",
    ];

    private HttpClient Client(bool authenticated = true) => fixture.Host.CreateClient(authenticated);

    private Guid RunId => fixture.Scene.RunId;

    private PlanningSequenceStep Row(int ordinal) => fixture.Steps[ordinal];

    private static string Index(Guid run) => $"/api/runs/{run}/planning-sequence/inspection";

    private static string Step(Guid run, Guid step) => $"{Index(run)}/steps/{step}";

    private static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string route)
    {
        using var client = Client();
        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private IEnumerable<string> AllRoutes() => [Index(RunId), Step(RunId, Row(0).Id)];

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void AssertNoForbiddenText(string text)
    {
        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- authentication and methods ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var before = fixture.Host.Store.Calls;
        foreach (var route in AllRoutes())
        {
            using var anonymous = Client(authenticated: false);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

            using var wrong = Client(authenticated: false);
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

            using var queryOnly = Client(authenticated: false);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await queryOnly.GetAsync($"{route}?access_token={PlanningSequenceInspectionHost.Secret}")).StatusCode);

            using var client = Client();
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        }

        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Fact]
    public void Exactly_two_inspection_routes_exist_each_an_authorized_get_with_no_anonymous_access()
    {
        var endpoints = fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("planning-sequence/inspection", StringComparison.Ordinal) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "api/runs/{runId:guid}/planning-sequence/inspection",
                "api/runs/{runId:guid}/planning-sequence/inspection/steps/{stepId:guid}",
            ],
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText));
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        }

        // Every other operation under the same resource keeps its own authorization: nothing here added an anonymous endpoint.
        Assert.DoesNotContain(
            fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("planning-sequence", StringComparison.Ordinal) == true),
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
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_even_for_an_unknown_run()
    {
        var routes = new[] { Index(Guid.NewGuid()), Step(Guid.NewGuid(), Guid.NewGuid()), Index(RunId), Step(RunId, Row(0).Id) };
        foreach (var route in routes)
        {
            foreach (var (name, shape) in BodyShapes())
            {
                using var client = Client();
                using var response = await client.SendAsync(shape(new HttpRequestMessage(HttpMethod.Get, route)));
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{name} {route}");
                using var document = JsonDocument.Parse(text);
                var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
                Assert.Equal(PlanningSequenceInspectionErrors.BodyNotAllowedCode, error.GetProperty("code").GetString());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }
    }

    // ---- query ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("x")]
    [InlineData("fromOffset=0")]
    [InlineData("a=1&b=2")]
    [InlineData("&")]
    [InlineData("runId=00000000-0000-0000-0000-000000000000")]
    public async Task Both_routes_accept_no_query_key_and_refuse_before_dispatch_even_for_an_unknown_run(string query)
    {
        foreach (var route in new[] { Index(Guid.NewGuid()), Step(Guid.NewGuid(), Guid.NewGuid()), Index(RunId), Step(RunId, Row(0).Id) })
        {
            var response = await GetAsync($"{route}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(PlanningSequenceInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_bare_question_mark_is_no_query_for_either_route()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Index(RunId) + "?")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Step(RunId, Row(0).Id) + "?")).Status);
    }

    // ---- identity and the fixed 404 ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_a_missing_step_a_foreign_step_and_a_step_of_a_run_without_a_sequence_are_the_same_fixed_404()
    {
        var unknown = Guid.NewGuid();
        var step = Row(0).Id;
        var runWithoutSequence = await fixture.Harness.SeedSceneAsync();
        var routes = new[]
        {
            Index(unknown),
            Step(unknown, step),
            Step(RunId, unknown),
            Step(RunId, Guid.Empty),
            Step(Guid.Empty, step),
            Step(runWithoutSequence.RunId, step),
        };
        var responses = new List<(HttpStatusCode Status, JsonElement Body, string Text)>();
        foreach (var route in routes)
        {
            responses.Add(await GetAsync(route));
        }

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(PlanningSequenceInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknown.ToString(), RunId.ToString(), step.ToString(), runWithoutSequence.RunId.ToString() })
            {
                Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Theory]
    [InlineData("/api/runs/not-a-guid/planning-sequence/inspection")]
    [InlineData("/api/runs/00000000000000000000000000000000x/planning-sequence/inspection/steps/not-a-guid")]
    public async Task A_route_identity_that_is_not_a_guid_is_not_a_route(string route) =>
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(route)).Status);

    [Fact]
    public async Task A_step_of_another_runs_sequence_is_outside_this_runs_sequence()
    {
        await using var other = new Features.Runs.PlanningSequences.PlanningSequenceHostedHarness();
        var scene = await other.SeedSceneAsync();
        var armed = await other.ArmAsync(scene);
        await other.StartHostsAsync();
        await other.WaitForEndAsync(armed.SequenceId);
        await other.StopHostsAsync();

        Guid foreign;
        await using (var context = other.OpenContext())
        {
            foreign = (await context.PlanningSequenceSteps.AsNoTracking().OrderBy(step => step.Ordinal).FirstAsync()).Id;
        }

        var response = await GetAsync(Step(RunId, foreign));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal(
            PlanningSequenceInspectionErrors.NotFoundCode, response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    // ---- representative integrated reads and the closed wire ---------------------------------------------------------------------

    [Fact]
    public async Task The_index_is_the_closed_graph_of_the_recorded_sequence_in_order_with_canonical_strings()
    {
        var response = await GetAsync(Index(RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(IndexMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(RunId, response.Body.GetProperty("runId").GetGuid());
        var header = response.Body.GetProperty("sequence");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(fixture.Sequence.Id, header.GetProperty("sequenceId").GetGuid());
        Assert.Equal(fixture.Scene.WorkspaceId, header.GetProperty("workspaceId").GetGuid());
        Assert.Equal(fixture.Scene.CheckpointId, header.GetProperty("checkpointId").GetGuid());
        Assert.Equal(fixture.Scene.Fingerprint, header.GetProperty("fingerprintSha256").GetString());
        Assert.Equal("AwaitingHumanDecision", header.GetProperty("recordedState").GetString());
        Assert.Equal("second_round_escalated", header.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.String, header.GetProperty("revision").ValueKind);
        Assert.Equal(fixture.Sequence.Revision.ToString(CultureInfo.InvariantCulture), header.GetProperty("revision").GetString());
        Assert.Equal(Time(fixture.Sequence.ArmedAtUtc), header.GetProperty("armedAtUtc").GetString());
        Assert.Equal(Time(fixture.Sequence.UpdatedAtUtc), header.GetProperty("updatedAtUtc").GetString());
        Assert.Equal(Time(fixture.Sequence.EndedAtUtc!.Value), header.GetProperty("endedAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, header.GetProperty("stopRequestedAtUtc").ValueKind);

        var steps = response.Body.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(5, steps.Count);
        string[] roles = ["Planner", "CriticalReviewer", "Resolver", "CriticalReviewer", "Resolver"];
        string[] providers = ["Codex", "ClaudeCode", "Codex", "ClaudeCode", "Codex"];
        string[] contracts = ["Proposal", "CriticalReview", "ChallengeResolution", "CriticalReview", "ChallengeResolution"];
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var row = fixture.Steps[index];
            Assert.Equal(StepMembers, Names(step));
            Assert.Equal(row.Id, step.GetProperty("stepId").GetGuid());
            Assert.Equal(index, step.GetProperty("ordinal").GetInt32());
            Assert.Equal(roles[index], step.GetProperty("role").GetString());
            Assert.Equal(providers[index], step.GetProperty("provider").GetString());
            Assert.Equal(contracts[index], step.GetProperty("responseContract").GetString());
            Assert.Equal("Concluded", step.GetProperty("recordedState").GetString());
            Assert.Equal(JsonValueKind.Null, step.GetProperty("reasonCode").ValueKind);
            Assert.Equal(JsonValueKind.String, step.GetProperty("revision").ValueKind);
            Assert.Equal(row.Revision.ToString(CultureInfo.InvariantCulture), step.GetProperty("revision").GetString());
            Assert.Equal(row.AttemptId, step.GetProperty("attemptId").GetGuid());
            Assert.Equal(Time(row.PreparationReservedAtUtc), step.GetProperty("preparationReservedAtUtc").GetString());
            Assert.Equal(Time(row.PreparationDeadlineUtc), step.GetProperty("preparationDeadlineUtc").GetString());
            if (index == 0)
            {
                Assert.Equal(JsonValueKind.Null, step.GetProperty("predecessorAttemptId").ValueKind);
                Assert.Equal(JsonValueKind.Null, step.GetProperty("inputProposalMessageId").ValueKind);
            }
            else
            {
                Assert.Equal(fixture.Steps[index - 1].AttemptId, step.GetProperty("predecessorAttemptId").GetGuid());
                Assert.Equal(row.InputProposalMessageId, step.GetProperty("inputProposalMessageId").GetGuid());
            }
        }

        AssertNoForbiddenText(response.Text);
    }

    [Fact]
    public async Task Every_step_returns_the_closed_step_and_only_the_immutable_attempt_header_of_its_fixed_role()
    {
        var attempts = fixture.Attempts.ToDictionary(attempt => attempt.Id);
        string[] roles = ["Planner", "CriticalReviewer", "Resolver", "CriticalReviewer", "Resolver"];

        foreach (var row in fixture.Steps)
        {
            var response = await GetAsync(Step(RunId, row.Id));

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(StepResponseMembers, Names(response.Body));
            Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
            Assert.Equal(RunId, response.Body.GetProperty("runId").GetGuid());
            Assert.Equal(fixture.Sequence.Id, response.Body.GetProperty("sequenceId").GetGuid());
            Assert.Equal(row.Id, response.Body.GetProperty("stepId").GetGuid());
            Assert.Equal(StepMembers, Names(response.Body.GetProperty("step")));
            Assert.Equal(roles[row.Ordinal], response.Body.GetProperty("step").GetProperty("role").GetString());
            var agent = response.Body.GetProperty("agent");
            Assert.Equal(AgentMembers, Names(agent));
            var attempt = attempts[row.AttemptId!.Value];
            Assert.Equal(attempt.Id, agent.GetProperty("id").GetGuid());
            Assert.Equal(attempt.AttemptNumber, agent.GetProperty("attemptNumber").GetInt32());
            Assert.Equal(attempt.AgentRole.ToString(), agent.GetProperty("role").GetString());
            Assert.Equal(attempt.AgentProvider.ToString(), agent.GetProperty("provider").GetString());
            Assert.Equal(attempt.AgentResponseContract.ToString(), agent.GetProperty("responseContract").GetString());
            AssertNoForbiddenText(response.Text);
        }
    }

    [Fact]
    public async Task A_run_without_a_sequence_is_not_recorded_with_a_null_header_and_an_empty_list()
    {
        var scene = await fixture.Harness.SeedSceneAsync();

        var index = await GetAsync(Index(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, index.Status);
        Assert.Equal(IndexMembers, Names(index.Body));
        Assert.Equal("NotRecorded", index.Body.GetProperty("status").GetString());
        Assert.Equal(scene.RunId, index.Body.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.Null, index.Body.GetProperty("sequence").ValueKind);
        Assert.Equal(0, index.Body.GetProperty("steps").GetArrayLength());
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.Harness.OpenContext();
        var counts = new[]
        {
            await db.Events.CountAsync(),
            await db.Attempts.CountAsync(),
            await db.CollaborationMessages.CountAsync(),
            await db.AttemptInputMessages.CountAsync(),
            await db.PlanningSequenceSteps.CountAsync(),
        };
        var sequences = (await db.PlanningSequences.AsNoTracking().ToListAsync()).Select(sequence => $"{sequence.Revision}:{sequence.State}");
        var steps = (await db.PlanningSequenceSteps.AsNoTracking().ToListAsync()).Select(step => $"{step.Revision}:{step.State}");
        return string.Join("|", counts) + "#" + string.Join(",", sequences) + ";" + string.Join(",", steps);
    }

    [Fact]
    public async Task Reading_every_route_writes_nothing_reads_no_artifact_and_invokes_no_provider()
    {
        var before = await SnapshotAsync();
        var invocations = (fixture.Harness.Planner.InvocationCount, fixture.Harness.Reviewer.InvocationCount, fixture.Harness.Resolver.InvocationCount);
        var captures = fixture.Harness.Evidence.Captures;
        var calls = fixture.Host.Store.Calls;

        await GetAsync(Index(RunId));
        foreach (var row in fixture.Steps)
        {
            await GetAsync(Step(RunId, row.Id));
        }

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(
            invocations, (fixture.Harness.Planner.InvocationCount, fixture.Harness.Reviewer.InvocationCount, fixture.Harness.Resolver.InvocationCount));
        Assert.Equal(captures, fixture.Harness.Evidence.Captures);
        Assert.Equal(calls, fixture.Host.Store.Calls);
    }

    // ---- the actual OpenAPI contract -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_two_bodyless_routes_without_query_and_the_closed_responses()
    {
        var generator = fixture.Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var paths = document["paths"]!;
        var routes = new Dictionary<string, (string[] Parameters, string Response)>
        {
            ["/api/runs/{runId}/planning-sequence/inspection"] = (["runId:path"], "GetPlanningSequenceInspectionResponse"),
            ["/api/runs/{runId}/planning-sequence/inspection/steps/{stepId}"] =
                (["runId:path", "stepId:path"], "GetPlanningStepInspectionResponse"),
        };

        foreach (var (route, expected) in routes)
        {
            var path = paths[route]!.AsObject();
            Assert.Equal(["get"], path.Select(item => item.Key));
            var operation = path["get"]!;
            Assert.Null(operation["requestBody"]);
            Assert.Equal(
                expected.Parameters,
                operation["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>() + ":" + p["in"]!.GetValue<string>()).Order(StringComparer.Ordinal));
            var responses = operation["responses"]!.AsObject();
            Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
            Assert.Contains(expected.Response, responses["200"]!.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
            Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        }

        Assert.Equal(IndexMembers, Properties(document, "GetPlanningSequenceInspectionResponse"));
        Assert.Equal(HeaderMembers, Properties(document, "PlanningSequenceInspectionHeaderResponse"));
        Assert.Equal(StepMembers, Properties(document, "PlanningSequenceInspectionStepResponse"));
        Assert.Equal(StepResponseMembers, Properties(document, "GetPlanningStepInspectionResponse"));
        Assert.Equal(AgentMembers, Properties(document, "PlanningStepInspectionAgentResponse"));
        var schemas = document["components"]!["schemas"]!;
        foreach (var name in new[] { "revision", "armedAtUtc", "updatedAtUtc", "fingerprintSha256", "recordedState" })
        {
            Assert.Contains("string", schemas["PlanningSequenceInspectionHeaderResponse"]!["properties"]![name]!.ToJsonString(), StringComparison.Ordinal);
        }

        Assert.Contains("string", schemas["PlanningSequenceInspectionStepResponse"]!["properties"]!["revision"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var internalName in new[] { "PlanningSequenceInspectionHeader", "PlanningSequenceInspectionStep", "GetPlanningStepInspectionQueryResult" })
        {
            Assert.Null(schemas[internalName]);
        }
    }

    private static string[] Properties(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        return properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }
}

