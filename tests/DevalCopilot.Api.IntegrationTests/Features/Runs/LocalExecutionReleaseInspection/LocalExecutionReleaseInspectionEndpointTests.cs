using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// ADR-0067's one protected, bodyless recorded release read through the real host and the real production wiring: authentication and the
/// MVC authorization inventory, the strict body and query refusals with the fixed non-echoing inspection errors, the closed wire of every
/// status (only Recorded carries a receipt, with canonical strings and none of the private provenance), the absence of any write, and the
/// actual OpenAPI contract. The release itself is made through the existing production POST; no provider, process or coordinator runs.
/// </summary>
public sealed class LocalExecutionReleaseInspectionEndpointTests : IDisposable
{
    private static readonly string[] ReceiptMembers =
    [
        "releaseId", "releasedAtUtc", "eventSequence", "sequenceId", "sequenceState", "admissionMode", "stepKind", "ordinal", "passNumber",
        "operationFamily", "operationId", "sourceCheckpointId", "sourceFingerprintSha256", "operationStatus", "processOutcome",
        "semanticExitCode", "processDurationTicks", "witnessId", "observationId", "dispatchedAtUtc", "recordedAtUtc", "physicalExitCode",
        "observedElapsedTicks",
    ];

    // One host and database per test: a quarantined mutating owner of one scenario must never count against another's capacity.
    private readonly ApiWebApplicationFactory baseFactory = new();
    private readonly WebApplicationFactory<Program> factory;
    private readonly LocalExecutionSequenceMatchingEvidenceReader evidence = new();

    public LocalExecutionReleaseInspectionEndpointTests()
    {
        factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        }));
    }

    public void Dispose()
    {
        factory.Dispose();
        baseFactory.Dispose();
    }

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string Inspect(Guid runId, Guid stepId) => $"/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/ownership-release";

    private static string Candidate(Guid runId, Guid stepId) => $"/api/runs/{runId}/local-execution-sequence/steps/{stepId}/ownership-release";

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString()!;

    private async Task<(LocalExecutionSequenceApiScene Scene, LocalExecutionOwnershipReleaseApiSeed.Terminal Terminal)> TerminalAsync(
        bool witnessed = true, bool observed = true)
    {
        var scene = await WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db));
        using var client = Client();
        var availability = await client.GetAsync($"/api/runs/{scene.RunId}/local-execution-sequence/availability/{scene.PlanId}");
        var confirmation = (await BodyAsync(availability)).GetProperty("confirmationSha256").GetString()!;
        var arm = await client.PostAsync(
            $"/api/runs/{scene.RunId}/local-execution-sequence",
            Json(JsonSerializer.Serialize(new
            {
                planProposalMessageId = scene.PlanId,
                workspaceId = scene.WorkspaceId,
                checkpointId = scene.CheckpointId,
                fingerprintSha256 = LocalExecutionSequenceApiSeed.Fingerprint,
                confirmationSha256 = confirmation,
            })));
        Assert.True(arm.StatusCode == HttpStatusCode.OK, await arm.Content.ReadAsStringAsync());
        var terminal = await WithDbAsync(db => LocalExecutionOwnershipReleaseApiSeed.SeedTimedOutAsync(db, scene.RunId, witnessed, observed));
        return (scene, terminal);
    }

    /// <summary>Releases the terminal step through the existing production POST and returns the receipt it answered.</summary>
    private async Task<JsonElement> ReleaseAsync(HttpClient client, Guid runId, Guid stepId)
    {
        var candidate = (await BodyAsync(await client.GetAsync(Candidate(runId, stepId)))).GetProperty("candidate");
        var body = JsonSerializer.Serialize(new
        {
            observationId = candidate.GetProperty("observationId").GetString(),
            confirmationVersion = candidate.GetProperty("confirmationVersion").GetString(),
            confirmationSha256 = candidate.GetProperty("confirmationSha256").GetString(),
            confirmationExpiresAtUtc = candidate.GetProperty("confirmationExpiresAtUtc").GetString(),
        });
        var released = await client.PostAsync(Candidate(runId, stepId), Json(body));
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        return (await BodyAsync(released)).GetProperty("release");
    }

    private async Task<(int Releases, int Events, int Witnesses, int Observations)> RowCountsAsync() => (
        await WithDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()),
        await WithDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)),
        await WithDbAsync(db => db.LocalExecutionDispatchWitnesses.CountAsync()),
        await WithDbAsync(db => db.LocalExecutionTerminationObservations.CountAsync()));

    // ---- authentication, methods and the MVC authorization inventory -------------------------------------------------------------------

    [Fact]
    public async Task The_read_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var (scene, terminal) = await TerminalAsync();
        var route = Inspect(scene.RunId, terminal.StepId);
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        using var queryOnly = factory.CreateClient();
        using var client = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        Assert.Equal((0, 0, 1, 1), await RowCountsAsync());
    }

    [Fact]
    public void The_recorded_release_route_exists_once_as_an_authorized_get_and_adds_no_other_method()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("inspection/steps/{stepId:guid}/ownership-release", StringComparison.Ordinal) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal(
            "api/runs/{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}/ownership-release", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    // ---- the closed wire -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_recorded_release_is_the_closed_graph_with_canonical_strings_and_none_of_the_private_provenance()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var receipt = await ReleaseAsync(client, scene.RunId, terminal.StepId);
        var before = await RowCountsAsync();

        var response = await client.GetAsync(Inspect(scene.RunId, terminal.StepId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(["runId", "stepId", "status", "receipt"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(scene.RunId, body.GetProperty("runId").GetGuid());
        Assert.Equal(terminal.StepId, body.GetProperty("stepId").GetGuid());
        Assert.Equal("Recorded", body.GetProperty("status").GetString());
        var recorded = body.GetProperty("receipt");
        Assert.Equal(ReceiptMembers, recorded.EnumerateObject().Select(property => property.Name));
        Assert.Equal(receipt.GetProperty("releaseId").GetGuid(), recorded.GetProperty("releaseId").GetGuid());
        Assert.Equal(receipt.GetProperty("releasedAtUtc").GetString(), recorded.GetProperty("releasedAtUtc").GetString());
        Assert.Equal(receipt.GetProperty("eventSequence").GetString(), recorded.GetProperty("eventSequence").GetString());
        Assert.Equal(terminal.AttemptId, recorded.GetProperty("operationId").GetGuid());
        Assert.Equal("NeedsAttention", recorded.GetProperty("sequenceState").GetString());
        Assert.Equal("Immediate", recorded.GetProperty("admissionMode").GetString());
        Assert.Equal("Implementation", recorded.GetProperty("stepKind").GetString());
        Assert.Equal("Agent", recorded.GetProperty("operationFamily").GetString());
        Assert.Equal("Failed", recorded.GetProperty("operationStatus").GetString());
        Assert.Equal("TimedOut", recorded.GetProperty("processOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, recorded.GetProperty("semanticExitCode").ValueKind);
        Assert.Equal(137, recorded.GetProperty("physicalExitCode").GetInt32());
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks.ToString(CultureInfo.InvariantCulture), recorded.GetProperty("observedElapsedTicks").GetString());
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks.ToString(CultureInfo.InvariantCulture), recorded.GetProperty("processDurationTicks").GetString());
        Assert.Equal(JsonValueKind.Number, recorded.GetProperty("ordinal").ValueKind);
        Assert.Equal(JsonValueKind.Number, recorded.GetProperty("passNumber").ValueKind);
        Assert.Equal(JsonValueKind.String, recorded.GetProperty("eventSequence").ValueKind);
        Assert.Equal(JsonValueKind.String, recorded.GetProperty("processDurationTicks").ValueKind);
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(recorded.GetProperty("releasedAtUtc").GetString(), out _));
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(recorded.GetProperty("dispatchedAtUtc").GetString(), out _));
        Assert.True(LocalExecutionOwnershipReleaseConfirmation.TryParseTime(recorded.GetProperty("recordedAtUtc").GetString(), out _));
        Assert.True(long.Parse(recorded.GetProperty("eventSequence").GetString()!, CultureInfo.InvariantCulture) >= 1);
        Assert.Equal(before, await RowCountsAsync());

        var raw = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[]
                 {
                     "nonce", "hostLaunch", "ticket", "processId", "path", "arguments", "environment", "stdout", "stderr", "confirmation",
                     "candidate", "expires",
                 })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        var witness = await WithDbAsync(db => db.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync());
        Assert.Equal(witness.Id, recorded.GetProperty("witnessId").GetGuid());
        Assert.DoesNotContain(witness.Nonce.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(witness.HostLaunchId.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_history_with_no_receipt_is_not_recorded_with_no_receipt_and_the_read_mints_no_candidate_or_write()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var before = await RowCountsAsync();

        var body = await BodyAsync(await client.GetAsync(Inspect(scene.RunId, terminal.StepId)));

        Assert.Equal(["runId", "stepId", "status", "receipt"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("NotRecorded", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("receipt").ValueKind);
        Assert.Equal(before, await RowCountsAsync());
        // The recorded read is not the candidate read: the candidate still shows the exact release the Human has not made.
        Assert.Equal("Available", (await BodyAsync(await client.GetAsync(Candidate(scene.RunId, terminal.StepId)))).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Missing_producer_evidence_is_the_closed_unavailable_graph_never_absence(bool witnessed, bool observed)
    {
        var (scene, terminal) = await TerminalAsync(witnessed, observed);
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Inspect(scene.RunId, terminal.StepId)));

        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("receipt").ValueKind);
    }

    // ---- damaged storage through the real host --------------------------------------------------------------------------------------------

    private async Task<(int? Owners, string Status)> CapacityAsync(HttpClient client)
    {
        var capacity = await BodyAsync(await client.GetAsync("/api/execution-capacity"));
        var owners = capacity.GetProperty("recordedOwners");
        return (owners.ValueKind == JsonValueKind.Number ? owners.GetInt32() : null, capacity.GetProperty("status").GetString()!);
    }

    [Theory]
    [InlineData("checksum-only", "UPDATE local_execution_ownership_releases SET \"ConfirmationSha256\" = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'", true)]
    [InlineData("snapshot-drift", "UPDATE local_execution_ownership_releases SET \"IntentProcessDuration\" = \"IntentProcessDuration\" + 1", false)]
    public async Task A_receipt_that_fails_its_proof_reads_unavailable_with_no_receipt_and_changes_no_ownership(string name, string sql, bool ownershipStaysReleased)
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        await ReleaseAsync(client, scene.RunId, terminal.StepId);
        await WithDbAsync(async db =>
        {
            await LocalExecutionOwnershipReleaseApiSeed.TamperAsync(db, "local_execution_ownership_releases", sql);
            return 0;
        });
        var capacityBefore = await CapacityAsync(client);
        var before = await RowCountsAsync();

        var body = await BodyAsync(await client.GetAsync(Inspect(scene.RunId, terminal.StepId)));

        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("receipt").ValueKind);
        Assert.Equal(before, await RowCountsAsync());
        Assert.Equal(capacityBefore, await CapacityAsync(client));
        // A null count is the capacity policy's fail-closed Unavailable reading: only an Available zero would admit past the owner.
        Assert.True(ownershipStaysReleased ? capacityBefore.Owners == 0 : capacityBefore.Owners is null or >= 1);
        Assert.True(name.Length > 0);
    }

    // ---- errors ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_or_step_is_one_fixed_inspection_not_found_that_repeats_no_value()
    {
        var (scene, terminal) = await TerminalAsync();
        var other = await TerminalAsync();
        using var client = Client();
        var foreign = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync(Inspect(foreign, terminal.StepId)),
            await client.GetAsync(Inspect(scene.RunId, foreign)),
            await client.GetAsync(Inspect(scene.RunId, other.Terminal.StepId)),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        foreach (var response in responses)
        {
            var problem = await BodyAsync(response);
            var error = problem.GetProperty("errors")[0];
            Assert.Equal(LocalExecutionInspectionErrors.NotFoundCode, error.GetProperty("code").GetString());
            var errorText = error.ToString() + problem.GetProperty("title").GetString() + problem.GetProperty("detail").GetString();
            Assert.DoesNotContain(foreign.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(terminal.StepId.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scene.RunId.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("/api/runs/not-a-guid/local-execution-sequence/inspection/steps/00000000-0000-0000-0000-000000000001/ownership-release")]
    [InlineData("/api/runs/00000000-0000-0000-0000-000000000001/local-execution-sequence/inspection/steps/not-a-guid/ownership-release")]
    public async Task A_malformed_identity_is_a_plain_unmatched_route(string route)
    {
        using var client = Client();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task The_read_is_bodyless_and_queryless_with_the_existing_fixed_refusals_and_no_write()
    {
        var (scene, terminal) = await TerminalAsync();
        using var client = Client();
        var route = Inspect(scene.RunId, terminal.StepId);
        var before = await RowCountsAsync();

        var query = await client.GetAsync($"{route}?refresh=true");
        var body = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, route) { Content = Json("{}") });
        var emptyChunked = new HttpRequestMessage(HttpMethod.Get, route) { Content = new StringContent(string.Empty) };
        emptyChunked.Headers.TransferEncodingChunked = true;
        var chunked = await client.SendAsync(emptyChunked);

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(LocalExecutionInspectionErrors.QueryNotAllowedCode, await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal(LocalExecutionInspectionErrors.BodyNotAllowedCode, await ErrorCodeAsync(body));
        Assert.Equal(HttpStatusCode.BadRequest, chunked.StatusCode);
        Assert.Equal(LocalExecutionInspectionErrors.BodyNotAllowedCode, await ErrorCodeAsync(chunked));
        Assert.DoesNotContain("refresh", await query.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await RowCountsAsync());
    }

    // ---- the actual OpenAPI contract ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_actual_openapi_document_declares_the_one_bodyless_route_and_the_closed_responses()
    {
        var generator = factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;
        var path = document["paths"]!["/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/ownership-release"]!.AsObject();

        Assert.Equal(["get"], path.Select(item => item.Key));
        var operation = path["get"]!;
        Assert.Null(operation["requestBody"]);
        Assert.Equal(
            ["runId:path", "stepId:path"],
            operation["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>() + ":" + p["in"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetLocalExecutionReleaseInspectionResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(
            ["receipt", "runId", "status", "stepId"], Properties(document, "GetLocalExecutionReleaseInspectionResponse"));
        Assert.Equal(
            ReceiptMembers.Order(StringComparer.Ordinal), Properties(document, "LocalExecutionReleaseInspectionReceiptResponse"));
    }

    private static string[] Properties(JsonNode document, string name)
    {
        var schema = document["components"]!["schemas"]![name]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        return properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
    }
}
