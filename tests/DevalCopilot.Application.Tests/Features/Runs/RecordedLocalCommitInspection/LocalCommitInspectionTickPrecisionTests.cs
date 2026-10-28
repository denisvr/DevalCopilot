using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>Stored times are admitted and ordered at the provider writer's full tick precision (ADR-0070): a one-tick difference decides
/// the status/time tuple, equal instants are admitted, the writer's trimmed fraction (no trailing zeros, no fraction for a whole second)
/// is read exactly, and the instants come back with their stored ticks, never rounded to milliseconds or seconds.</summary>
public sealed class LocalCommitInspectionTickPrecisionTests : LocalCommitInspectionTestBase
{
    private const string Operations = "local_commit_operations";

    public static TheoryData<string, string, string, bool> Completed => new()
    {
        { "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00.0000001+00:00", true },
        { "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00.0000002+00:00", "2026-10-06 12:00:00.0000003+00:00", true },
        { "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00+00:00", "2026-10-06 12:00:00.0000003+00:00", false },
        { "2026-10-06 12:00:00.0000002+00:00", "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00.0000003+00:00", false },
        { "2026-10-06 12:00:00.0000001+00:00", "2026-10-06 12:00:00.0000003+00:00", "2026-10-06 12:00:00.0000002+00:00", false },
        { "2026-10-06 12:00:00.9999999+00:00", "2026-10-06 12:00:01+00:00", "2026-10-06 12:00:01.0000001+00:00", true },
        { "2026-10-06 12:00:00.5+00:00", "2026-10-06 12:00:00.5000001+00:00", "2026-10-06 12:00:01+00:00", true },
        { "2026-10-06 12:00:00.5000001+00:00", "2026-10-06 12:00:00.5+00:00", "2026-10-06 12:00:01+00:00", false },
    };

    [Theory]
    [MemberData(nameof(Completed))]
    public async Task A_completed_tuple_is_ordered_by_ticks_and_returns_the_stored_instants(
        string created, string started, string completed, bool admitted)
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        Assert.Equal(1, await ExecuteAsync(
            $"UPDATE {Operations} SET CreatedAtUtc = '{created}', ExecutionStartedAtUtc = '{started}', CompletedAtUtc = '{completed}' "
            + $"WHERE Id = '{Id(rows.Operation.Id)}'"));

        var result = await InspectAsync(rows.Run.Id);

        if (!admitted)
        {
            Assert.Equal(LocalCommitInspectionStatus.Unavailable, result.Status);
            Assert.Null(result.Operation);
            return;
        }

        var recorded = Recorded(result);
        Assert.Equal(Ticks(created), recorded.CreatedAtUtc.UtcTicks);
        Assert.Equal(Ticks(started), recorded.ExecutionStartedAtUtc!.Value.UtcTicks);
        Assert.Equal(Ticks(completed), recorded.CompletedAtUtc!.Value.UtcTicks);
    }

    [Theory]
    [InlineData("2026-10-06 12:00:00.0000002+00:00", "2026-10-06 12:00:00.0000001+00:00", false)]
    [InlineData("2026-10-06 12:00:00.0000002+00:00", "2026-10-06 12:00:00.0000002+00:00", true)]
    [InlineData("2026-10-06 12:00:00.0000002+00:00", "2026-10-06 12:00:00.0000003+00:00", true)]
    public async Task An_outcome_recorded_without_a_marker_is_ordered_against_its_creation_by_ticks(string created, string completed, bool admitted)
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.FailedUnstarted);
        Assert.Equal(1, await ExecuteAsync(
            $"UPDATE {Operations} SET CreatedAtUtc = '{created}', CompletedAtUtc = '{completed}' WHERE Id = '{Id(rows.Operation.Id)}'"));

        var result = await InspectAsync(rows.Run.Id);

        if (!admitted)
        {
            Assert.Equal(LocalCommitInspectionStatus.Unavailable, result.Status);
            return;
        }

        var recorded = Recorded(result);
        Assert.Null(recorded.ExecutionStartedAtUtc);
        Assert.Equal(Ticks(completed), recorded.CompletedAtUtc!.Value.UtcTicks);
    }

    private static long Ticks(string stored) =>
        DateTimeOffset.Parse(stored, System.Globalization.CultureInfo.InvariantCulture).UtcTicks;
}
