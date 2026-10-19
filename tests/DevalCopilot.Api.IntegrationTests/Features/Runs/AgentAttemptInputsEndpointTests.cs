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
/// ADR-0051: the protected, bodyless, read-only recorded-input inspection of one Agent attempt, against the real host and file-backed
/// SQLite. An unknown run and a missing, foreign or non-Agent attempt are one fixed 404, the answer is the closed status with a minimal
/// header and reference entries whose exact sequence and time are strings, and nothing is written, echoed or substituted.
/// </summary>
public sealed class AgentAttemptInputsEndpointTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static string InputsRoute(Guid runId, Guid attemptId) => $"/api/runs/{runId}/agent-attempts/{attemptId}/inputs";

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetInputsAsync(Guid runId, Guid attemptId, string? query = null)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(InputsRoute(runId, attemptId) + (query is null ? string.Empty : "?" + query));
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

    private async Task<List<CollaborationMessage>> SeedInputsAsync(Guid runId, Guid attemptId, int count)
    {
        var messages = await AppendAsync(runId, count);
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

    private static readonly string[] RootMembers =
        ["attempt", "attemptId", "inputMessageTotalCount", "inputMessages", "inputMessagesOmitted", "runId", "status"];

    private static readonly string[] HeaderMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] EntryMembers = ["collaborationMessageId", "collaborationSequence", "occurredAtUtc", "sequence", "type"];

    private static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedPlannerAsync(runId);

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(InputsRoute(runId, attempt.Id))).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(InputsRoute(runId, attempt.Id))).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(InputsRoute(runId, attempt.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(InputsRoute(runId, attempt.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(InputsRoute(runId, attempt.Id))).StatusCode);
    }

    [Fact]
    public void Exactly_one_inputs_route_exists_and_it_is_an_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.EndsWith("/inputs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/agent-attempts/{attemptId:guid}/inputs", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_foreign_or_non_agent_attempt_are_the_same_fixed_not_found_problem()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var foreign = await SeedPlannerAsync(otherRun);
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
            await GetInputsAsync(Guid.NewGuid(), foreign.Id),
            await GetInputsAsync(runId, unknownId),
            await GetInputsAsync(runId, foreign.Id),
            await GetInputsAsync(simulatedRun, simulated.Id),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("agent_attempt_inputs.not_found", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknownId, foreign.Id, simulated.Id, runId, otherRun, simulatedRun })
            {
                Assert.DoesNotContain(value.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Fact]
    public async Task A_malformed_attempt_identifier_is_not_routed_and_is_not_echoed()
    {
        var runId = await SeedRunAsync();
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/not-a-guid-{Secret}/inputs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorded_carries_only_the_documented_members_with_exact_string_sequences_and_utc_round_trip_times()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId, number: 4);
        var messages = await SeedInputsAsync(runId, attempt.Id, 3);

        var response = await GetInputsAsync(runId, attempt.Id);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal(runId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(attempt.Id, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(3, response.Body.GetProperty("inputMessageTotalCount").GetInt32());
        Assert.False(response.Body.GetProperty("inputMessagesOmitted").GetBoolean());
        var header = response.Body.GetProperty("attempt");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal(attempt.Id, header.GetProperty("id").GetGuid());
        Assert.Equal(4, header.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("Resolver", header.GetProperty("role").GetString());
        Assert.Equal("Codex", header.GetProperty("provider").GetString());
        Assert.Equal("ChallengeResolution", header.GetProperty("responseContract").GetString());

        var entries = response.Body.GetProperty("inputMessages").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            Assert.Equal(EntryMembers, Names(entry));
            Assert.Equal(JsonValueKind.Number, entry.GetProperty("sequence").ValueKind);
            Assert.Equal(index, entry.GetProperty("sequence").GetInt32());
            Assert.Equal(messages[index].Id, entry.GetProperty("collaborationMessageId").GetGuid());
            Assert.Equal("Proposal", entry.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.String, entry.GetProperty("collaborationSequence").ValueKind);
            Assert.Equal(messages[index].Sequence.ToString(CultureInfo.InvariantCulture), entry.GetProperty("collaborationSequence").GetString());
            Assert.Equal(JsonValueKind.String, entry.GetProperty("occurredAtUtc").ValueKind);
            var occurred = entry.GetProperty("occurredAtUtc").GetString()!;
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", occurred);
            Assert.Equal(messages[index].OccurredAtUtc, DateTimeOffset.Parse(occurred, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        foreach (var forbidden in new[]
        {
            "summary", "structuredContent", "Recorded scope", "outcome", "model", "effort", "session", "path", "argument", "artifact", "token",
            "checkpoint", "fingerprint", "workspace", "inReplyTo", "repair", "claimedAt", "dispatchedAt", "completedAt", "provenance",
        })
        {
            Assert.DoesNotContain(forbidden, response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Eleven_inputs_show_the_first_ten_and_disclose_the_exact_count()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 11);

        var response = await GetInputsAsync(runId, attempt.Id);

        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(11, response.Body.GetProperty("inputMessageTotalCount").GetInt32());
        Assert.True(response.Body.GetProperty("inputMessagesOmitted").GetBoolean());
        Assert.Equal(
            messages.Take(10).Select(message => message.Id),
            response.Body.GetProperty("inputMessages").EnumerateArray().Select(entry => entry.GetProperty("collaborationMessageId").GetGuid()));
    }

    [Fact]
    public async Task Empty_and_unavailable_carry_only_the_documented_members_and_no_target()
    {
        var runId = await SeedRunAsync();
        var planner = await SeedPlannerAsync(runId, number: 2);
        var resolverRun = await SeedRunAsync();
        var resolver = await SeedResolverAsync(resolverRun);
        var unknownRun = await SeedRunAsync();
        var unknown = await SeedResolverAsync(unknownRun);
        await SeedInputsAsync(unknownRun, unknown.Id, 2);
        await ExecuteAsync($"UPDATE attempts SET AgentRole = {"Overlord" + Secret} WHERE Id = {unknown.Id}");

        var empty = await GetInputsAsync(runId, planner.Id);
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Equal(RootMembers, Names(empty.Body));
        Assert.Equal("Empty", empty.Body.GetProperty("status").GetString());
        Assert.Equal(HeaderMembers, Names(empty.Body.GetProperty("attempt")));
        Assert.Equal("Planner", empty.Body.GetProperty("attempt").GetProperty("role").GetString());
        Assert.Equal(2, empty.Body.GetProperty("attempt").GetProperty("attemptNumber").GetInt32());
        Assert.Equal(0, empty.Body.GetProperty("inputMessageTotalCount").GetInt32());
        Assert.Empty(empty.Body.GetProperty("inputMessages").EnumerateArray());
        Assert.False(empty.Body.GetProperty("inputMessagesOmitted").GetBoolean());

        foreach (var (run, attempt) in new[] { (resolverRun, resolver), (unknownRun, unknown) })
        {
            var unavailable = await GetInputsAsync(run, attempt.Id);
            Assert.Equal(HttpStatusCode.OK, unavailable.Status);
            Assert.Equal(RootMembers, Names(unavailable.Body));
            Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
            Assert.Equal(run, unavailable.Body.GetProperty("runId").GetGuid());
            Assert.Equal(attempt.Id, unavailable.Body.GetProperty("attemptId").GetGuid());
            Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("attempt").ValueKind);
            Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("inputMessageTotalCount").ValueKind);
            Assert.Empty(unavailable.Body.GetProperty("inputMessages").EnumerateArray());
            Assert.False(unavailable.Body.GetProperty("inputMessagesOmitted").GetBoolean());
            Assert.DoesNotContain(Secret, unavailable.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_sixty_fifth_input_is_unavailable_at_the_boundary_while_sixty_four_are_recorded()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 64);

        var recorded = await GetInputsAsync(runId, attempt.Id);
        Assert.Equal("Recorded", recorded.Body.GetProperty("status").GetString());
        Assert.Equal(64, recorded.Body.GetProperty("inputMessageTotalCount").GetInt32());
        Assert.Equal(10, recorded.Body.GetProperty("inputMessages").GetArrayLength());

        var extra = await AppendAsync(runId, 1);
        await WithDbAsync(async db =>
        {
            db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, extra[0].Id, 64));
            return await db.SaveChangesAsync();
        });

        var unavailable = await GetInputsAsync(runId, attempt.Id);
        Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("inputMessageTotalCount").ValueKind);
        Assert.Empty(unavailable.Body.GetProperty("inputMessages").EnumerateArray());
        Assert.DoesNotContain(messages[0].Id.ToString(), unavailable.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, 10, true)]
    [InlineData("limit=", 10, true)]
    [InlineData("limit=%20", 10, true)]
    [InlineData("limit=1", 1, true)]
    [InlineData("limit=10", 10, true)]
    [InlineData("limit=11", 11, false)]
    [InlineData("limit=64", 11, false)]
    public async Task An_admitted_limit_returns_exactly_that_prefix_and_an_absent_or_single_blank_one_keeps_ten(
        string? query, int shown, bool omitted)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 11);

        var response = await GetInputsAsync(runId, attempt.Id, query);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(11, response.Body.GetProperty("inputMessageTotalCount").GetInt32());
        Assert.Equal(omitted, response.Body.GetProperty("inputMessagesOmitted").GetBoolean());
        Assert.Equal(
            messages.Take(shown).Select(message => message.Id),
            response.Body.GetProperty("inputMessages").EnumerateArray().Select(entry => entry.GetProperty("collaborationMessageId").GetGuid()));
        if (query is null)
        {
            Assert.Equal(Normalize((await GetInputsAsync(runId, attempt.Id, "limit=10")).Text), Normalize(response.Text));
        }
    }

    [Fact]
    public async Task Sixty_four_inputs_with_limit_sixty_four_return_every_reference_and_sixty_five_stay_unavailable()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var messages = await SeedInputsAsync(runId, attempt.Id, 64);

        var recorded = await GetInputsAsync(runId, attempt.Id, "limit=64");
        Assert.Equal("Recorded", recorded.Body.GetProperty("status").GetString());
        Assert.False(recorded.Body.GetProperty("inputMessagesOmitted").GetBoolean());
        Assert.Equal(
            messages.Select(message => message.Id),
            recorded.Body.GetProperty("inputMessages").EnumerateArray().Select(entry => entry.GetProperty("collaborationMessageId").GetGuid()));

        var extra = await AppendAsync(runId, 1);
        await WithDbAsync(async db =>
        {
            db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, extra[0].Id, 64));
            return await db.SaveChangesAsync();
        });

        var unavailable = await GetInputsAsync(runId, attempt.Id, "limit=64");
        Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("inputMessageTotalCount").ValueKind);
        Assert.Empty(unavailable.Body.GetProperty("inputMessages").EnumerateArray());
    }

    public static TheoryData<string, string> RejectedLimits() => new()
    {
        { "limit=0", string.Empty },
        { "limit=65", string.Empty },
        { "limit=-1", string.Empty },
        { "limit=%2B5", string.Empty },
        { "limit=05", string.Empty },
        { "limit=%205", string.Empty },
        { "limit=5%20", string.Empty },
        { "limit=1.5", "1.5" },
        { "limit=1e1", "1e1" },
        { "limit=0x10", "0x10" },
        { "limit=reviewer-invalid-limit", "reviewer-invalid-limit" },
        { "limit=99999999999999999999", "99999999999999999999" },
        { "limit=2147483648", "2147483648" },
        { "limit=%E2%82%AC", "\u20AC" },
        { "limit=%D9%A5", "\u0665" },
        { "limit=5&limit=5", string.Empty },
        { "limit=&limit=", string.Empty },
        { "limit=5&limit=", string.Empty },
        { "limit=86420", "86420" },
    };

    [Theory]
    [MemberData(nameof(RejectedLimits))]
    public async Task A_rejected_limit_is_the_shared_validation_problem_without_the_value_and_never_a_clamped_answer(string query, string rejected)
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        await SeedInputsAsync(runId, attempt.Id, 11);

        foreach (var (run, attemptId) in new[] { (runId, attempt.Id), (Guid.NewGuid(), Guid.NewGuid()) })
        {
            var response = await GetInputsAsync(run, attemptId, query);

            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            Assert.Equal("urn:devalente:problem:validation", response.Body.GetProperty("type").GetString());
            Assert.Equal(400, response.Body.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("validation.invalid_limit", error.GetProperty("code").GetString());
            Assert.Equal("#/limit", error.GetProperty("pointer").GetString());
            Assert.Equal("The limit must be a whole number from 1 to 64.", error.GetProperty("detail").GetString());
            Assert.False(response.Body.TryGetProperty("inputMessages", out _));
            Assert.DoesNotContain("ValidationProblemDetails", response.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("is not valid", response.Text, StringComparison.Ordinal);
            if (rejected.Length > 0)
            {
                Assert.DoesNotContain(rejected, Normalize(response.Text), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Reading_recorded_inputs_writes_nothing()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        await SeedInputsAsync(runId, attempt.Id, 2);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var attempts = await db.Attempts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.AttemptNumber + "|" + row.CompletedAtUtc).ToListAsync();
            var messages = await db.CollaborationMessages.AsNoTracking().OrderBy(row => row.Sequence)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.AttemptId + "|" + row.Summary).ToListAsync();
            var inputs = await db.AttemptInputMessages.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.AttemptId + "|" + row.CollaborationMessageId + "|" + row.Sequence).ToListAsync();
            return string.Join(";", attempts) + "#" + string.Join(";", messages) + "#" + string.Join(";", inputs) + "#"
                + await db.Events.CountAsync() + "#" + await db.Runs.CountAsync() + "#" + await db.Artifacts.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetInputsAsync(runId, attempt.Id);
        await GetInputsAsync(runId, Guid.NewGuid());

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_the_bodyless_route_the_closed_response_and_string_sequence_and_time()
    {
        var generator = Factory.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/agent-attempts/{attemptId}/inputs"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("attemptId", "path"), ("limit", "query"), ("runId", "path")], parameters.Order());
        var limit = operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "limit")!;
        Assert.NotEqual(true, limit["required"]?.GetValue<bool>());
        Assert.Contains("integer", limit.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "400", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetAgentAttemptInputsResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["400"]!["content"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        JsonObject PropertiesOf(string name)
        {
            var schema = document["components"]!["schemas"]![name]!;
            return (schema["properties"] ?? schema["allOf"]!.AsArray().First(node => node!["properties"] is not null)!["properties"])!.AsObject();
        }

        Assert.Equal(RootMembers, PropertiesOf("GetAgentAttemptInputsResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(HeaderMembers, PropertiesOf("AgentAttemptInputsHeaderResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        var entry = PropertiesOf("AgentAttemptInputReferenceResponse");
        Assert.Equal(EntryMembers, entry.Select(item => item.Key).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "collaborationSequence", "occurredAtUtc" })
        {
            var schema = entry[name]!.ToJsonString();
            Assert.Contains("\"string\"", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("integer", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("date-time", schema, StringComparison.Ordinal);
        }

        Assert.Contains("integer", entry["sequence"]!.ToJsonString(), StringComparison.Ordinal);
    }
}
