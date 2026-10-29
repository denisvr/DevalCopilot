using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>
/// Full-tick equality and the supported instants of the recorded abandonment inspection (ADR-0071): the producer's own text at every tick
/// precision and offset is Recorded and normalized to the same instant, equal instants in different offsets agree, the policy-coherent
/// default instant stays supported (ADR-0070's nondefault exclusion belongs to that operation alone), and the abandonment column alone
/// is TEXT-only: a BLOB holding the exact timestamp bytes is refused beside the identical TEXT control. The other time columns are read
/// as the stored bytes of the writer's text, exactly like every sibling inspector's bounded byte projection.
/// </summary>
public sealed class RunAbandonmentInspectionTimeTests : RunAbandonmentInspectionTestBase
{
    private static readonly DateTimeOffset Whole = AbandonmentRaceSupport.AbandonedAt;

    public static TheoryData<string> Instants() => ["whole seconds", "fractional seconds", "one tick", "positive offset", "negative fractional offset"];

    private static DateTimeOffset Instant(string name) => name switch
    {
        "whole seconds" => Whole,
        "fractional seconds" => Whole.AddTicks(1234567),
        "one tick" => Whole.AddTicks(1),
        "positive offset" => new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(3)),
        "negative fractional offset" => new DateTimeOffset(2026, 10, 8, 6, 30, 0, 250, TimeSpan.FromHours(-5.5)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task Every_supported_instant_the_producer_writes_is_recorded_at_full_tick_precision(string name)
    {
        var instant = Instant(name);
        var closed = await AbandonAsync(at: instant);

        var recorded = Recorded(await InspectAsync(closed.RunId));

        Assert.Equal(instant, recorded.AbandonedAtUtc);
        Assert.Equal(instant.UtcTicks, recorded.AbandonedAtUtc.UtcTicks);
        Assert.Equal(instant.UtcDateTime, recorded.AbandonedAtUtc.UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Instants))]
    public async Task A_blob_holding_the_exact_text_of_the_abandonment_time_is_refused_beside_its_text_control(string name)
    {
        var text = await AbandonAsync(at: Instant(name));
        var blob = await AbandonAsync(at: Instant(name));
        await ExecuteAsync($"UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = '{Id(blob.RunId)}'");

        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(text.RunId)).Status);
        var refused = await InspectAsync(blob.RunId);
        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, refused.Status);
        Assert.Null(refused.Abandonment);
    }

    [Theory]
    [InlineData("0001-01-01 00:00:00+00:00")]
    [InlineData("0001-01-01 00:00:00.0000001+00:00")]
    [InlineData("2026-10-08 12:00:00.0000000+00:00")]
    [InlineData("9999-12-31 23:59:59.9999999+00:00")]
    public async Task The_default_instant_and_the_calendar_extremes_stay_supported_when_every_time_agrees(string stored)
    {
        var closed = await AbandonAsync();
        Assert.True(await ExecuteAsync(
            $"UPDATE runs SET AbandonedAtUtc = '{stored}', LastAdvancedAtUtc = '{stored}' WHERE Id = '{Id(closed.RunId)}'; "
            + $"UPDATE events SET OccurredAtUtc = '{stored}' WHERE RunId = '{Id(closed.RunId)}' AND EventType = 'run.abandoned'") > 0);

        var recorded = Recorded(await InspectAsync(closed.RunId));

        Assert.Equal(
            DateTimeOffset.ParseExact(stored, "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture).UtcTicks,
            recorded.AbandonedAtUtc.UtcTicks);
        Assert.Equal(TimeSpan.Zero, recorded.AbandonedAtUtc.Offset);
    }

    [Fact]
    public async Task Equal_instants_written_in_different_offsets_agree_and_one_tick_apart_do_not()
    {
        var closed = await AbandonAsync();
        var run = Id(closed.RunId);
        await ExecuteAsync(
            $"UPDATE runs SET AbandonedAtUtc = '2026-10-08 15:00:00+03:00', LastAdvancedAtUtc = '2026-10-08 12:00:00+00:00' WHERE Id = '{run}'; "
            + $"UPDATE events SET OccurredAtUtc = '2026-10-08 06:30:00-05:30' WHERE RunId = '{run}' AND EventType = 'run.abandoned'");

        Assert.Equal(Whole.UtcTicks, Recorded(await InspectAsync(closed.RunId)).AbandonedAtUtc.UtcTicks);

        await ExecuteAsync($"UPDATE runs SET AbandonedAtUtc = '2026-10-08 15:00:00.0000001+03:00' WHERE Id = '{run}'");

        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, (await InspectAsync(closed.RunId)).Status);
    }

    [Fact]
    public async Task An_explicit_zero_fraction_and_a_trimmed_fraction_are_the_same_instant()
    {
        var closed = await AbandonAsync();
        var run = Id(closed.RunId);
        await ExecuteAsync(
            $"UPDATE runs SET AbandonedAtUtc = '2026-10-08 12:00:00.0000000+00:00' WHERE Id = '{run}'; "
            + $"UPDATE events SET OccurredAtUtc = '2026-10-08 12:00:00.000000+00:00' WHERE RunId = '{run}' AND EventType = 'run.abandoned'");

        Assert.Equal(Whole, Recorded(await InspectAsync(closed.RunId)).AbandonedAtUtc);
    }

    [Theory]
    [InlineData("events", "OccurredAtUtc")]
    [InlineData("runs", "LastAdvancedAtUtc")]
    public async Task A_blob_of_the_exact_text_of_another_time_column_reads_as_that_text_like_the_sibling_inspectors(string table, string column)
    {
        var closed = await AbandonAsync();
        var key = table == "runs" ? $"Id = '{Id(closed.RunId)}'" : $"RunId = '{Id(closed.RunId)}' AND EventType = 'run.abandoned'";
        Assert.True(await ExecuteAsync($"UPDATE {table} SET {column} = CAST({column} AS BLOB) WHERE {key}") > 0);

        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(closed.RunId)).Status);
    }
}
