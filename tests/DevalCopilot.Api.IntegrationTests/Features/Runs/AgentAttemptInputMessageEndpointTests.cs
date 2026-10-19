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
/// ADR-0053: the protected, bodyless, read-only content inspection of one input message of one Agent attempt, against the real host and
/// file-backed SQLite. An unknown run, a missing, foreign or non-Agent attempt and a nonmember of a proved set are one fixed 404; an
/// unprovable set is a 200 Unavailable without any part of it; a Recorded answer is the closed graph with string sequences and times, and
/// unavailable content keeps its reference. Nothing is written, echoed or substituted.
/// </summary>
public sealed class AgentAttemptInputMessageEndpointTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] RootMembers = ["attempt", "attemptId", "collaborationMessageId", "input", "message", "runId", "status"];

    private static readonly string[] HeaderMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] InputMembers = ["collaborationMessageId", "collaborationSequence", "occurredAtUtc", "sequence", "type"];

    private static readonly string[] MessageMembers =
    [
        "actor", "contentStatus", "fields", "id", "occurredAtUtc", "protocolVersion", "provenance", "recipient", "runId", "sequence",
        "summary", "type",
    ];

    private static readonly string[] ParticipantMembers = ["kind", "provider", "role"];

    private static readonly string[] FieldMembers = ["name", "value"];

    private static string MessageRoute(Guid runId, Guid attemptId, Guid messageId) =>
        $"/api/runs/{runId}/agent-attempts/{attemptId}/inputs/{messageId}";

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetMessageAsync(Guid runId, Guid attemptId, Guid messageId)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(MessageRoute(runId, attemptId, messageId));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private Task<Attempt> SeedPlannerAsync(Guid runId, int number = 1) => WithDbAsync(async db =>
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, number);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    });

    private Task<Attempt> SeedResolverAsync(Guid runId, int number = 1) => WithDbAsync(async db =>
    {
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, number);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    });

    private async Task<List<CollaborationMessage>> SeedInputsAsync(
        Guid runId, Guid attemptId, int count, CollaborationMessageType type = CollaborationMessageType.Proposal)
    {
        var messages = await AppendAsync(runId, count, type);
        await WithDbAsync(async db =>
        {
            for (var index = 0; index < count; index++)
            {
                db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, messages[index].Id, index));
            }

            return await db.SaveChangesAsync();
        });
        return messages;
    }

    private static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    private static void AssertUnavailable((HttpStatusCode Status, JsonElement Body, string Text) response, Guid runId, Guid attemptId, Guid messageId)
    {
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(runId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(attemptId, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(messageId, response.Body.GetProperty("collaborationMessageId").GetGuid());
        foreach (var member in new[] { "attempt", "input", "message" })
        {
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty(member).ValueKind);
        }

        Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Recorded scope", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 1);
        var route = MessageRoute(runId, attempt.Id, messages[0].Id);

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
    }

    [Fact]
    public void Exactly_one_input_message_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("/inputs/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/agent-attempts/{attemptId:guid}/inputs/{messageId:guid}", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Unknown_missing_foreign_non_agent_and_nonmember_targets_are_the_same_fixed_not_found_problem()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var members = await SeedInputsAsync(runId, attempt.Id, 2);
        var loose = (await AppendAsync(runId, 1))[0];
        var otherRun = await SeedRunAsync();
        var foreign = await SeedResolverAsync(otherRun);
        var foreignMembers = await SeedInputsAsync(otherRun, foreign.Id, 1);
        var simulatedRun = await SeedRunAsync();
        var simulated = await WithDbAsync(async db =>
        {
            var claimed = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
            db.Attempts.Add(claimed);
            await db.SaveChangesAsync();
            return claimed;
        });
        var plannerRun = await SeedRunAsync();
        var planner = await SeedPlannerAsync(plannerRun);
        var plannerRunMessage = (await AppendAsync(plannerRun, 1))[0];
        var unknownId = Guid.NewGuid();

        var responses = new[]
        {
            await GetMessageAsync(Guid.NewGuid(), attempt.Id, members[0].Id),
            await GetMessageAsync(runId, unknownId, members[0].Id),
            await GetMessageAsync(runId, foreign.Id, foreignMembers[0].Id),
            await GetMessageAsync(simulatedRun, simulated.Id, members[0].Id),
            await GetMessageAsync(runId, attempt.Id, loose.Id),
            await GetMessageAsync(runId, attempt.Id, foreignMembers[0].Id),
            await GetMessageAsync(runId, attempt.Id, unknownId),
            await GetMessageAsync(runId, attempt.Id, Guid.Empty),
            await GetMessageAsync(plannerRun, planner.Id, plannerRunMessage.Id),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("agent_attempt_input_message.not_found", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknownId, attempt.Id, foreign.Id, simulated.Id, planner.Id, loose.Id, members[0].Id, foreignMembers[0].Id, plannerRunMessage.Id, runId })
            {
                Assert.DoesNotContain(value.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Fact]
    public async Task A_malformed_message_identifier_is_not_routed_and_is_not_echoed()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attempt.Id}/inputs/not-a-guid-{Secret}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorded_carries_only_the_documented_members_with_string_sequences_utc_round_trip_times_and_the_admitted_content()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId, number: 4);
        var messages = await SeedInputsAsync(runId, attempt.Id, 12);
        var selected = messages[11];

        var response = await GetMessageAsync(runId, attempt.Id, selected.Id);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(runId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(attempt.Id, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(selected.Id, response.Body.GetProperty("collaborationMessageId").GetGuid());

        var header = response.Body.GetProperty("attempt");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(attempt.Id, header.GetProperty("id").GetGuid());
        Assert.Equal(4, header.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Resolver", header.GetProperty("role").GetString());
        Assert.Equal("Codex", header.GetProperty("provider").GetString());
        Assert.Equal("ChallengeResolution", header.GetProperty("responseContract").GetString());

        var sequence = selected.Sequence.ToString(CultureInfo.InvariantCulture);
        var occurred = selected.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", occurred);
        var input = response.Body.GetProperty("input");
        Assert.Equal(InputMembers, Names(input));
        Assert.Equal(JsonValueKind.Number, input.GetProperty("sequence").ValueKind);
        Assert.Equal(11, input.GetProperty("sequence").GetInt32());
        Assert.Equal(selected.Id, input.GetProperty("collaborationMessageId").GetGuid());
        Assert.Equal("Proposal", input.GetProperty("type").GetString());
        Assert.Equal(sequence, input.GetProperty("collaborationSequence").GetString());
        Assert.Equal(occurred, input.GetProperty("occurredAtUtc").GetString());

        var message = response.Body.GetProperty("message");
        Assert.Equal(MessageMembers, Names(message));
        Assert.Equal(runId, message.GetProperty("runId").GetGuid());
        Assert.Equal(selected.Id, message.GetProperty("id").GetGuid());
        Assert.Equal(sequence, message.GetProperty("sequence").GetString());
        Assert.Equal(occurred, message.GetProperty("occurredAtUtc").GetString());
        Assert.Equal("1.0", message.GetProperty("protocolVersion").GetString());
        Assert.Equal("Proposal", message.GetProperty("type").GetString());
        Assert.Equal("ProviderObserved", message.GetProperty("provenance").GetString());
        Assert.Equal(ParticipantMembers, Names(message.GetProperty("actor")));
        Assert.Equal(ParticipantMembers, Names(message.GetProperty("recipient")));
        Assert.Equal(("Agent", "Planner", "Codex"), Participant(message.GetProperty("actor")));
        Assert.Equal(("Orchestrator", (string?)null, (string?)null), Participant(message.GetProperty("recipient")));
        Assert.Equal("Available", message.GetProperty("contentStatus").GetString());
        Assert.Equal("Recorded Proposal summary 11.", message.GetProperty("summary").GetString());
        var fields = message.GetProperty("fields").EnumerateArray().ToArray();
        Assert.All(fields, field => Assert.Equal(FieldMembers, Names(field)));
        Assert.Equal(
            FieldNames[CollaborationMessageType.Proposal].Select(name => (name, Value(name))),
            fields.Select(field => (field.GetProperty("name").GetString()!, field.GetProperty("value").GetString()!)));

        foreach (var forbidden in new[]
        {
            "summary 10.", "structuredContent", "inReplyTo", "repair", "artifact", "session", "outcome", "model", "effort", "token", "path",
            "checkpoint", "fingerprint", "claimedAt", "completedAt", "producing",
        })
        {
            Assert.DoesNotContain(forbidden, response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static (string? Kind, string? Role, string? Provider) Participant(JsonElement participant) => (
        participant.GetProperty("kind").GetString(), participant.GetProperty("role").GetString(), participant.GetProperty("provider").GetString());

    [Fact]
    public async Task A_human_instruction_input_is_disclosed_with_its_human_sender_and_fields()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 1, CollaborationMessageType.HumanInstruction);

        var response = await GetMessageAsync(runId, attempt.Id, messages[0].Id);

        var message = response.Body.GetProperty("message");
        Assert.Equal("HumanInstruction", message.GetProperty("type").GetString());
        Assert.Equal("HumanSubmitted", message.GetProperty("provenance").GetString());
        Assert.Equal(("Human", (string?)null, (string?)null), Participant(message.GetProperty("actor")));
        Assert.Equal(["instruction", "rationale"], message.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task Unprovable_oversized_and_corrupt_sets_are_unavailable_with_only_the_documented_members()
    {
        var unknownRun = await SeedRunAsync();
        var unknown = await SeedResolverAsync(unknownRun);
        var unknownMembers = await SeedInputsAsync(unknownRun, unknown.Id, 2);
        await ExecuteAsync($"UPDATE attempts SET AgentRole = {"Overlord" + Secret} WHERE Id = {unknown.Id}");

        var largeRun = await SeedRunAsync();
        var large = await SeedResolverAsync(largeRun);
        var largeMembers = await SeedInputsAsync(largeRun, large.Id, 65);

        var corruptRun = await SeedRunAsync();
        var corrupt = await SeedResolverAsync(corruptRun);
        var corruptMembers = await SeedInputsAsync(corruptRun, corrupt.Id, 64);
        await ExecuteAsync($"UPDATE collaboration_messages SET Type = {"Overlord" + Secret} WHERE Id = {corruptMembers[63].Id}");

        AssertUnavailable(await GetMessageAsync(unknownRun, unknown.Id, unknownMembers[0].Id), unknownRun, unknown.Id, unknownMembers[0].Id);
        AssertUnavailable(await GetMessageAsync(largeRun, large.Id, largeMembers[0].Id), largeRun, large.Id, largeMembers[0].Id);
        AssertUnavailable(await GetMessageAsync(corruptRun, corrupt.Id, corruptMembers[0].Id), corruptRun, corrupt.Id, corruptMembers[0].Id);
        // An unprovable set answers Unavailable even for an identity that was never a member: membership cannot be decided.
        var stranger = Guid.NewGuid();
        AssertUnavailable(await GetMessageAsync(largeRun, large.Id, stranger), largeRun, large.Id, stranger);
    }

    [Fact]
    public async Task Unavailable_content_keeps_the_reference_with_a_null_summary_and_no_fields()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {"The password is hunter2 " + Secret} WHERE Id = {messages[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Provenance = {"Forged" + Secret} WHERE Id = {messages[1].Id}");

        foreach (var (message, provenance) in new[] { (messages[0], "ProviderObserved"), (messages[1], "Unrecognized") })
        {
            var response = await GetMessageAsync(runId, attempt.Id, message.Id);

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
            Assert.Equal(message.Id, response.Body.GetProperty("input").GetProperty("collaborationMessageId").GetGuid());
            var body = response.Body.GetProperty("message");
            Assert.Equal(MessageMembers, Names(body));
            Assert.Equal(provenance, body.GetProperty("provenance").GetString());
            Assert.Equal("Unavailable", body.GetProperty("contentStatus").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("summary").ValueKind);
            Assert.Empty(body.GetProperty("fields").EnumerateArray());
            Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Recorded scope", response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Reading_an_input_message_writes_nothing()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 2);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var attempts = await db.Attempts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.AttemptNumber + "|" + row.CompletedAtUtc).ToListAsync();
            var stored = await db.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.AttemptId + "|" + row.Summary + "|" + row.StructuredContentJson).ToListAsync();
            var inputs = await db.AttemptInputMessages.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.AttemptId + "|" + row.CollaborationMessageId + "|" + row.Sequence).ToListAsync();
            return string.Join(";", attempts) + "#" + string.Join(";", stored) + "#" + string.Join(";", inputs) + "#"
                + await db.Events.CountAsync() + "#" + await db.Runs.CountAsync() + "#" + await db.Artifacts.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetMessageAsync(runId, attempt.Id, messages[1].Id);
        await GetMessageAsync(runId, attempt.Id, Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_and_the_closed_graph_with_string_sequences_and_times()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/agent-attempts/{attemptId}/inputs/{messageId}"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("attemptId", "path"), ("messageId", "path"), ("runId", "path")], parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetAgentAttemptInputMessageResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        JsonObject PropertiesOf(string name)
        {
            var schema = document["components"]!["schemas"]![name]!;
            return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        }

        string[] Keys(JsonObject properties) => properties.Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(RootMembers, Keys(PropertiesOf("GetAgentAttemptInputMessageResponse")));
        Assert.Equal(HeaderMembers, Keys(PropertiesOf("AgentAttemptInputMessageAttemptResponse")));
        Assert.Equal(ParticipantMembers, Keys(PropertiesOf("AgentAttemptInputMessageParticipantResponse")));
        Assert.Equal(FieldMembers, Keys(PropertiesOf("AgentAttemptInputMessageFieldResponse")));
        var input = PropertiesOf("AgentAttemptInputMessageInputResponse");
        var message = PropertiesOf("AgentAttemptInputCollaborationMessageResponse");
        Assert.Equal(InputMembers, Keys(input));
        Assert.Equal(MessageMembers, Keys(message));
        foreach (var schema in new[] { input["collaborationSequence"]!, input["occurredAtUtc"]!, message["sequence"]!, message["occurredAtUtc"]! })
        {
            var text = schema.ToJsonString();
            Assert.Contains("\"string\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("integer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("date-time", text, StringComparison.Ordinal);
        }

        Assert.Contains("integer", input["sequence"]!.ToJsonString(), StringComparison.Ordinal);
    }
}
