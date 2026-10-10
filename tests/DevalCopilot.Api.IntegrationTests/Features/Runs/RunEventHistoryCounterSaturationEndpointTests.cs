using System.Net;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0034: a stored row at the signed 64-bit maximum saturates SQLite's sequence counter, so nothing can be appended to that host's
/// database afterwards. This is the only class in the project that does it and its host is private, so no other control observes a
/// saturated counter. The wire and the cursor still carry the maximum exactly.
/// </summary>
public sealed class RunEventHistoryCounterSaturationEndpointTests(ApiWebApplicationFactory factory)
    : RunEventHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task A_header_stored_at_the_int64_maximum_roundtrips_exactly_as_a_string_and_as_a_cursor()
    {
        var runId = await SeedRunAsync();
        var older = await AppendAsync(runId, 1);
        var maximumId = Guid.NewGuid();
        await ExecuteAsync(
            $@"INSERT INTO events (Sequence, Id, RunId, AttemptId, EventType, ActorKind, ActorAgentRole, ActorAgentProvider, PayloadJson, OccurredAtUtc)
               VALUES ({long.MaxValue}, {maximumId}, {runId}, NULL, {RunEventType.RunCompleted}, 'Orchestrator', NULL, NULL, '{{}}', '2026-10-09 10:00:00+00:00')");

        var all = await ReadAsync(runId);
        var one = await ReadAsync(runId, "limit=1");
        var below = await ReadAsync(runId, "beforeSequence=9223372036854775807");

        Assert.Equal(HttpStatusCode.OK, all.Status);
        Assert.Equal(["9223372036854775807", older[0].Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)], Sequences(all.Body));
        Assert.Contains("\"sequence\":\"9223372036854775807\"", all.Text, StringComparison.Ordinal);
        Assert.Equal(maximumId, all.Body.GetProperty("entries")[0].GetProperty("id").GetGuid());
        Assert.Equal("9223372036854775807", one.Body.GetProperty("nextBeforeSequence").GetString());
        Assert.Equal([older[0].Id], below.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));

        // The saturation is real: the counter admits no further append, and the read model never claimed otherwise.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => AppendAsync(runId, 1));
    }
}
