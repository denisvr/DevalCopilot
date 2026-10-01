using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Test-only access to the stored execution-mode value. Production has no setter, conversion, or
/// rewrite of a Run mode, so a test that needs a different stored value (a historical row, a malformed value, or a
/// competing change) writes the column directly.</summary>
internal static class RunExecutionModeTestSupport
{
    /// <summary>A number that is not a member of the enum.</summary>
    public const int UndefinedMode = 7;

    /// <summary>Malformed stored values as SQLite literals: a REAL that truncates to a valid mode, integers that
    /// overflow 32 bits (one whose low bits are a valid mode), TEXT that is not a canonical integer, and BLOBs.
    /// TEXT that SQLite numeric conversion accepts (such as '2', ' 2', or '2.0') is deliberately absent: the INTEGER column affinity stores it as the integer 2.</summary>
    public static readonly string[] MalformedLiterals =
    [
        "2.5", "1.0000001", "4294967296", "4294967298", "9223372036854775807", "-1", "'two'", "'2x'", "''",
        "X'02'", "X'32'", "X''",
    ];

    public static Task SetStoredModeAsync(DevalCopilotDbContext dbContext, Guid runId, int mode) =>
        SetStoredRawAsync(dbContext, runId, mode.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Writes an exact SQLite literal into the column, bypassing the model. The literal is test-owned input.</summary>
#pragma warning disable EF1003
    public static Task SetStoredRawAsync(DevalCopilotDbContext dbContext, Guid runId, string sqlLiteral) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE runs SET ExecutionMode = " + sqlLiteral + " WHERE Id = {0}", runId);
#pragma warning restore EF1003

    public static async Task SetStoredRawAsync(SqliteDatabaseFixture fixture, Guid runId, string sqlLiteral)
    {
        await using var other = fixture.CreateContext();
        await SetStoredRawAsync(other, runId, sqlLiteral);
    }

    /// <summary>Writes through a separate, independent context: a change committed by another transaction.</summary>
    public static async Task SetStoredModeAsync(SqliteDatabaseFixture fixture, Guid runId, int mode)
    {
        await using var other = fixture.CreateContext();
        await SetStoredModeAsync(other, runId, mode);
    }

    public static async Task<int> ReadStoredModeAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var other = fixture.CreateContext();
        return (int)await other.Database
            .SqlQuery<long>($"SELECT CAST(ExecutionMode AS INTEGER) AS Value FROM runs WHERE Id = {runId}")
            .SingleAsync();
    }

    /// <summary>The SQLite storage class and a text rendering of the stored value, read without the model.</summary>
    public static async Task<(string StorageClass, string Text)> ReadStoredRawAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var other = fixture.CreateContext();
        var row = await other.Database
            .SqlQuery<RawStored>($"SELECT typeof(ExecutionMode) AS StorageClass, CAST(ExecutionMode AS TEXT) AS Text FROM runs WHERE Id = {runId}")
            .SingleAsync();
        return (row.StorageClass, row.Text);
    }

    private sealed record RawStored(string StorageClass, string Text);
}
