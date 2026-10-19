using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0054: the protected, bodyless, read-only reference inspection of every message that records one Agent attempt as its own attempt,
/// against the real host and file-backed SQLite. An unknown run and a missing, foreign or non-Agent attempt are one fixed 404; an
/// unprovable set is a 200 Unavailable without any part of it; Empty and Recorded are the closed graph with string sequences and times.
/// The detailed policy permutations belong to the Application tests; nothing is written, echoed or substituted.
/// </summary>
public sealed class AgentAttemptOutputsEndpointTests(ApiWebApplicationFactory factory)
    : AgentAttemptOutputsTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] RootMembers = ["attempt", "attemptId", "outputMessageTotalCount", "outputMessages", "runId", "status"];

    private static readonly string[] HeaderMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] ReferenceMembers = ["collaborationMessageId", "collaborationSequence", "occurredAtUtc", "position", "type"];

    private static string OutputsRoute(Guid runId, Guid attemptId) => $"/api/runs/{runId}/agent-attempts/{attemptId}/outputs";

    private Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetOutputsAsync(Guid runId, Guid attemptId) =>
        GetAsync(OutputsRoute(runId, attemptId));

    private static void AssertUnavailable((HttpStatusCode Status, JsonElement Body, string Text) response, Guid runId, Guid attemptId)
    {
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(runId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(attemptId, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("attempt").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("outputMessageTotalCount").ValueKind);
        Assert.Empty(response.Body.GetProperty("outputMessages").EnumerateArray());
        Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        await SeedOutputsAsync(attempt, 1);
        var route = OutputsRoute(runId, attempt.Id);

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        // The reference list takes no limit or cursor: a query string is ignored, never bound.
        var ignored = await GetOutputsAsync(runId, attempt.Id);
        using var withQuery = await client.GetAsync(route + "?limit=1&cursor=x");
        Assert.Equal(HttpStatusCode.OK, withQuery.StatusCode);
        Assert.Equal(ignored.Text, await withQuery.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Exactly_one_outputs_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.EndsWith("/outputs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/agent-attempts/{attemptId:guid}/outputs", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Unknown_missing_foreign_and_non_agent_targets_are_the_same_fixed_not_found_problem()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        await SeedOutputsAsync(attempt, 2);
        var otherRun = await SeedRunAsync();
        var foreign = await SeedResolverAsync(otherRun);
        var simulatedRun = await SeedRunAsync();
        var simulated = await WithDbAsync(async db =>
        {
            var claimed = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
            db.Attempts.Add(claimed);
            await db.SaveChangesAsync();
            return claimed;
        });
        var unknownId = Guid.NewGuid();

        var responses = new[]
        {
            await GetOutputsAsync(Guid.NewGuid(), attempt.Id),
            await GetOutputsAsync(runId, unknownId),
            await GetOutputsAsync(runId, foreign.Id),
            await GetOutputsAsync(simulatedRun, simulated.Id),
            await GetOutputsAsync(runId, Guid.Empty),
        };

        AssertFixedNotFound(responses, "agent_attempt_outputs.not_found", unknownId, attempt.Id, foreign.Id, simulated.Id, runId, otherRun);
    }

    [Fact]
    public async Task A_malformed_attempt_identifier_is_not_routed_and_is_not_echoed()
    {
        var runId = await SeedRunAsync();
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/not-a-guid-{Secret}/outputs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorded_carries_only_the_documented_members_with_string_sequences_utc_round_trip_times_and_every_reference()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId, number: 4);
        var messages = await SeedOutputsAsync(attempt, 12);

        var response = await GetOutputsAsync(runId, attempt.Id);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(runId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(attempt.Id, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(12, response.Body.GetProperty("outputMessageTotalCount").GetInt32());

        var header = response.Body.GetProperty("attempt");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(attempt.Id, header.GetProperty("id").GetGuid());
        Assert.Equal(4, header.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Resolver", header.GetProperty("role").GetString());
        Assert.Equal("Codex", header.GetProperty("provider").GetString());
        Assert.Equal("ChallengeResolution", header.GetProperty("responseContract").GetString());

        var references = response.Body.GetProperty("outputMessages").EnumerateArray().ToArray();
        Assert.Equal(12, references.Length);
        for (var position = 0; position < references.Length; position++)
        {
            var reference = references[position];
            var message = messages[position];
            var occurred = message.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", occurred);
            Assert.Equal(ReferenceMembers, Names(reference));
            Assert.Equal(JsonValueKind.Number, reference.GetProperty("position").ValueKind);
            Assert.Equal(position, reference.GetProperty("position").GetInt32());
            Assert.Equal(message.Id, reference.GetProperty("collaborationMessageId").GetGuid());
            Assert.Equal(message.Type.ToString(), reference.GetProperty("type").GetString());
            Assert.Equal(message.Sequence.ToString(CultureInfo.InvariantCulture), reference.GetProperty("collaborationSequence").GetString());
            Assert.Equal(occurred, reference.GetProperty("occurredAtUtc").GetString());
        }

        // References only: no text, reply, artifact or session reaches the wire.
        foreach (var forbidden in new[]
        {
            "summary", "structuredContent", "inReplyTo", "repair", "artifact", "session", "outcome", "model", "effort", "token", "path",
            "checkpoint", "fingerprint", "claimedAt", "completedAt", "scope", "Recorded ",
        })
        {
            Assert.DoesNotContain(forbidden, response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Empty_is_the_admitted_header_with_count_zero_for_a_dispatched_or_an_undispatched_attempt()
    {
        var runId = await SeedRunAsync();
        var planner = await SeedPlannerAsync(runId, dispatched: false);

        var response = await GetOutputsAsync(runId, planner.Id);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Empty", response.Body.GetProperty("status").GetString());
        Assert.Equal(0, response.Body.GetProperty("outputMessageTotalCount").GetInt32());
        Assert.Empty(response.Body.GetProperty("outputMessages").EnumerateArray());
        Assert.Equal(HeaderMembers, Names(response.Body.GetProperty("attempt")));
        Assert.Equal("Planner", response.Body.GetProperty("attempt").GetProperty("role").GetString());

        var dispatchedRun = await SeedRunAsync();
        var dispatched = await SeedResolverAsync(dispatchedRun);
        var second = await GetOutputsAsync(dispatchedRun, dispatched.Id);
        Assert.Equal("Empty", second.Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unprovable_oversized_and_corrupt_sets_are_unavailable_with_only_the_documented_members()
    {
        var unknownRun = await SeedRunAsync();
        var unknown = await SeedResolverAsync(unknownRun);
        await SeedOutputsAsync(unknown, 2);
        await ExecuteAsync($"UPDATE attempts SET AgentRole = {"Overlord" + Secret} WHERE Id = {unknown.Id}");

        var largeRun = await SeedRunAsync();
        var large = await SeedResolverAsync(largeRun);
        await SeedOutputsAsync(large, 65);

        var corruptRun = await SeedRunAsync();
        var corrupt = await SeedResolverAsync(corruptRun);
        var corruptMembers = await SeedOutputsAsync(corrupt, 64);
        await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {corruptMembers[63].Id}");

        var foreignRun = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreignAssociated = await SeedResolverAsync(foreignRun);
        var foreignMembers = await SeedOutputsAsync(foreignAssociated, 3);
        await ExecuteAsync($"UPDATE collaboration_messages SET RunId = {otherRun} WHERE Id = {foreignMembers[0].Id}");

        var undispatchedRun = await SeedRunAsync();
        var undispatched = await SeedResolverAsync(undispatchedRun);
        await SeedOutputsAsync(undispatched, 2);
        await ExecuteAsync($"UPDATE attempts SET AgentDispatchedAtUtc = NULL WHERE Id = {undispatched.Id}");

        AssertUnavailable(await GetOutputsAsync(unknownRun, unknown.Id), unknownRun, unknown.Id);
        AssertUnavailable(await GetOutputsAsync(largeRun, large.Id), largeRun, large.Id);
        AssertUnavailable(await GetOutputsAsync(corruptRun, corrupt.Id), corruptRun, corrupt.Id);
        AssertUnavailable(await GetOutputsAsync(foreignRun, foreignAssociated.Id), foreignRun, foreignAssociated.Id);
        AssertUnavailable(await GetOutputsAsync(undispatchedRun, undispatched.Id), undispatchedRun, undispatched.Id);
    }

    [Fact]
    public async Task Reading_outputs_writes_nothing()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        await SeedOutputsAsync(attempt, 2);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var attempts = await db.Attempts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.AttemptNumber + "|" + row.CompletedAtUtc).ToListAsync();
            var stored = await db.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.AttemptId + "|" + row.Summary + "|" + row.StructuredContentJson).ToListAsync();
            return string.Join(";", attempts) + "#" + string.Join(";", stored) + "#"
                + await db.Events.CountAsync() + "#" + await db.Runs.CountAsync() + "#" + await db.Artifacts.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetOutputsAsync(runId, attempt.Id);
        await GetOutputsAsync(runId, Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_and_the_closed_graph_with_string_sequences_and_times()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/agent-attempts/{attemptId}/outputs"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("attemptId", "path"), ("runId", "path")], parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetAgentAttemptOutputsResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        JsonObject PropertiesOf(string name)
        {
            var schema = document["components"]!["schemas"]![name]!;
            return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        }

        string[] Keys(JsonObject properties) => properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(RootMembers, Keys(PropertiesOf("GetAgentAttemptOutputsResponse")));
        Assert.Equal(HeaderMembers, Keys(PropertiesOf("AgentAttemptOutputsHeaderResponse")));
        var reference = PropertiesOf("AgentAttemptOutputReferenceResponse");
        Assert.Equal(ReferenceMembers, Keys(reference));
        foreach (var schema in new[] { reference["collaborationSequence"]!, reference["occurredAtUtc"]! })
        {
            var text = schema.ToJsonString();
            Assert.Contains("\"string\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("integer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("date-time", text, StringComparison.Ordinal);
        }

        Assert.Contains("integer", reference["position"]!.ToJsonString(), StringComparison.Ordinal);
    }
}
