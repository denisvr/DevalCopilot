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
/// ADR-0036: the protected, bodyless, read-only inspection of one message's recorded reply parent against the real host and
/// file-backed SQLite. Unknown run and missing or foreign source are one fixed 404, an unusable stored source sequence is one fixed
/// 409, the parent keeps the ADR-0035 disclosure, and nothing is written or echoed.
/// </summary>
public sealed class CollaborationReplyEndpointTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static string ReplyRoute(Guid runId, Guid messageId) => $"{Route(runId)}/{messageId}/reply";

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetReplyAsync(Guid runId, Guid messageId)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(ReplyRoute(runId, messageId));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private Task<CollaborationMessage> RecordAsync(
        Guid runId, CollaborationMessageType type, Guid? replyTo, int index = 0) => WithDbAsync(async db =>
    {
        var human = type == CollaborationMessageType.HumanInstruction;
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
            human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForOrchestrator(), type, replyTo, $"Recorded {type} summary {index}.", ContentJson(type),
            human ? CollaborationMessageProvenance.HumanSubmitted : CollaborationMessageProvenance.ProviderObserved,
            new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
        db.CollaborationMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    });

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null);

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ReplyRoute(runId, root.Id))).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(ReplyRoute(runId, root.Id))).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(ReplyRoute(runId, root.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(ReplyRoute(runId, root.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(ReplyRoute(runId, root.Id))).StatusCode);
    }

    [Fact]
    public void Exactly_one_reply_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("/reply", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/collaboration-history/{messageId:guid}/reply", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    private static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_or_foreign_source_are_the_same_fixed_not_found_problem()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await RecordAsync(otherRun, CollaborationMessageType.Proposal, null);
        var unknownId = Guid.NewGuid();

        var responses = new[]
        {
            await GetReplyAsync(Guid.NewGuid(), foreign.Id),
            await GetReplyAsync(runId, unknownId),
            await GetReplyAsync(runId, foreign.Id),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("collaboration_reply.not_found", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            // The shared contract's `instance` is the caller's own request path; nothing else may carry an identity.
            var body = Normalize(response.Text);
            Assert.DoesNotContain(unknownId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(foreign.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        // Only the instance and trace differ: nothing distinguishes the three refusals.
        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Fact]
    public async Task A_malformed_message_identifier_is_not_routed_and_is_not_echoed()
    {
        var runId = await SeedRunAsync();
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-history/not-a-guid-{Secret}/reply");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_stored_source_sequence_is_the_shared_conflict_problem_with_one_fixed_code()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {source.Id}");

        var response = await GetReplyAsync(runId, source.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("urn:devalente:problem:conflict", response.Body.GetProperty("type").GetString());
        Assert.Equal(409, response.Body.GetProperty("status").GetInt32());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal("collaboration_reply.unavailable", error.GetProperty("code").GetString());
        Assert.DoesNotContain(parent.Id.ToString(), response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task None_unavailable_and_available_carry_only_the_documented_members()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var accepted = await RecordAsync(runId, CollaborationMessageType.Acceptance, root.Id, 1);
        var broken = await RecordAsync(runId, CollaborationMessageType.Challenge, Guid.NewGuid(), 2);

        var none = await GetReplyAsync(runId, root.Id);
        var available = await GetReplyAsync(runId, accepted.Id);
        var unavailable = await GetReplyAsync(runId, broken.Id);

        Assert.Equal(HttpStatusCode.OK, none.Status);
        Assert.Equal(
            ["messageId", "parent", "runId", "sourceSequence", "status"],
            none.Body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("None", none.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, none.Body.GetProperty("parent").ValueKind);
        Assert.Equal(root.Sequence.ToString(), none.Body.GetProperty("sourceSequence").GetString());
        Assert.Equal(root.Id, none.Body.GetProperty("messageId").GetGuid());
        Assert.Equal(runId, none.Body.GetProperty("runId").GetGuid());

        Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("parent").ValueKind);
        Assert.DoesNotContain("inReplyTo", unavailable.Text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Available", available.Body.GetProperty("status").GetString());
        var parent = available.Body.GetProperty("parent");
        Assert.Equal(root.Id, parent.GetProperty("id").GetGuid());
        Assert.Equal(root.Sequence.ToString(), parent.GetProperty("sequence").GetString());
        Assert.Equal(
            ["actor", "contentStatus", "fields", "id", "occurredAtUtc", "protocolVersion", "provenance", "recipient", "runId", "sequence",
                "summary", "type"],
            parent.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var forbidden in new[] { "attempt", "inReplyTo", "structuredContent", "artifact" })
        {
            Assert.DoesNotContain(forbidden, available.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_parent_beyond_every_loaded_page_is_disclosed_exactly_as_its_history_entry()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        await AppendAsync(runId, 24);
        var source = await RecordAsync(runId, CollaborationMessageType.Challenge, root.Id, 30);

        var reply = await GetReplyAsync(runId, source.Id);

        Assert.Equal("Available", reply.Body.GetProperty("status").GetString());
        var page = await ReadAsync(runId, "limit=20");
        Assert.DoesNotContain(root.Id, page.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
        var older = await ReadAsync(runId, $"limit=20&beforeSequence={page.Body.GetProperty("nextBeforeSequence").GetString()}");
        var fromHistory = older.Body.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == root.Id);
        Assert.Equal(fromHistory.GetRawText(), reply.Body.GetProperty("parent").GetRawText());
    }

    [Fact]
    public async Task A_parent_with_unsafe_content_is_an_available_relationship_with_a_header_only_entry()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {"The password is hunter2 " + Secret} WHERE Id = {parent.Id}");

        var reply = await GetReplyAsync(runId, source.Id);

        Assert.Equal("Available", reply.Body.GetProperty("status").GetString());
        var entry = reply.Body.GetProperty("parent");
        Assert.Equal("Unavailable", entry.GetProperty("contentStatus").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("summary").ValueKind);
        Assert.Empty(entry.GetProperty("fields").EnumerateArray());
        Assert.Equal("Proposal", entry.GetProperty("type").GetString());
        Assert.DoesNotContain(Secret, reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_stored_metadata_is_disclosed_only_as_unrecognized_and_never_as_stored_text()
    {
        var runId = await SeedRunAsync();
        var parent = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, parent.Id, 1);
        await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = {"Forged" + Secret} WHERE Id = {parent.Id}");

        var reply = await GetReplyAsync(runId, source.Id);

        Assert.Equal("Available", reply.Body.GetProperty("status").GetString());
        Assert.Equal("Unrecognized", reply.Body.GetProperty("parent").GetProperty("provenance").GetString());
        Assert.DoesNotContain(Secret, reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_reply_writes_nothing()
    {
        var runId = await SeedRunAsync();
        var root = await RecordAsync(runId, CollaborationMessageType.Proposal, null, 0);
        var source = await RecordAsync(runId, CollaborationMessageType.Acceptance, root.Id, 1);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var messages = await db.CollaborationMessages.AsNoTracking().OrderBy(message => message.Sequence)
                .Select(message => message.Id + "|" + message.Sequence + "|" + message.InReplyToMessageId + "|" + message.Summary)
                .ToListAsync();
            return string.Join(";", messages) + "#" + await db.Events.CountAsync() + "#" + await db.Runs.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetReplyAsync(runId, source.Id);
        await GetReplyAsync(runId, root.Id);
        await GetReplyAsync(runId, Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_the_closed_response_and_the_shared_404_and_409_schemas()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/collaboration-history/{messageId}/reply"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("messageId", "path"), ("runId", "path")], parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "404", "409"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetCollaborationHistoryReplyResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var status in new[] { "404", "409" })
        {
            Assert.Contains("ApiProblemDetails", responses[status]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        }

        var schema = document["components"]!["schemas"]!["GetCollaborationHistoryReplyResponse"]!;
        var properties = (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        Assert.Equal(
            ["messageId", "parent", "runId", "sourceSequence", "status"], properties.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("string", properties["sourceSequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("integer", properties["sourceSequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("CollaborationHistoryEntryResponse", properties["parent"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("json", responses["404"]!["content"]!.AsObject().Single().Key, StringComparison.Ordinal);
    }
}
