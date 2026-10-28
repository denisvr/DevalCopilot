using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The exact INTEGER admission of the three stored numbers (ADR-0070): the pinned checkpoint number 1 to Int32.MaxValue (set on
/// the operation and on its named checkpoint together, so only the admission is tested), the changed-path count 1 to 128 and the total
/// bytes 0 to 4194304. The extrema are admitted exactly; zero or negative values, overflow, a truncated 64-bit value, REAL, TEXT and BLOB
/// storage are Unavailable and never clamped, defaulted or truncated.</summary>
public sealed class LocalCommitInspectionNumericTests : LocalCommitInspectionTestBase
{
    public static TheoryData<string, string, bool> Numbers => new()
    {
        { "checkpoint", "1", true },
        { "checkpoint", "2", true },
        { "checkpoint", "2147483646", true },
        { "checkpoint", "2147483647", true },
        { "checkpoint", "0", false },
        { "checkpoint", "-1", false },
        { "checkpoint", "-2147483647", false },
        { "checkpoint", "-2147483648", false },
        { "checkpoint", "2147483648", false },
        { "checkpoint", "4294967297", false },
        { "checkpoint", "9007199254740993", false },
        { "checkpoint", "9223372036854775807", false },
        { "checkpoint", "7.5", false },
        { "checkpoint", "1.5", false },
        { "checkpoint", "'x'", false },
        { "checkpoint", "''", false },
        { "checkpoint", "zeroblob(4)", false },
        { "checkpoint", "X'01'", false },
        { "paths", "1", true },
        { "paths", "64", true },
        { "paths", "128", true },
        { "paths", "0", false },
        { "paths", "-1", false },
        { "paths", "129", false },
        { "paths", "2147483647", false },
        { "paths", "4294967297", false },
        { "paths", "1.5", false },
        { "paths", "'x'", false },
        { "paths", "zeroblob(4)", false },
        { "bytes", "0", true },
        { "bytes", "1", true },
        { "bytes", "4194303", true },
        { "bytes", "4194304", true },
        { "bytes", "-1", false },
        { "bytes", "4194305", false },
        { "bytes", "2147483647", false },
        { "bytes", "9007199254740993", false },
        { "bytes", "9223372036854775807", false },
        { "bytes", "-9223372036854775808", false },
        { "bytes", "1.5", false },
        { "bytes", "4194303.5", false },
        { "bytes", "'x'", false },
        { "bytes", "zeroblob(8)", false },
    };

    [Theory]
    [MemberData(nameof(Numbers))]
    public async Task A_stored_number_is_admitted_exactly_or_the_operation_is_unavailable(string target, string literal, bool admitted)
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.Completed);
        var operation = Id(rows.Operation.Id);
        var checkpoint = Id(rows.Checkpoint.Id);
        var sql = target switch
        {
            "checkpoint" =>
                $"UPDATE local_commit_operations SET CheckpointNumber = {literal} WHERE Id = '{operation}'; "
                + $"UPDATE git_checkpoints SET CheckpointNumber = {literal} WHERE Id = '{checkpoint}';",
            "paths" => $"UPDATE local_commit_operations SET ChangedPathCount = {literal} WHERE Id = '{operation}'",
            _ => $"UPDATE local_commit_operations SET TotalBytes = {literal} WHERE Id = '{operation}'",
        };
        await ExecuteAsync(sql);

        var result = await InspectAsync(rows.Run.Id);

        if (!admitted)
        {
            Assert.Equal(LocalCommitInspectionStatus.Unavailable, result.Status);
            Assert.Null(result.Operation);
            return;
        }

        var recorded = Recorded(result);
        var expected = long.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(target == "checkpoint" ? expected : rows.Checkpoint.CheckpointNumber, recorded.CheckpointNumber);
        Assert.Equal(target == "paths" ? expected : 1, recorded.ChangedPathCount);
        Assert.Equal(target == "bytes" ? expected : 10, recorded.TotalBytes);
    }
}
