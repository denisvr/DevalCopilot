using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionInspection;

/// <summary>
/// ADR-0061's three protected, bodyless reads against the real host over one REAL recorded cycle: complete authentication, method, body,
/// query, identity and fixed-error matrices; the closed wire of every payload; representative integrated index, step and sealed-window
/// reads of Agent and verification evidence through the real artifact store; the absence of any write or of any artifact read for a
/// refused request; and the actual OpenAPI contract.
/// </summary>
public sealed class LocalExecutionInspectionEndpointTests(LocalExecutionInspectionApiFixture fixture) : IClassFixture<LocalExecutionInspectionApiFixture>
{
    private static readonly string[] IndexMembers = ["runId", "sequence", "status", "steps"];

    private static readonly string[] HeaderMembers =
    [
        "admissionMode", "armedAtUtc", "checkpointId", "correctionAllowance", "endedAtUtc", "fingerprintSha256", "planAuthority", "planProposalMessageId",
        "reasonCode", "recipeCount", "recordedState", "revision", "sequenceId", "updatedAtUtc", "workspaceId",
    ];

    private static readonly string[] StepMembers =
    [
        "claimedAtUtc", "kind", "operationId", "ordinal", "passNumber", "preparationDeadlineUtc", "preparationReservedAtUtc", "reasonCode",
        "recipeName", "recipeOrdinal", "recordedState", "reportMessageId", "resultDeadlineUtc", "sourceCheckpointId",
        "sourceFingerprintSha256", "stepId", "subjectAttemptId",
    ];

    private static readonly string[] StepResponseMembers = ["agent", "runId", "sequenceId", "status", "step", "stepId", "verification"];

    private static readonly string[] AgentMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] VerificationMembers =
    [
        "claimedAtUtc", "commandName", "completedAtUtc", "dispatchedAtUtc", "executionId", "executionNumber", "exitCode", "outcome",
        "standardError", "standardOutput", "status",
    ];

    private static readonly string[] StreamMembers = ["artifactId", "byteLength", "captureOutcome", "status", "truncated"];

    private static readonly string[] WindowMembers =
        ["artifact", "executionId", "nextOffset", "runId", "sequenceId", "status", "stepId", "stream", "text", "totalLength"];

    private static readonly string[] Forbidden =
    [
        "sentinel-tools", "build-tool", "--recipe-", "devalcopilot-local-sequence-ws", ".sealed", "sha256:", "verifications", "ownerToken",
        "preparationOwnerToken", "settingsSnapshot", "confirmation", "planInputs",
    ];

    private HttpClient Client(bool authenticated = true) => fixture.Host.CreateClient(authenticated);

    private Guid RunId => fixture.Lineage.RunId;

    private LocalExecutionSequenceStep Row(int ordinal) => fixture.Steps[ordinal];

    private LocalExecutionSequenceStep FailedVerification => fixture.Steps.Single(step =>
        step.Kind == LocalExecutionStepKind.Verification && step.PassNumber == 1 && step.RecipeOrdinal == 0);

    private LocalExecutionSequenceStep PassedVerification => fixture.Steps.Single(step =>
        step.Kind == LocalExecutionStepKind.Verification && step.PassNumber == 1 && step.RecipeOrdinal == 1);

    private static string Index(Guid run) => $"/api/runs/{run}/local-execution-sequence/inspection";

    private static string Step(Guid run, Guid step) => $"{Index(run)}/steps/{step}";

    private static string Window(Guid run, Guid step, string stream, string? query = null) =>
        $"{Step(run, step)}/artifact-window/{stream}" + (query is null ? string.Empty : "?" + query);

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

    private IEnumerable<string> AllRoutes() =>
    [
        Index(RunId),
        Step(RunId, Row(0).Id),
        Window(RunId, FailedVerification.Id, "stdout"),
    ];

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
            var separator = route.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await queryOnly.GetAsync($"{route}{separator}access_token={LocalExecutionInspectionHost.Secret}")).StatusCode);

            using var client = Client();
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        }

        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Fact]
    public void Exactly_three_inspection_routes_exist_and_each_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("local-execution-sequence/inspection", StringComparison.Ordinal) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "api/runs/{runId:guid}/local-execution-sequence/inspection",
                "api/runs/{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}",
                "api/runs/{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}/artifact-window/{stream}",
            ],
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText));
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        }
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
    public async Task A_request_body_is_refused_with_the_fixed_400_before_dispatch_even_for_an_unknown_run_and_reads_no_artifact()
    {
        var before = fixture.Host.Store.Calls;
        var routes = new[]
        {
            Index(Guid.NewGuid()),
            Step(Guid.NewGuid(), Guid.NewGuid()),
            Window(Guid.NewGuid(), Guid.NewGuid(), "stdout"),
            Window(RunId, FailedVerification.Id, "stdout"),
            Window(RunId, FailedVerification.Id, "stdout", "fromOffset=0&maxBytes=64"),
        };
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
                Assert.Equal(LocalExecutionInspectionErrors.BodyNotAllowedCode, error.GetProperty("code").GetString());
                Assert.DoesNotContain("{}", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    // ---- query ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("x=1")]
    [InlineData("=1")]
    [InlineData("x")]
    [InlineData("fromOffset=0")]
    [InlineData("maxBytes=64")]
    [InlineData("a=1&b=2")]
    [InlineData("&")]
    public async Task The_index_and_the_step_accept_no_query_key_and_refuse_before_dispatch_even_for_an_unknown_run(string query)
    {
        foreach (var route in new[] { Index(Guid.NewGuid()), Step(Guid.NewGuid(), Guid.NewGuid()), Index(RunId), Step(RunId, Row(0).Id) })
        {
            var response = await GetAsync($"{route}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalExecutionInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("x=1", response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_bare_question_mark_is_no_query_for_the_index_and_the_step()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Index(RunId) + "?")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Step(RunId, Row(0).Id) + "?")).Status);
    }

    [Theory]
    [InlineData("foo=1")]
    [InlineData("FromOffset=1")]
    [InlineData("fromoffset=1")]
    [InlineData("MAXBYTES=64")]
    [InlineData("maxbytes=64")]
    [InlineData("fromOffset=1&fromOffset=2")]
    [InlineData("fromOffset=1&FromOffset=1")]
    [InlineData("maxBytes=64&maxBytes=64")]
    [InlineData("maxBytes=64&MaxBytes=65")]
    [InlineData("fromOffset=1&x=2")]
    [InlineData("=1")]
    [InlineData("%66romOffset%00=1")]
    public async Task The_window_admits_only_the_exact_ordinal_keys_each_once_and_refuses_everything_else_before_dispatch(string query)
    {
        var before = fixture.Host.Store.Calls;

        foreach (var route in new[] { Window(Guid.NewGuid(), Guid.NewGuid(), "stdout", query), Window(RunId, FailedVerification.Id, "stdout", query) })
        {
            var response = await GetAsync(route);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalExecutionInspectionErrors.QueryNotAllowedCode, error.GetProperty("code").GetString());
            Assert.DoesNotContain("foo", response.Text, StringComparison.Ordinal);
        }

        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fromOffset=0")]
    [InlineData("maxBytes=64")]
    [InlineData("fromOffset=2&maxBytes=64")]
    [InlineData("maxBytes=64&fromOffset=2")]
    [InlineData("%66romOffset=2")]
    [InlineData("from%4Fffset=1")]
    [InlineData("fromOffset=&maxBytes=")]
    [InlineData("fromOffset")]
    [InlineData("fromOffset=1&&")]
    public async Task The_window_admits_the_exact_keys_in_any_order_after_url_decoding(string? query)
    {
        var response = await GetAsync(Window(RunId, FailedVerification.Id, "stdout", query));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
    }

    public static TheoryData<string, string, string> RejectedValues() => new()
    {
        { "fromOffset=-1", "validation.invalid_offset", "#/fromOffset" },
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
        { "maxBytes=1.5", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=abc", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=064", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=%2B64", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=2147483648", "validation.invalid_max_bytes", "#/maxBytes" },
        { "maxBytes=99999999999999999999", "validation.invalid_max_bytes", "#/maxBytes" },
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task An_invalid_offset_or_cap_keeps_the_shared_validation_problem_is_never_clamped_never_echoed_and_reads_nothing(
        string query, string code, string pointer)
    {
        var before = fixture.Host.Store.Calls;

        var response = await GetAsync(Window(RunId, FailedVerification.Id, "stdout", query));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(pointer, error.GetProperty("pointer").GetString());
        Assert.DoesNotContain("ValidationProblemDetails", response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(query, Normalize(response.Text), StringComparison.Ordinal);
        var rejected = query.Split('=')[1];
        if (rejected.Length >= 4)
        {
            Assert.DoesNotContain(rejected, Normalize(response.Text), StringComparison.Ordinal);
        }
        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Fact]
    public async Task An_offset_past_the_recorded_length_is_the_validation_refusal_before_any_read_and_the_exact_end_is_empty()
    {
        var length = (await GetAsync(Window(RunId, FailedVerification.Id, "stdout"))).Body.GetProperty("totalLength").GetInt64();
        var before = fixture.Host.Store.Calls;

        var past = await GetAsync(Window(RunId, FailedVerification.Id, "stdout", $"fromOffset={length + 1}"));
        var atEnd = await GetAsync(Window(RunId, FailedVerification.Id, "stdout", $"fromOffset={length}"));

        Assert.Equal(HttpStatusCode.BadRequest, past.Status);
        Assert.Equal("validation.invalid_offset", past.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("Ok", atEnd.Body.GetProperty("status").GetString());
        Assert.Equal(string.Empty, atEnd.Body.GetProperty("text").GetString());
        Assert.Equal(length, atEnd.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(before + 1, fixture.Host.Store.Calls);
    }

    // ---- identity and the fixed 404 ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_step_foreign_step_non_verification_step_and_unsupported_stream_are_the_same_fixed_404_and_read_nothing()
    {
        var unknown = Guid.NewGuid();
        var agentStep = Row(0).Id;
        var before = fixture.Host.Store.Calls;
        var routes = new[]
        {
            Index(unknown),
            Step(unknown, agentStep),
            Step(RunId, unknown),
            Step(RunId, Guid.Empty),
            Step(Guid.Empty, agentStep),
            Window(unknown, FailedVerification.Id, "stdout"),
            Window(RunId, unknown, "stdout"),
            Window(RunId, agentStep, "stdout"),
            Window(RunId, FailedVerification.Id, "stdin"),
            Window(RunId, FailedVerification.Id, "STDOUT"),
            Window(RunId, FailedVerification.Id, "context-manifest"),
            Window(RunId, FailedVerification.Id, "x-SECRET-SENTINEL"),
        };
        var responses = new List<(HttpStatusCode Status, JsonElement Body, string Text)>();
        foreach (var route in routes)
        {
            responses.Add(await GetAsync(route));
        }

        // The index of an unknown run is the one fixed 404; a known run without a sequence is NotRecorded (see its own fact).
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknown.ToString(), RunId.ToString(), agentStep.ToString(), "stdin", "SECRET-SENTINEL" })
            {
                Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Theory]
    [InlineData("/api/runs/not-a-guid/local-execution-sequence/inspection")]
    [InlineData("/api/runs/00000000000000000000000000000000x/local-execution-sequence/inspection/steps/not-a-guid")]
    public async Task A_route_identity_that_is_not_a_guid_is_not_a_route(string route) =>
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(route)).Status);

    [Fact]
    public async Task A_step_of_another_runs_sequence_is_outside_this_runs_sequence()
    {
        await using var other = new LocalExecutionSequenceHostedHarness();
        var lineage = await other.SeedLineageAsync(recipeCount: 1);
        var armed = await other.ArmAsync(lineage);
        await other.StartHostsAsync();
        await other.WaitForEndAsync(armed.SequenceId);
        await other.StopHostsAsync();
        var foreign = (await other.StepsAsync(armed.SequenceId))[0].Id;

        var response = await GetAsync(Step(RunId, foreign));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal(
            LocalExecutionInspectionErrors.NotFoundCode, response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    // ---- representative integrated reads and the closed wire ---------------------------------------------------------------------

    [Fact]
    public async Task The_index_is_the_closed_graph_of_the_recorded_cycle_in_order_with_canonical_strings()
    {
        var response = await GetAsync(Index(RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(IndexMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(RunId, response.Body.GetProperty("runId").GetGuid());
        var header = response.Body.GetProperty("sequence");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal("Immediate", header.GetProperty("admissionMode").GetString());
        Assert.Equal(fixture.Sequence.Id, header.GetProperty("sequenceId").GetGuid());
        Assert.Equal(fixture.Sequence.PlanProposalMessageId, header.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal("AcceptedRoot", header.GetProperty("planAuthority").GetString());
        Assert.Equal(fixture.Lineage.WorkspaceId, header.GetProperty("workspaceId").GetGuid());
        Assert.Equal(fixture.Lineage.CheckpointId, header.GetProperty("checkpointId").GetGuid());
        Assert.Equal("ReadyForHumanApproval", header.GetProperty("recordedState").GetString());
        Assert.Equal("review_approved", header.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.String, header.GetProperty("revision").ValueKind);
        Assert.Equal(fixture.Sequence.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), header.GetProperty("revision").GetString());
        Assert.Equal(2, header.GetProperty("recipeCount").GetInt32());
        Assert.Equal(2, header.GetProperty("correctionAllowance").GetInt32());
        Assert.Equal(JsonValueKind.String, header.GetProperty("armedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, header.GetProperty("endedAtUtc").ValueKind);
        var steps = response.Body.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(fixture.Steps.Count, steps.Count);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var row = fixture.Steps[index];
            Assert.Equal(StepMembers, Names(step));
            Assert.Equal(row.Id, step.GetProperty("stepId").GetGuid());
            Assert.Equal(index, step.GetProperty("ordinal").GetInt32());
            Assert.Equal(row.Kind.ToString(), step.GetProperty("kind").GetString());
            Assert.Equal(row.PassNumber, step.GetProperty("passNumber").GetInt32());
            Assert.Equal(row.OperationId, step.GetProperty("operationId").GetGuid());
            Assert.Equal("Concluded", step.GetProperty("recordedState").GetString());
            Assert.Equal(row.SourceCheckpointId, step.GetProperty("sourceCheckpointId").GetGuid());
            if (row.Kind == LocalExecutionStepKind.Verification)
            {
                Assert.Equal($"Recipe {row.RecipeOrdinal + 1}", step.GetProperty("recipeName").GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, step.GetProperty("recipeName").ValueKind);
            }
        }

        AssertNoForbiddenText(response.Text);
    }

    private static void AssertNoForbiddenText(string text)
    {
        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Every_agent_step_returns_the_closed_step_and_only_the_immutable_attempt_header()
    {
        var attempts = (await fixture.Harness.AgentAttemptsAsync(RunId)).ToDictionary(attempt => attempt.Id);
        var agentSteps = fixture.Steps.Where(step => step.Kind != LocalExecutionStepKind.Verification).ToList();
        Assert.Equal(4, agentSteps.Count);

        foreach (var row in agentSteps)
        {
            var response = await GetAsync(Step(RunId, row.Id));

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(StepResponseMembers, Names(response.Body));
            Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
            Assert.Equal(fixture.Sequence.Id, response.Body.GetProperty("sequenceId").GetGuid());
            Assert.Equal(row.Id, response.Body.GetProperty("stepId").GetGuid());
            Assert.Equal(StepMembers, Names(response.Body.GetProperty("step")));
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("verification").ValueKind);
            var agent = response.Body.GetProperty("agent");
            Assert.Equal(AgentMembers, Names(agent));
            var attempt = attempts[row.OperationId!.Value];
            Assert.Equal(attempt.Id, agent.GetProperty("id").GetGuid());
            Assert.Equal(attempt.AttemptNumber, agent.GetProperty("attemptNumber").GetInt32());
            Assert.Equal(attempt.AgentRole.ToString(), agent.GetProperty("role").GetString());
            Assert.Equal(attempt.AgentProvider.ToString(), agent.GetProperty("provider").GetString());
            Assert.Equal(attempt.AgentResponseContract.ToString(), agent.GetProperty("responseContract").GetString());
            AssertNoForbiddenText(response.Text);
        }
    }

    [Fact]
    public async Task A_failed_verification_step_returns_its_proved_facts_and_two_recorded_stream_descriptors_without_any_path_or_hash()
    {
        var row = FailedVerification;
        var execution = (await fixture.Harness.ExecutionsAsync(fixture.Lineage.ProjectId)).Single(candidate => candidate.Id == row.OperationId);
        var outputs = await fixture.Harness.InDbAsync(db => db.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == execution.Id).ToListAsync());
        var before = fixture.Host.Store.Calls;

        var response = await GetAsync(Step(RunId, row.Id));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(StepResponseMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("agent").ValueKind);
        var verification = response.Body.GetProperty("verification");
        Assert.Equal(VerificationMembers, Names(verification));
        Assert.Equal(execution.Id, verification.GetProperty("executionId").GetGuid());
        Assert.Equal(execution.ExecutionNumber, verification.GetProperty("executionNumber").GetInt32());
        Assert.Equal("Recipe 1", verification.GetProperty("commandName").GetString());
        Assert.Equal("Failed", verification.GetProperty("status").GetString());
        Assert.Equal("Exited", verification.GetProperty("outcome").GetString());
        Assert.Equal(1, verification.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.String, verification.GetProperty("claimedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, verification.GetProperty("dispatchedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, verification.GetProperty("completedAtUtc").ValueKind);
        foreach (var (name, purpose) in new[] { ("standardOutput", VerificationOutputPurpose.StandardOutput), ("standardError", VerificationOutputPurpose.StandardError) })
        {
            var stream = verification.GetProperty(name);
            var output = outputs.Single(candidate => candidate.Purpose == purpose);
            Assert.Equal(StreamMembers, Names(stream));
            Assert.Equal("Recorded", stream.GetProperty("status").GetString());
            Assert.Equal(output.Id, stream.GetProperty("artifactId").GetGuid());
            Assert.Equal(output.ByteLength, stream.GetProperty("byteLength").GetInt64());
            Assert.Equal("CapturedWithKnownTruncation", stream.GetProperty("captureOutcome").GetString());
            Assert.Equal(JsonValueKind.False, stream.GetProperty("truncated").ValueKind);
        }

        AssertNoForbiddenText(response.Text);
        Assert.Equal(before, fixture.Host.Store.Calls);
    }

    [Fact]
    public async Task A_passed_verification_step_describes_both_of_its_real_recorded_streams_including_an_empty_one()
    {
        var outputs = await fixture.Harness.InDbAsync(db => db.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == PassedVerification.OperationId).ToListAsync());
        Assert.Equal(2, outputs.Count);

        var response = await GetAsync(Step(RunId, PassedVerification.Id));

        var verification = response.Body.GetProperty("verification");
        Assert.Equal("Passed", verification.GetProperty("status").GetString());
        Assert.Equal(0, verification.GetProperty("exitCode").GetInt32());
        foreach (var (name, purpose) in new[] { ("standardOutput", VerificationOutputPurpose.StandardOutput), ("standardError", VerificationOutputPurpose.StandardError) })
        {
            var stream = verification.GetProperty(name);
            var output = outputs.Single(candidate => candidate.Purpose == purpose);
            Assert.Equal(StreamMembers, Names(stream));
            Assert.Equal("Recorded", stream.GetProperty("status").GetString());
            Assert.Equal(output.Id, stream.GetProperty("artifactId").GetGuid());
            Assert.Equal(output.ByteLength, stream.GetProperty("byteLength").GetInt64());
        }
    }

    [Fact]
    public async Task The_failed_verifications_sealed_streams_are_served_through_the_real_store_reading_only_the_selected_stream()
    {
        var row = FailedVerification;
        var outputs = await fixture.Harness.InDbAsync(db => db.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == row.OperationId).ToListAsync());
        var stdoutPath = outputs.Single(output => output.Purpose == VerificationOutputPurpose.StandardOutput).RelativeStoragePath;
        var stderrPath = outputs.Single(output => output.Purpose == VerificationOutputPurpose.StandardError).RelativeStoragePath;
        var first = fixture.Host.Store.Calls;

        var stdout = await GetAsync(Window(RunId, row.Id, "stdout"));
        var stderr = await GetAsync(Window(RunId, row.Id, "stderr", "fromOffset=0&maxBytes=64"));

        Assert.Equal(HttpStatusCode.OK, stdout.Status);
        Assert.Equal(WindowMembers, Names(stdout.Body));
        Assert.Equal("Ok", stdout.Body.GetProperty("status").GetString());
        Assert.Equal(RunId, stdout.Body.GetProperty("runId").GetGuid());
        Assert.Equal(fixture.Sequence.Id, stdout.Body.GetProperty("sequenceId").GetGuid());
        Assert.Equal(row.Id, stdout.Body.GetProperty("stepId").GetGuid());
        Assert.Equal(row.OperationId, stdout.Body.GetProperty("executionId").GetGuid());
        Assert.Equal("stdout", stdout.Body.GetProperty("stream").GetString());
        Assert.Contains(LocalExecutionSequenceResponses.FailureLine, stdout.Body.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(stdout.Body.GetProperty("totalLength").GetInt64(), stdout.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(StreamMembers, Names(stdout.Body.GetProperty("artifact")));
        Assert.Equal("stderr", stderr.Body.GetProperty("stream").GetString());
        Assert.Contains("SENTINEL-STDERR build failed", stderr.Body.GetProperty("text").GetString(), StringComparison.Ordinal);
        foreach (var forbidden in new[] { "sentinel-tools", "build-tool", ".sealed", "sha256:", "verifications" })
        {
            Assert.DoesNotContain(forbidden, stdout.Text, StringComparison.OrdinalIgnoreCase);
        }

        lock (fixture.Host.Store.Paths)
        {
            Assert.Equal([stdoutPath, stderrPath], fixture.Host.Store.Paths.Skip(first));
        }
    }

    [Fact]
    public async Task A_passed_verifications_empty_stream_is_an_ok_window_with_empty_text_and_a_zero_length_after_one_sealed_read()
    {
        var empty = (await fixture.Harness.InDbAsync(db => db.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == PassedVerification.OperationId && output.ByteLength == 0).ToListAsync())).Single();
        var stream = empty.Purpose == VerificationOutputPurpose.StandardOutput ? "stdout" : "stderr";
        var before = fixture.Host.Store.Calls;

        var response = await GetAsync(Window(RunId, PassedVerification.Id, stream));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Ok", response.Body.GetProperty("status").GetString());
        Assert.Equal(PassedVerification.OperationId, response.Body.GetProperty("executionId").GetGuid());
        Assert.Equal(string.Empty, response.Body.GetProperty("text").GetString());
        Assert.Equal(0, response.Body.GetProperty("nextOffset").GetInt64());
        Assert.Equal(0, response.Body.GetProperty("totalLength").GetInt64());
        Assert.Equal(before + 1, fixture.Host.Store.Calls);
    }

    [Fact]
    public async Task A_run_without_a_sequence_is_not_recorded_with_a_null_header_and_an_empty_list_and_its_steps_are_not_found()
    {
        var run = await fixture.Harness.InDbAsync(async db =>
        {
            var project = Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
            db.Projects.Add(project);
            var other = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
            db.Runs.Add(other);
            await db.SaveChangesAsync();
            return other.Id;
        });

        var index = await GetAsync(Index(run));
        var step = await GetAsync(Step(run, Row(0).Id));
        var window = await GetAsync(Window(run, FailedVerification.Id, "stdout"));

        Assert.Equal(HttpStatusCode.OK, index.Status);
        Assert.Equal(IndexMembers, Names(index.Body));
        Assert.Equal("NotRecorded", index.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, index.Body.GetProperty("sequence").ValueKind);
        Assert.Equal(0, index.Body.GetProperty("steps").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, step.Status);
        Assert.Equal(HttpStatusCode.NotFound, window.Status);
    }

    [Fact]
    public async Task Reading_every_route_writes_nothing_and_runs_no_process()
    {
        var requests = fixture.Harness.Process.Requests.Count;
        string Snapshot() => string.Join("|", fixture.Harness.InDbAsync(async db => new[]
        {
            await db.Events.CountAsync(),
            await db.Attempts.CountAsync(),
            await db.VerificationExecutions.CountAsync(),
            await db.VerificationOutputArtifacts.CountAsync(),
            await db.CollaborationMessages.CountAsync(),
            await db.LocalExecutionSequenceSteps.CountAsync(),
        }).GetAwaiter().GetResult()) + "#" + fixture.Harness.InDbAsync(async db =>
            (await db.LocalExecutionSequences.AsNoTracking().SingleAsync()).Revision).GetAwaiter().GetResult();
        var before = Snapshot();
        var files = Directory.GetFiles(Path.GetTempPath(), "devalcopilot-local-sequence-artifacts-*", SearchOption.AllDirectories).Length;

        await GetAsync(Index(RunId));
        foreach (var row in fixture.Steps)
        {
            await GetAsync(Step(RunId, row.Id));
        }

        await GetAsync(Window(RunId, FailedVerification.Id, "stdout"));
        await GetAsync(Window(RunId, FailedVerification.Id, "stderr"));

        Assert.Equal(before, Snapshot());
        Assert.Equal(requests, fixture.Harness.Process.Requests.Count);
        Assert.Equal(files, Directory.GetFiles(Path.GetTempPath(), "devalcopilot-local-sequence-artifacts-*", SearchOption.AllDirectories).Length);
    }

    // ---- the actual OpenAPI contract -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_three_bodyless_routes_their_bounded_query_and_the_closed_responses()
    {
        var generator = fixture.Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var paths = document["paths"]!;
        var routes = new Dictionary<string, (string[] Parameters, string Response)>
        {
            ["/api/runs/{runId}/local-execution-sequence/inspection"] = (["runId:path"], "GetLocalExecutionSequenceInspectionResponse"),
            ["/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}"] =
                (["runId:path", "stepId:path"], "GetLocalExecutionStepInspectionResponse"),
            ["/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/artifact-window/{stream}"] =
                (["fromOffset:query", "maxBytes:query", "runId:path", "stepId:path", "stream:path"], "GetLocalExecutionVerificationOutputWindowResponse"),
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

        Assert.Equal(IndexMembers, Properties(document, "GetLocalExecutionSequenceInspectionResponse"));
        Assert.Equal(HeaderMembers, Properties(document, "LocalExecutionInspectionHeaderResponse"));
        Assert.Equal(StepMembers, Properties(document, "LocalExecutionInspectionStepResponse"));
        Assert.Equal(StepResponseMembers, Properties(document, "GetLocalExecutionStepInspectionResponse"));
        Assert.Equal(AgentMembers, Properties(document, "LocalExecutionInspectionAgentResponse"));
        Assert.Equal(VerificationMembers, Properties(document, "LocalExecutionInspectionVerificationResponse"));
        Assert.Equal(WindowMembers, Properties(document, "GetLocalExecutionVerificationOutputWindowResponse"));
        Assert.Contains("string", document["components"]!["schemas"]!["LocalExecutionInspectionHeaderResponse"]!["properties"]!["revision"]!.ToJsonString(), StringComparison.Ordinal);
    }

    private static string[] Properties(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        return properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }
}
