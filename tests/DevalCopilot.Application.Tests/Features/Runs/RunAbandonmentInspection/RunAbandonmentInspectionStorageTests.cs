using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RunAbandonmentInspection;

/// <summary>
/// The storage bounds and text semantics of the recorded abandonment inspection (ADR-0071): every production reason up to 2048 UTF-8
/// bytes (LF multiline, two-, three- and four-byte scalars, including surrogate pairs) is Recorded exactly, the event payload bound of
/// 16384 bytes admits the worst production escaping (every one of 2048 UTF-16 units written as six ASCII bytes) and refuses one byte
/// more, and a reason beyond the bound, an invalid byte sequence or a non-canonical reason is Unavailable without a partial answer.
/// </summary>
public sealed class RunAbandonmentInspectionStorageTests : RunAbandonmentInspectionTestBase
{
    private const int MaximumReasonBytes = 2048;
    private const int PayloadBound = 16384;

    private static string Repeat(string scalar, int bytes) => string.Concat(Enumerable.Repeat(scalar, bytes / Encoding.UTF8.GetByteCount(scalar)));

    public static TheoryData<string> Reasons()
    {
        var twoByte = char.ConvertFromUtf32(0xE9);
        var threeByte = char.ConvertFromUtf32(0x4E2D);
        var fourByte = char.ConvertFromUtf32(0x1F600);
        return new TheoryData<string>
        {
            new string('a', MaximumReasonBytes),
            Repeat(twoByte, MaximumReasonBytes),
            Repeat(threeByte, MaximumReasonBytes - 2) + "ab",
            Repeat(fourByte, MaximumReasonBytes),
            string.Join("\n", Enumerable.Repeat(new string('l', 31), 64)),
            new string('<', MaximumReasonBytes),
            new string('"', MaximumReasonBytes),
        };
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task A_production_reason_up_to_the_two_kibibyte_bound_is_recorded_exactly(string reason)
    {
        Assert.True(Encoding.UTF8.GetByteCount(reason) <= MaximumReasonBytes);
        var closed = await AbandonAsync(reason: reason);

        var recorded = Recorded(await InspectAsync(closed.RunId));

        Assert.Equal(reason, recorded.Reason);
        Assert.Equal(closed.EventId, recorded.EventId);
    }

    [Fact]
    public async Task The_worst_production_escaping_fits_the_payload_bound_with_room_to_spare()
    {
        var closed = await AbandonAsync(reason: new string('<', MaximumReasonBytes));
        await using var dbContext = Fixture.CreateContext();
        var payloadBytes = await dbContext.Database
            .SqlQuery<int>($"SELECT length(CAST(PayloadJson AS BLOB)) AS Value FROM events WHERE RunId = {Id(closed.RunId)} AND EventType = 'run.abandoned'")
            .SingleAsync();

        Assert.InRange(payloadBytes, MaximumReasonBytes * 6, PayloadBound - 1);
        Assert.Equal(RunAbandonmentInspectionStatus.Recorded, (await InspectAsync(closed.RunId)).Status);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task A_payload_of_exactly_the_bound_is_admitted_and_one_byte_more_is_refused(int extraBytes, bool recorded)
    {
        var closed = await AbandonAsync(reason: new string('<', MaximumReasonBytes));
        var run = Id(closed.RunId);
        Assert.True(await ExecuteAsync(
            "UPDATE events SET PayloadJson = PayloadJson || substr(replace(hex(zeroblob(8192)), '0', ' '), 1, "
            + $"{PayloadBound + extraBytes} - length(CAST(PayloadJson AS BLOB))) WHERE RunId = '{run}' AND EventType = 'run.abandoned'") > 0);
        await using var dbContext = Fixture.CreateContext();
        Assert.Equal(
            PayloadBound + extraBytes,
            await dbContext.Database
                .SqlQuery<int>($"SELECT length(CAST(PayloadJson AS BLOB)) AS Value FROM events WHERE RunId = {run} AND EventType = 'run.abandoned'")
                .SingleAsync());

        var result = await InspectAsync(closed.RunId);

        Assert.Equal(recorded ? RunAbandonmentInspectionStatus.Recorded : RunAbandonmentInspectionStatus.Unavailable, result.Status);
        Assert.Equal(recorded, result.Abandonment is not null);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task A_reason_of_exactly_two_kibibytes_is_admitted_and_one_byte_more_is_refused(int extraBytes, bool recorded)
    {
        var closed = await AbandonAsync(reason: "seed");
        var run = Id(closed.RunId);
        var reason = new string('a', MaximumReasonBytes + extraBytes);
        Assert.True(await ExecuteAsync(
            $"UPDATE runs SET AbandonmentReason = '{reason}' WHERE Id = '{run}'; "
            + $"UPDATE events SET PayloadJson = '{{\"reason\":\"{reason}\"}}' WHERE RunId = '{run}' AND EventType = 'run.abandoned'") > 0);

        var result = await InspectAsync(closed.RunId);

        Assert.Equal(recorded ? RunAbandonmentInspectionStatus.Recorded : RunAbandonmentInspectionStatus.Unavailable, result.Status);
        Assert.Equal(recorded ? reason : null, result.Abandonment?.Reason);
    }

    [Fact]
    public async Task A_multibyte_reason_is_bounded_by_its_utf8_bytes_and_not_by_its_characters()
    {
        var closed = await AbandonAsync(reason: "seed");
        var run = Id(closed.RunId);
        var reason = new string((char)0xE9, MaximumReasonBytes / 2 + 1);
        Assert.Equal(MaximumReasonBytes + 2, Encoding.UTF8.GetByteCount(reason));
        var escaped = string.Concat(Enumerable.Repeat("\\u00e9", reason.Length));
        Assert.True(await ExecuteAsync(
            $"UPDATE runs SET AbandonmentReason = '{reason}' WHERE Id = '{run}'; "
            + $"UPDATE events SET PayloadJson = '{{\"reason\":\"{escaped}\"}}' WHERE RunId = '{run}' AND EventType = 'run.abandoned'") > 0);

        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, (await InspectAsync(closed.RunId)).Status);
    }

    [Fact]
    public async Task A_reason_with_a_supplementary_plane_format_character_is_refused_as_non_canonical()
    {
        var closed = await AbandonAsync(reason: "seed");
        var run = Id(closed.RunId);
        var reason = "x" + char.ConvertFromUtf32(0xE0001) + "y";
        Assert.True(await ExecuteAsync(
            $"UPDATE runs SET AbandonmentReason = '{reason}' WHERE Id = '{run}'; "
            + $"UPDATE events SET PayloadJson = '{{\"reason\":\"{reason}\"}}' WHERE RunId = '{run}' AND EventType = 'run.abandoned'") > 0);

        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, (await InspectAsync(closed.RunId)).Status);
    }

    [Fact]
    public async Task The_stored_reason_is_never_trimmed_normalized_or_repaired_to_become_recorded()
    {
        var closed = await AbandonAsync(reason: "seed");
        var run = Id(closed.RunId);
        Assert.True(await ExecuteAsync(
            $"UPDATE runs SET AbandonmentReason = 'tail ' WHERE Id = '{run}'; "
            + $"UPDATE events SET PayloadJson = '{{\"reason\":\"tail \"}}' WHERE RunId = '{run}' AND EventType = 'run.abandoned'") > 0);

        Assert.Equal(RunAbandonmentInspectionStatus.Unavailable, (await InspectAsync(closed.RunId)).Status);
        await using var dbContext = Fixture.CreateContext();
        Assert.Equal(
            "tail ",
            await dbContext.Database.SqlQuery<string>($"SELECT AbandonmentReason AS Value FROM runs WHERE Id = {run}").SingleAsync());
    }
}
