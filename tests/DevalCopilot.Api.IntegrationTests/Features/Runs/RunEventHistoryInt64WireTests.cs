using System.Globalization;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0034: sequences around 2^53 and up to the signed 64-bit maximum roundtrip exactly through the wire and the cursor. A row at
/// the maximum saturates SQLite's sequence counter, so this is the only test of its class and owns a private host: no other control
/// appends to this database afterwards.
/// </summary>
public sealed class RunEventHistoryInt64WireTests(ApiWebApplicationFactory factory)
    : RunEventHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Int64_sequences_roundtrip_exactly_through_the_wire_and_the_cursor_including_values_around_two_to_the_fifty_third()
    {
        var runId = await SeedRunAsync();
        var events = await AppendAsync(runId, 6);
        string[] values =
        [
            "9007199254740990", "9007199254740991", "9007199254740992", "9007199254740993", "9223372036854775806", "9223372036854775807",
        ];
        for (var index = 0; index < values.Length; index++)
        {
            var (id, value) = (events[index].Id, long.Parse(values[index], CultureInfo.InvariantCulture));
            await ExecuteAsync($"UPDATE events SET Sequence = {value} WHERE Id = {id}");
        }

        var all = await ReadAsync(runId, "limit=50");
        Assert.Equal(values.Reverse(), Sequences(all.Body));
        Assert.Contains("\"sequence\":\"9223372036854775807\"", all.Text, StringComparison.Ordinal);
        Assert.Contains("\"sequence\":\"9007199254740993\"", all.Text, StringComparison.Ordinal);

        var paged = await ReadAsync(runId, "limit=2&beforeSequence=9223372036854775807");
        Assert.Equal(["9223372036854775806", "9007199254740993"], Sequences(paged.Body));
        Assert.Equal("9007199254740993", paged.Body.GetProperty("nextBeforeSequence").GetString());

        // A floating-point cursor would collapse 2^53 + 1 onto 2^53; the neighbors stay distinguishable.
        var next = await ReadAsync(runId, "limit=50&beforeSequence=9007199254740993");
        Assert.Equal(["9007199254740992", "9007199254740991", "9007199254740990"], Sequences(next.Body));
        var even = await ReadAsync(runId, "limit=50&beforeSequence=9007199254740992");
        Assert.Equal(["9007199254740991", "9007199254740990"], Sequences(even.Body));
        var none = await ReadAsync(runId, "beforeSequence=9007199254740990");
        Assert.Empty(none.Body.GetProperty("entries").EnumerateArray());
    }
}
