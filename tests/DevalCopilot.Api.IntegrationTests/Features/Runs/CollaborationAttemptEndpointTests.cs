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
/// ADR-0037: the protected, bodyless, read-only attribution of one message to the Agent attempt it references, against the real host and
/// file-backed SQLite. Unknown run and missing or foreign source are one fixed 404, an unusable stored source sequence is one fixed 409,
/// the answer is the closed status with a minimal attempt header, and nothing is written, echoed or substituted.
/// </summary>
public sealed class CollaborationAttemptEndpointTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static string AttemptRoute(Guid runId, Guid messageId) => $"{Route(runId)}/{messageId}/attempt";

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAttemptAsync(Guid runId, Guid messageId)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(AttemptRoute(runId, messageId));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private Task<Attempt> SeedAttemptAsync(Guid runId, int number = 1) => WithDbAsync(async db =>
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, number);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    });

    private Task<CollaborationMessage> SeedMessageAsync(
        Guid runId, Guid? attemptId, CollaborationMessageProvenance provenance = CollaborationMessageProvenance.ProviderObserved, int index = 0) =>
        WithDbAsync(async db =>
        {
            var human = provenance == CollaborationMessageProvenance.HumanSubmitted;
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), runId, attemptId, CollaborationMessage.ProtocolVersionOne,
                human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                human ? ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex) : ParticipantIdentity.ForOrchestrator(),
                CollaborationMessageType.Proposal, null, $"Recorded Proposal summary {index}.", ContentJson(CollaborationMessageType.Proposal),
                provenance, Now.AddSeconds(index));
            db.CollaborationMessages.Add(message);
            await db.SaveChangesAsync();
            return message;
        });

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await SeedMessageAsync(runId, attempt.Id);

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AttemptRoute(runId, message.Id))).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(AttemptRoute(runId, message.Id))).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(AttemptRoute(runId, message.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(AttemptRoute(runId, message.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(AttemptRoute(runId, message.Id))).StatusCode);
    }

    [Fact]
    public void Exactly_one_attempt_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.EndsWith("/attempt", StringComparison.OrdinalIgnoreCase) == true
                && endpoint.RoutePattern.RawText.Contains("collaboration-history", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/collaboration-history/{messageId:guid}/attempt", endpoint.RoutePattern.RawText);
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
        var foreign = await SeedMessageAsync(otherRun, null, CollaborationMessageProvenance.HumanSubmitted);
        var unknownId = Guid.NewGuid();

        var responses = new[]
        {
            await GetAttemptAsync(Guid.NewGuid(), foreign.Id),
            await GetAttemptAsync(runId, unknownId),
            await GetAttemptAsync(runId, foreign.Id),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("collaboration_attempt.not_found", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var body = Normalize(response.Text);
            Assert.DoesNotContain(unknownId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(foreign.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Fact]
    public async Task A_malformed_message_identifier_is_not_routed_and_is_not_echoed()
    {
        var runId = await SeedRunAsync();
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-history/not-a-guid-{Secret}/attempt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unusable_stored_source_sequence_is_the_shared_conflict_problem_with_one_fixed_code()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await SeedMessageAsync(runId, attempt.Id);
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {message.Id}");

        var response = await GetAttemptAsync(runId, message.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("urn:devalente:problem:conflict", response.Body.GetProperty("type").GetString());
        Assert.Equal(409, response.Body.GetProperty("status").GetInt32());
        var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
        Assert.Equal("collaboration_attempt.unavailable", error.GetProperty("code").GetString());
        Assert.DoesNotContain(attempt.Id.ToString(), response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Available_no_agent_attempt_and_unavailable_carry_only_the_documented_members()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId, number: 3);
        var observed = await SeedMessageAsync(runId, attempt.Id, index: 0);
        var human = await SeedMessageAsync(runId, attempt.Id, CollaborationMessageProvenance.HumanSubmitted, index: 1);
        var unlinked = await SeedMessageAsync(runId, null, index: 2);

        var available = await GetAttemptAsync(runId, observed.Id);
        var none = await GetAttemptAsync(runId, human.Id);
        var unavailable = await GetAttemptAsync(runId, unlinked.Id);

        Assert.Equal(HttpStatusCode.OK, available.Status);
        Assert.Equal(
            ["attempt", "messageId", "runId", "sourceSequence", "status"],
            available.Body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Available", available.Body.GetProperty("status").GetString());
        Assert.Equal(observed.Sequence.ToString(), available.Body.GetProperty("sourceSequence").GetString());
        Assert.Equal(observed.Id, available.Body.GetProperty("messageId").GetGuid());
        var header = available.Body.GetProperty("attempt");
        Assert.Equal(
            ["attemptNumber", "claimedAtUtc", "completedAtUtc", "dispatchedAtUtc", "id", "provider", "responseContract", "role", "status"],
            header.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(attempt.Id, header.GetProperty("id").GetGuid());
        Assert.Equal(3, header.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Planner", header.GetProperty("role").GetString());
        Assert.Equal("Codex", header.GetProperty("provider").GetString());
        Assert.Equal("Proposal", header.GetProperty("responseContract").GetString());
        Assert.Equal("Running", header.GetProperty("status").GetString());
        Assert.Equal(Now, header.GetProperty("claimedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, header.GetProperty("dispatchedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("completedAtUtc").ValueKind);
        foreach (var forbidden in new[]
        {
            "summary", "structuredContent", "outcome", "model", "effort", "session", "path", "argument", "artifact", "token", "checkpoint",
            "fingerprint", "workspace", "inReplyTo", "repair",
        })
        {
            Assert.DoesNotContain(forbidden, available.Text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("NoAgentAttempt", none.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, none.Body.GetProperty("attempt").ValueKind);
        Assert.DoesNotContain(attempt.Id.ToString(), none.Text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("attempt").ValueKind);
    }

    [Fact]
    public async Task The_header_reports_the_recorded_status_and_times_exactly_and_never_a_substitute_attempt()
    {
        var runId = await SeedRunAsync();
        var older = await SeedAttemptAsync(runId, number: 1);
        await ExecuteAsync($"UPDATE attempts SET Status = 'Completed', AgentDispatchedAtUtc = '2026-10-12 09:01:02.5+00:00', CompletedAtUtc = '2026-10-12 09:07:08.25+00:00' WHERE Id = {older.Id}");
        var latest = await SeedAttemptAsync(runId, number: 2);
        var message = await SeedMessageAsync(runId, older.Id);

        var response = await GetAttemptAsync(runId, message.Id);

        var header = response.Body.GetProperty("attempt");
        Assert.Equal(older.Id, header.GetProperty("id").GetGuid());
        Assert.NotEqual(latest.Id, header.GetProperty("id").GetGuid());
        Assert.Equal("Completed", header.GetProperty("status").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 1, 2, 500, TimeSpan.Zero), header.GetProperty("dispatchedAtUtc").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 7, 8, 250, TimeSpan.Zero), header.GetProperty("completedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task An_unsupported_identity_or_hostile_stored_text_is_unavailable_and_never_echoed()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await SeedMessageAsync(runId, attempt.Id);
        Assert.Equal("Available", (await GetAttemptAsync(runId, message.Id)).Body.GetProperty("status").GetString());

        await ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = {"x" + Secret + new string('m', 200)} WHERE Id = {attempt.Id}");
        var oversized = await GetAttemptAsync(runId, message.Id);
        Assert.Equal("Unavailable", oversized.Body.GetProperty("status").GetString());
        Assert.DoesNotContain(Secret, oversized.Text, StringComparison.Ordinal);

        await ExecuteAsync($"UPDATE attempts SET AgentRequestedModel = NULL, AgentRole = {"Overlord" + Secret} WHERE Id = {attempt.Id}");
        var unknown = await GetAttemptAsync(runId, message.Id);
        Assert.Equal("Unavailable", unknown.Body.GetProperty("status").GetString());
        Assert.DoesNotContain(Secret, unknown.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_an_attempt_attribution_writes_nothing()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedAttemptAsync(runId);
        var message = await SeedMessageAsync(runId, attempt.Id);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var attempts = await db.Attempts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.AttemptNumber + "|" + row.CompletedAtUtc).ToListAsync();
            var messages = await db.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.AttemptId + "|" + row.Summary).ToListAsync();
            return string.Join(";", attempts) + "#" + string.Join(";", messages) + "#" + await db.Events.CountAsync() + "#" + await db.Runs.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetAttemptAsync(runId, message.Id);
        await GetAttemptAsync(runId, Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_the_closed_response_and_the_shared_404_and_409_schemas()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/collaboration-history/{messageId}/attempt"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("messageId", "path"), ("runId", "path")], parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "404", "409"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetCollaborationHistoryAttemptResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        foreach (var status in new[] { "404", "409" })
        {
            Assert.Contains("ApiProblemDetails", responses[status]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        }

        JsonObject PropertiesOf(string name)
        {
            var schema = document["components"]!["schemas"]![name]!;
            return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        }

        var response = PropertiesOf("GetCollaborationHistoryAttemptResponse");
        Assert.Equal(["attempt", "messageId", "runId", "sourceSequence", "status"], response.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("string", response["sourceSequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("integer", response["sourceSequence"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("CollaborationAttemptHeaderResponse", response["attempt"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(
            ["attemptNumber", "claimedAtUtc", "completedAtUtc", "dispatchedAtUtc", "id", "provider", "responseContract", "role", "status"],
            PropertiesOf("CollaborationAttemptHeaderResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
    }
}
