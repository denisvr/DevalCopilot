using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0035: the protected, bodyless, read-only page of one run's recorded collaboration messages against the real host and
/// file-backed SQLite. Sequences are exact decimal strings end to end, the cursor is exclusive, unknown or defective stored values keep
/// their header as unavailable, and no raw JSON, attempt, reply target or secret is ever returned.
/// </summary>
public sealed class CollaborationHistoryEndpointTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task The_route_requires_authentication_and_a_wrong_credential_is_refused()
    {
        var runId = await SeedRunAsync();

        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(runId))).StatusCode);

        using var wrong = Factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route(runId))).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(Route(runId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(Route(runId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Route(runId))).StatusCode);
    }

    [Fact]
    public void Every_route_that_mentions_collaboration_history_is_one_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("collaboration-history", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/collaboration-history", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task An_unknown_run_is_a_safe_not_found_and_an_existing_run_without_messages_is_an_empty_page()
    {
        var unknown = await ReadAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Contains("runs.not_found", unknown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", unknown.Text, StringComparison.Ordinal);

        var runId = await SeedRunAsync();
        var empty = await ReadAsync(runId);
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Equal(runId, empty.Body.GetProperty("runId").GetGuid());
        Assert.Empty(empty.Body.GetProperty("entries").EnumerateArray());
        Assert.False(empty.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, empty.Body.GetProperty("nextBeforeSequence").ValueKind);
    }

    [Fact]
    public async Task The_page_carries_only_the_documented_members_with_exact_string_sequences_and_never_an_attempt_or_reply_target()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 3);

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["entries", "hasMore", "nextBeforeSequence", "runId"], page.Body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var entries = page.Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(
                ["actor", "contentStatus", "fields", "id", "occurredAtUtc", "protocolVersion", "provenance", "recipient", "runId",
                    "sequence", "summary", "type"],
                entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(["kind", "provider", "role"], entry.GetProperty("actor").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(["kind", "provider", "role"], entry.GetProperty("recipient").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(JsonValueKind.String, entry.GetProperty("sequence").ValueKind);
            Assert.Equal(runId, entry.GetProperty("runId").GetGuid());
            Assert.Equal("Available", entry.GetProperty("contentStatus").GetString());
            Assert.Equal("1.0", entry.GetProperty("protocolVersion").GetString());
            Assert.Equal("Proposal", entry.GetProperty("type").GetString());
            Assert.Equal("ProviderObserved", entry.GetProperty("provenance").GetString());
            Assert.Equal("Agent", entry.GetProperty("actor").GetProperty("kind").GetString());
            Assert.Equal("Planner", entry.GetProperty("actor").GetProperty("role").GetString());
            Assert.Equal("Codex", entry.GetProperty("actor").GetProperty("provider").GetString());
            Assert.Equal("Orchestrator", entry.GetProperty("recipient").GetProperty("kind").GetString());
            Assert.Equal(
                FieldNames[CollaborationMessageType.Proposal],
                entry.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()!));
            Assert.All(entry.GetProperty("fields").EnumerateArray(), field =>
                Assert.Equal(["name", "value"], field.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        }

        Assert.Equal(messages.Select(message => message.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse(), Sequences(page.Body));
        Assert.Equal(messages.Select(message => message.Id).Reverse(), entries.Select(entry => entry.GetProperty("id").GetGuid()));
        Assert.Equal(
            messages.Select(message => message.Summary).Reverse(), entries.Select(entry => entry.GetProperty("summary").GetString()));
        Assert.DoesNotContain("attemptId", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inReplyTo", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("structuredContentJson", page.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_descend_by_sequence_with_an_exclusive_cursor_and_never_leak_another_run_or_project()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        await AppendAsync(otherRun, 8);
        var mine = await AppendAsync(runId, 25);
        await AppendAsync(otherRun, 8);
        var expected = mine.Select(message => message.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().ToList();

        var first = await ReadAsync(runId, "limit=10");
        Assert.Equal(expected.Take(10), Sequences(first.Body));
        Assert.True(first.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(expected[9], first.Body.GetProperty("nextBeforeSequence").GetString());

        var second = await ReadAsync(runId, $"limit=10&beforeSequence={expected[9]}");
        Assert.Equal(expected.Skip(10).Take(10), Sequences(second.Body));
        Assert.DoesNotContain(expected[9], Sequences(second.Body));
        Assert.Equal(expected[19], second.Body.GetProperty("nextBeforeSequence").GetString());

        var third = await ReadAsync(runId, $"limit=10&beforeSequence={expected[19]}");
        Assert.Equal(expected.Skip(20), Sequences(third.Body));
        Assert.False(third.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, third.Body.GetProperty("nextBeforeSequence").ValueKind);

        var ids = mine.Select(message => message.Id).ToHashSet();
        foreach (var page in new[] { first, second, third })
        {
            Assert.Equal(runId, page.Body.GetProperty("runId").GetGuid());
            Assert.All(page.Body.GetProperty("entries").EnumerateArray(), entry => Assert.Contains(entry.GetProperty("id").GetGuid(), ids));
        }
    }

    [Fact]
    public async Task The_default_limit_is_ten_and_the_boundary_limits_are_accepted()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 21);

        var defaulted = await ReadAsync(runId);
        Assert.Equal(10, Sequences(defaulted.Body).Length);
        Assert.True(defaulted.Body.GetProperty("hasMore").GetBoolean());

        var maximum = await ReadAsync(runId, "limit=20");
        Assert.Equal(20, Sequences(maximum.Body).Length);
        Assert.True(maximum.Body.GetProperty("hasMore").GetBoolean());

        var one = await ReadAsync(runId, "limit=1");
        Assert.Single(Sequences(one.Body));
        Assert.True(one.Body.GetProperty("hasMore").GetBoolean());

        var exact = await ReadAsync(runId, $"limit=20&beforeSequence={Sequences(one.Body)[0]}");
        Assert.Equal(20, Sequences(exact.Body).Length);
        Assert.False(exact.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, exact.Body.GetProperty("nextBeforeSequence").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("limit=")]
    [InlineData("limit=%20")]
    [InlineData("beforeSequence=")]
    [InlineData("beforeSequence=%20%20")]
    [InlineData("limit=&beforeSequence=")]
    public async Task Omitted_or_blank_scalars_mean_absent(string query)
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 12);

        var page = await ReadAsync(runId, query);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(10, Sequences(page.Body).Length);
        Assert.True(page.Body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Messages_appended_between_pages_neither_repeat_nor_skip_an_older_message_and_gaps_are_valid()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var first = await AppendAsync(runId, 12);
        await AppendAsync(otherRun, 6);
        var second = await AppendAsync(runId, 13);
        var expected = first.Concat(second).Select(message => message.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().ToList();

        var page = await ReadAsync(runId, "limit=10");
        var appended = await AppendAsync(runId, 2);
        var older = await ReadAsync(runId, $"limit=10&beforeSequence={page.Body.GetProperty("nextBeforeSequence").GetString()}");

        Assert.Equal(expected.Skip(10).Take(10), Sequences(older.Body));
        var reload = await ReadAsync(runId, "limit=10");
        Assert.Equal(
            appended.Select(message => message.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().Concat(expected.Take(8)),
            Sequences(reload.Body));
        var all = await ReadAsync(runId, "limit=20");
        var sequences = Sequences(all.Body).Select(text => long.Parse(text, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(20, sequences.Count);
        Assert.Contains(sequences.Zip(sequences.Skip(1), (newer, older) => newer - older), gap => gap > 1);
    }

    public static TheoryData<CollaborationMessageType> MessageTypes()
    {
        var data = new TheoryData<CollaborationMessageType>();
        foreach (var type in Enum.GetValues<CollaborationMessageType>())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public async Task Every_current_message_schema_is_disclosed_with_its_exact_ordered_fields(CollaborationMessageType type)
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 1, type);

        var page = await ReadAsync(runId);

        var entry = Assert.Single(page.Body.GetProperty("entries").EnumerateArray());
        Assert.Equal(type.ToString(), entry.GetProperty("type").GetString());
        Assert.Equal("Available", entry.GetProperty("contentStatus").GetString());
        Assert.Equal(
            FieldNames[type].Select(name => (name, Value(name))),
            entry.GetProperty("fields").EnumerateArray().Select(field => (field.GetProperty("name").GetString()!, field.GetProperty("value").GetString()!)));
    }

    [Theory]
    [InlineData("Type", "Overlord")]
    [InlineData("Provenance", "Forged")]
    [InlineData("ProtocolVersion", "9.9")]
    [InlineData("ActorKind", "Wizard")]
    [InlineData("ActorAgentRole", "Overlord")]
    [InlineData("ActorAgentProvider", "Gemini")]
    [InlineData("RecipientKind", "Wizard")]
    [InlineData("RecipientAgentRole", "Overlord")]
    [InlineData("RecipientAgentProvider", "Gemini")]
    public async Task Unknown_stored_metadata_is_the_fixed_unrecognized_value_and_the_content_is_unavailable(string column, string stored)
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 3);
        var marked = stored + Secret;
        await ExecuteAsync(column switch
        {
            "Type" => $"UPDATE collaboration_messages SET Type = {marked} WHERE Id = {messages[1].Id}",
            "Provenance" => $"UPDATE collaboration_messages SET Provenance = {marked} WHERE Id = {messages[1].Id}",
            "ProtocolVersion" => $"UPDATE collaboration_messages SET ProtocolVersion = {marked} WHERE Id = {messages[1].Id}",
            "ActorKind" => $"UPDATE collaboration_messages SET ActorKind = {marked} WHERE Id = {messages[1].Id}",
            "ActorAgentRole" => $"UPDATE collaboration_messages SET ActorAgentRole = {marked} WHERE Id = {messages[1].Id}",
            "ActorAgentProvider" => $"UPDATE collaboration_messages SET ActorAgentProvider = {marked} WHERE Id = {messages[1].Id}",
            "RecipientKind" => $"UPDATE collaboration_messages SET RecipientKind = {marked} WHERE Id = {messages[1].Id}",
            "RecipientAgentRole" => $"UPDATE collaboration_messages SET RecipientAgentRole = {marked} WHERE Id = {messages[1].Id}",
            _ => $"UPDATE collaboration_messages SET RecipientAgentProvider = {marked} WHERE Id = {messages[1].Id}",
        });

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        var entries = page.Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["Available", "Unavailable", "Available"], entries.Select(entry => entry.GetProperty("contentStatus").GetString()));
        var damaged = entries[1];
        Assert.Equal(JsonValueKind.Null, damaged.GetProperty("summary").ValueKind);
        Assert.Empty(damaged.GetProperty("fields").EnumerateArray());
        Assert.Contains("Unrecognized", damaged.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overlord", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Wizard", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemini", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Forged", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_unsafe_and_oversized_content_is_unavailable_without_its_text_and_neighbors_stay_healthy()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 8);
        await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {"not json " + Secret} WHERE Id = {messages[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {"{\"scope\":\"" + Secret + "\",\"unknownField\":\"x\"}"} WHERE Id = {messages[1].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {"The password is " + Secret} WHERE Id = {messages[2].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {new string('s', 700_000) + Secret} WHERE Id = {messages[3].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = {new string('j', 900_000) + Secret} WHERE Id = {messages[4].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = {string.Concat(Enumerable.Repeat("\U0001F600", 600)) + Secret} WHERE Id = {messages[5].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(
            [true, true, false, false, false, false, false, false],
            page.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("contentStatus").GetString() == "Available"));
        Assert.All(page.Body.GetProperty("entries").EnumerateArray().Where(entry => entry.GetProperty("contentStatus").GetString() == "Unavailable"), entry =>
        {
            Assert.Equal("Proposal", entry.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("summary").ValueKind);
            Assert.Empty(entry.GetProperty("fields").EnumerateArray());
        });
        Assert.DoesNotContain(Secret, page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("unknownField", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(page.Text.Length < 20_000);
    }

    [Fact]
    public async Task Oversized_text_hidden_behind_an_embedded_nul_is_unavailable_and_never_returned()
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 3);
        var summary = System.Text.Encoding.UTF8.GetBytes("Safe summary\0" + new string('s', 700_000) + Secret);
        var content = System.Text.Encoding.UTF8.GetBytes(ContentJson(CollaborationMessageType.Proposal) + "\0" + new string('j', 900_000) + Secret);
        await ExecuteAsync($"UPDATE collaboration_messages SET Summary = CAST({summary} AS TEXT) WHERE Id = {messages[1].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET StructuredContentJson = CAST({content} AS TEXT) WHERE Id = {messages[2].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(
            ["Unavailable", "Unavailable", "Available"],
            page.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("contentStatus").GetString()));
        Assert.DoesNotContain(Secret, page.Text, StringComparison.Ordinal);
        Assert.True(page.Text.Length < 20_000);
    }

    [Theory]
    [InlineData("empty-identity")]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    public async Task A_selected_row_without_an_identity_or_a_positive_sequence_is_one_safe_409_and_never_a_partial_page(string defect)
    {
        var runId = await SeedRunAsync();
        var messages = await AppendAsync(runId, 4);
        var victim = messages[1];
        FormattableString statement = defect switch
        {
            "empty-identity" => $"UPDATE collaboration_messages SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {victim.Id}",
            "zero-sequence" => $"UPDATE collaboration_messages SET Sequence = 0 WHERE Id = {victim.Id}",
            _ => $"UPDATE collaboration_messages SET Sequence = -3 WHERE Id = {victim.Id}",
        };
        await ExecuteAsync(statement);

        var failed = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.Conflict, failed.Status);
        Assert.Equal("urn:devalente:problem:conflict", failed.Body.GetProperty("type").GetString());
        Assert.Equal("collaboration_history.unavailable", failed.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.DoesNotContain("entries", failed.Text.Replace("\"errors\"", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain(victim.Id.ToString(), failed.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", failed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_history_writes_nothing_and_never_calls_a_provider()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 12);
        async Task<(int, int, int, int, int)> Counts() => await WithDbAsync(async db => (
            await db.Runs.CountAsync(), await db.CollaborationMessages.CountAsync(), await db.Attempts.CountAsync(),
            await db.LocalCommitOperations.CountAsync(), await db.Events.CountAsync()));
        var before = await Counts();
        var probes = Factory.ToolDiscoveryAdapter.CallCount;

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ReadAsync(runId)).Status);
            Assert.Equal(HttpStatusCode.OK, (await ReadAsync(runId, "limit=3&beforeSequence=9223372036854775807")).Status);
        }

        Assert.Equal(before, await Counts());
        Assert.Equal(probes, Factory.ToolDiscoveryAdapter.CallCount);
    }
}
