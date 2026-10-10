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
/// ADR-0034: the protected, bodyless, read-only page of one run's persisted event headers against the real host and file-backed
/// SQLite. Sequences are exact decimal strings end to end, the cursor is exclusive, unknown stored values keep their rows, and no
/// payload, attempt or secret is ever selected or returned.
/// </summary>
public sealed class RunEventHistoryEndpointTests(ApiWebApplicationFactory factory)
    : RunEventHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
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
    public void Every_route_that_mentions_event_history_is_one_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("event-history", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/runs/{runId:guid}/event-history", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task An_unknown_run_is_a_safe_not_found_and_an_existing_run_without_events_is_an_empty_page()
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
    public async Task The_page_carries_only_the_documented_members_with_exact_string_sequences()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 3);

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["entries", "hasMore", "nextBeforeSequence", "runId"], page.Body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var entries = page.Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(["actor", "eventType", "id", "occurredAtUtc", "runId", "sequence"], entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(["kind", "provider", "role"], entry.GetProperty("actor").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(JsonValueKind.String, entry.GetProperty("sequence").ValueKind);
            Assert.Equal(runId, entry.GetProperty("runId").GetGuid());
        }

        Assert.Equal(events.Select(runEvent => runEvent.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse(), Sequences(page.Body));
        Assert.Equal(events.Select(runEvent => runEvent.Id).Reverse(), entries.Select(entry => entry.GetProperty("id").GetGuid()));
        Assert.Equal(
            events.Select(runEvent => runEvent.OccurredAtUtc).Reverse(), entries.Select(entry => entry.GetProperty("occurredAtUtc").GetDateTimeOffset()));
        Assert.All(entries, entry => Assert.Equal("collaboration.message_recorded", entry.GetProperty("eventType").GetString()));
        Assert.All(entries, entry => Assert.Equal("Orchestrator", entry.GetProperty("actor").GetProperty("kind").GetString()));
        Assert.All(entries, entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("actor").GetProperty("role").ValueKind));
        Assert.All(entries, entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("actor").GetProperty("provider").ValueKind));
        Assert.False(page.Body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Pages_descend_by_sequence_with_an_exclusive_cursor_and_never_leak_another_run_or_project()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        await AppendAsync(otherRun, 8);
        var mine = await AppendAsync(runId, 25);
        await AppendAsync(otherRun, 8);
        var expected = mine.Select(runEvent => runEvent.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().ToList();

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

        var ids = mine.Select(runEvent => runEvent.Id).ToHashSet();
        foreach (var page in new[] { first, second, third })
        {
            Assert.Equal(runId, page.Body.GetProperty("runId").GetGuid());
            Assert.All(page.Body.GetProperty("entries").EnumerateArray(), entry => Assert.Contains(entry.GetProperty("id").GetGuid(), ids));
        }
    }

    [Fact]
    public async Task The_default_limit_is_twenty_and_the_boundary_limits_are_accepted()
    {
        var runId = await SeedRunAsync();
        await AppendAsync(runId, 51);

        var defaulted = await ReadAsync(runId);
        Assert.Equal(20, Sequences(defaulted.Body).Length);
        Assert.True(defaulted.Body.GetProperty("hasMore").GetBoolean());

        var maximum = await ReadAsync(runId, "limit=50");
        Assert.Equal(50, Sequences(maximum.Body).Length);
        Assert.True(maximum.Body.GetProperty("hasMore").GetBoolean());

        var one = await ReadAsync(runId, "limit=1");
        Assert.Single(Sequences(one.Body));
        Assert.True(one.Body.GetProperty("hasMore").GetBoolean());

        var exact = await ReadAsync(runId, $"limit=50&beforeSequence={Sequences(one.Body)[0]}");
        Assert.Equal(50, Sequences(exact.Body).Length);
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
        await AppendAsync(runId, 22);

        var page = await ReadAsync(runId, query);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(20, Sequences(page.Body).Length);
        Assert.True(page.Body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Events_appended_between_pages_neither_repeat_nor_skip_an_older_header_and_a_reload_reads_the_newest()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 25);
        var expected = events.Select(runEvent => runEvent.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().ToList();

        var first = await ReadAsync(runId, "limit=10");
        var appended = await AppendAsync(runId, 2);
        var second = await ReadAsync(runId, $"limit=10&beforeSequence={first.Body.GetProperty("nextBeforeSequence").GetString()}");

        Assert.Equal(expected.Skip(10).Take(10), Sequences(second.Body));
        var reload = await ReadAsync(runId, "limit=10");
        Assert.Equal(
            appended.Select(runEvent => runEvent.Sequence.ToString(CultureInfo.InvariantCulture)).Reverse().Concat(expected.Take(8)),
            Sequences(reload.Body));
    }

    [Fact]
    public async Task Sequence_gaps_are_valid_and_the_page_never_infers_a_missing_event()
    {
        var runId = await SeedRunAsync();
        var otherRun = await SeedRunAsync();
        var first = await AppendAsync(runId, 2);
        await AppendAsync(otherRun, 6);
        var last = await AppendAsync(runId, 2);

        var page = await ReadAsync(runId);

        var sequences = Sequences(page.Body).Select(text => long.Parse(text, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(first.Concat(last).Select(runEvent => runEvent.Sequence).OrderDescending(), sequences);
        Assert.True(sequences[1] - sequences[2] > 1);
        Assert.Equal(4, sequences.Count);
    }

    [Fact]
    public async Task Every_known_event_type_is_disclosed_by_its_exact_constant_and_unknown_ones_are_only_unrecognized()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 4);
        await ExecuteAsync($"UPDATE events SET EventType = {RunEventType.RunAbandoned} WHERE Id = {events[0].Id}");
        await ExecuteAsync($"UPDATE events SET EventType = 'SECRET-EVENT-TYPE-5b' WHERE Id = {events[1].Id}");
        await ExecuteAsync($"UPDATE events SET EventType = 'RUN.STARTED' WHERE Id = {events[2].Id}");
        await ExecuteAsync($"UPDATE events SET EventType = {RunEventType.LocalCommitNeedsAttention} WHERE Id = {events[3].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(
            ["local_commit.needs_attention", "Unrecognized", "Unrecognized", "run.abandoned"],
            page.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("eventType").GetString()!));
        Assert.DoesNotContain("SECRET-EVENT-TYPE", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("RUN.STARTED", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actor_disclosures_keep_valid_unknown_role_agents_and_report_unknown_or_inconsistent_actors_as_unrecognized()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 7);
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Agent', ActorAgentRole = 'Planner', ActorAgentProvider = 'Codex' WHERE Id = {events[0].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Agent', ActorAgentRole = NULL, ActorAgentProvider = 'ClaudeCode' WHERE Id = {events[1].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Human' WHERE Id = {events[2].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Agent', ActorAgentRole = 'Overlord', ActorAgentProvider = 'Codex' WHERE Id = {events[3].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Human', ActorAgentRole = 'Planner' WHERE Id = {events[4].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'Agent', ActorAgentRole = 'Planner', ActorAgentProvider = NULL WHERE Id = {events[5].Id}");
        await ExecuteAsync($"UPDATE events SET ActorKind = 'SECRET-ACTOR-9c' WHERE Id = {events[6].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        var actors = page.Body.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("actor"))
            .Select(actor => (
                actor.GetProperty("kind").GetString(), actor.GetProperty("role").GetString(), actor.GetProperty("provider").GetString()))
            .Reverse()
            .ToList();
        Assert.Equal(
            [
                ("Agent", "Planner", "Codex"),
                ("Agent", null, "ClaudeCode"),
                ("Human", null, null),
                ("Unrecognized", null, null),
                ("Unrecognized", null, null),
                ("Unrecognized", null, null),
                ("Unrecognized", null, null),
            ],
            actors);
        Assert.DoesNotContain("SECRET-ACTOR", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overlord", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_payloads_attempt_links_and_secret_sentinels_are_never_selected_or_returned()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 3, payload: "{\"token\":\"SECRET-SENTINEL-4e1d\",");
        await ExecuteAsync($"UPDATE events SET PayloadJson = 'SECRET-SENTINEL-4e1d <script>' WHERE Id = {events[1].Id}");
        await ExecuteAsync($"UPDATE events SET PayloadJson = '' WHERE Id = {events[2].Id}");

        var page = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(3, Sequences(page.Body).Length);
        Assert.DoesNotContain("SECRET-SENTINEL", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attemptId", page.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("empty-identity")]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    public async Task A_selected_header_without_an_identity_or_a_positive_sequence_is_one_safe_409_and_never_a_partial_page(string defect)
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 4);
        var victim = events[1];
        FormattableString statement = defect switch
        {
            "empty-identity" => $"UPDATE events SET Id = '00000000-0000-0000-0000-000000000000' WHERE Id = {victim.Id}",
            "zero-sequence" => $"UPDATE events SET Sequence = 0 WHERE Id = {victim.Id}",
            _ => $"UPDATE events SET Sequence = -3 WHERE Id = {victim.Id}",
        };
        await ExecuteAsync(statement);

        var failed = await ReadAsync(runId);

        Assert.Equal(HttpStatusCode.Conflict, failed.Status);
        Assert.Equal("urn:devalente:problem:conflict", failed.Body.GetProperty("type").GetString());
        var error = failed.Body.GetProperty("errors")[0];
        Assert.Equal("run_event_history.unavailable", error.GetProperty("code").GetString());
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
            await db.Runs.CountAsync(), await db.Events.CountAsync(), await db.Attempts.CountAsync(),
            await db.LocalCommitOperations.CountAsync(), await db.Projects.CountAsync()));
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
