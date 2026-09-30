using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Owner-side and raw-storage helpers for the run-scoped Claude mutation turn-limit request,
/// each through its own context as a separate request or a corrupted row would appear.</summary>
public static class ClaudeMutationTurnLimitTestSupport
{
    public static async Task SetLimitAsync(SqliteDatabaseFixture fixture, Guid runId, int? maxTurns)
    {
        await using var context = fixture.CreateContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
        run.SetRequestedClaudeMaxTurns(maxTurns);
        await context.SaveChangesAsync();
    }

    /// <summary>Writes the run's stored limit with raw SQL, bypassing every Domain validation, to
    /// simulate a corrupted or out-of-band row.</summary>
    public static async Task SetRawRunLimitAsync(SqliteDatabaseFixture fixture, Guid runId, object? maxTurns)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE runs SET RequestedClaudeMaxTurns = {maxTurns} WHERE Id = {runId}");
    }

    /// <summary>Writes an attempt's stored limit with raw SQL, bypassing every Domain validation.</summary>
    public static async Task SetRawAttemptLimitAsync(SqliteDatabaseFixture fixture, Guid attemptId, object? maxTurns)
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentRequestedMaxTurns = {maxTurns} WHERE Id = {attemptId}");
    }

    /// <summary>The text a raw-stored test value has when SQLite decodes it (a BLOB decodes as its UTF-8 bytes).</summary>
    public static string StoredText(object stored) => stored is double.PositiveInfinity ? "Inf" : stored is byte[] bytes
        ? System.Text.Encoding.UTF8.GetString(bytes)
        : Convert.ToString(stored, System.Globalization.CultureInfo.InvariantCulture)!;

    /// <summary>SQLite's storage class of the stored run value (<c>integer</c>, <c>real</c>, <c>text</c>, <c>blob</c>, <c>null</c>).</summary>
    public static async Task<string> ReadRunStorageClassAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var context = fixture.CreateContext();
        return await context.Database
            .SqlQuery<string>($"SELECT typeof(RequestedClaudeMaxTurns) AS Value FROM runs WHERE Id = {runId}")
            .SingleAsync();
    }

    /// <summary>SQLite's storage class of the stored attempt value.</summary>
    public static async Task<string> ReadAttemptStorageClassAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        await using var context = fixture.CreateContext();
        return await context.Database
            .SqlQuery<string>($"SELECT typeof(AgentRequestedMaxTurns) AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    /// <summary>The stored run value exactly as SQLite holds it (text view), independent of any mapping.</summary>
    public static async Task<string?> ReadRunRawAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var context = fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT CAST(RequestedClaudeMaxTurns AS TEXT) AS Value FROM runs WHERE Id = {runId}")
            .SingleAsync();
    }

    /// <summary>The stored attempt value exactly as SQLite holds it (text view), independent of any mapping.</summary>
    public static async Task<string?> ReadAttemptRawAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        await using var context = fixture.CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT CAST(AgentRequestedMaxTurns AS TEXT) AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    /// <summary>The stored run request when it is valid (or none); throws for a malformed stored value.</summary>
    public static async Task<int?> ReadRunLimitAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        var reading = ClaudeMutationTurnLimit.Read(await ReadRunRawAsync(fixture, runId));
        return reading.IsMalformed ? throw new InvalidOperationException("The stored run request is malformed.") : reading.Value;
    }

    /// <summary>The stored attempt snapshot when it is valid (or none); throws for a malformed stored value.</summary>
    public static async Task<int?> ReadAttemptLimitAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        var reading = ClaudeMutationTurnLimit.Read(await ReadAttemptRawAsync(fixture, attemptId));
        return reading.IsMalformed ? throw new InvalidOperationException("The stored attempt snapshot is malformed.") : reading.Value;
    }

    public static async Task<string?> ReadAttemptContractVersionAsync(SqliteDatabaseFixture fixture, Guid attemptId)
    {
        await using var context = fixture.CreateContext();
        return await context.Attempts.AsNoTracking()
            .Where(candidate => candidate.Id == attemptId)
            .Select(candidate => candidate.AgentAdapterContractVersion)
            .SingleAsync();
    }

    public static async Task<long> CountEventsAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var context = fixture.CreateContext();
        return await context.Events.CountAsync(candidate => candidate.RunId == runId);
    }
}
