using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Configurations.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The type-preserving representation of the two turn-limit columns through real SQLite: every non-integer storage
/// class is encoded disjointly (an actual TEXT that resembles an internal marker is still TEXT, an empty or non-hex
/// suffix never throws, a REAL is bit-exact including infinities, a BLOB keeps its exact bytes), and the original value
/// is bound back with its actual type and content. The direct SQLite view (storage class and content) is asserted after
/// an unrelated Run save, after a successful set that repairs the row, and a genuine concurrent change is still refused
/// while an identical out-of-band value is not reported as a change.
/// </summary>
public sealed class ExactStoredIntegerTextStorageTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-exact-stored-integer-{Guid.NewGuid():N}.db");

    /// <summary>A SQLite literal and the storage class it must produce; none of these is a valid request.</summary>
    public static IEnumerable<object[]> Malformed =>
    [
        ["'blob:37'", "text"],
        ["'blob:'", "text"],
        ["'blob:ZZ'", "text"],
        ["'blob:0g'", "text"],
        ["'b:37'", "text"],
        ["'t:7'", "text"],
        ["'r:0000000000000000'", "text"],
        ["'?:x'", "text"],
        ["''", "text"],
        ["'abc'", "text"],
        ["X'37'", "blob"],
        ["X''", "blob"],
        ["X'00FF10'", "blob"],
        ["X'626C6F623A3337'", "blob"],
        ["3.5", "real"],
        ["1.0000000000000002", "real"],
        ["0.1", "real"],
        ["-0.0", "integer"], // a whole-valued REAL is converted to INTEGER by the column affinity on write
        ["9e999", "real"],
        ["-9e999", "real"],
        ["0", "integer"],
        ["-3", "integer"],
        ["101", "integer"],
        ["4294967297", "integer"],
    ];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    /// <summary>Writes a SQLite literal (test-controlled text such as <c>X'37'</c> or <c>9e999</c>) into the stored column.</summary>
    private static Task<int> SetLiteralAsync(DevalCopilotDbContext context, string literal, Guid runId)
    {
        var sql = string.Concat(
            "UPDATE runs SET RequestedClaudeMaxTurns = ", literal, " WHERE Id = '", runId.ToString().ToUpperInvariant(), "'");
        return context.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<Guid> SeedRunAsync(string literal)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        context.Projects.Add(Project.Register(projectId, "Exact storage", $@"C:\repos\{Guid.NewGuid():N}", Now));
        context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
        await context.SaveChangesAsync();
        await SetLiteralAsync(context, literal, runId);
        return runId;
    }

    /// <summary>The value exactly as SQLite holds it: its storage class and its content with no decoding.</summary>
    private async Task<(string Class, object? Value)> RawAsync(Guid runId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(RequestedClaudeMaxTurns), RequestedClaudeMaxTurns FROM runs WHERE Id = $id";
        command.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetValue(1));
    }

    private static void AssertSameRaw((string Class, object? Value) expected, (string Class, object? Value) actual)
    {
        Assert.Equal(expected.Class, actual.Class);
        switch (expected.Value)
        {
            case byte[] bytes:
                Assert.Equal(bytes, Assert.IsType<byte[]>(actual.Value));
                break;
            case double number:
                Assert.Equal(BitConverter.DoubleToInt64Bits(number), BitConverter.DoubleToInt64Bits(Assert.IsType<double>(actual.Value)));
                break;
            default:
                Assert.Equal(expected.Value, actual.Value);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task A_row_holds_its_declared_storage_class_and_reads_as_malformed_never_as_a_request(string literal, string storageClass)
    {
        var runId = await SeedRunAsync(literal);

        Assert.Equal(storageClass, (await RawAsync(runId)).Class);
        await using var context = CreateContext();
        var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.True(run.ReadRequestedClaudeMaxTurns().IsMalformed);
        Assert.Null(run.ReadRequestedClaudeMaxTurns().Value);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task An_unrelated_run_save_leaves_the_exact_stored_class_and_content_untouched(string literal, string storageClass)
    {
        var runId = await SeedRunAsync(literal);
        var before = await RawAsync(runId);

        await using (var context = CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.Claim(Now);
            await context.SaveChangesAsync();
        }

        Assert.Equal(storageClass, before.Class);
        AssertSameRaw(before, await RawAsync(runId));
        await using var verify = CreateContext();
        Assert.Equal(RunLifecycle.Running, (await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId)).Lifecycle);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Setting_a_valid_request_repairs_the_row_to_a_true_sqlite_integer(string literal, string storageClass)
    {
        var runId = await SeedRunAsync(literal);
        Assert.Equal(storageClass, (await RawAsync(runId)).Class);

        await using (var context = CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            Assert.True(run.ReadRequestedClaudeMaxTurns().IsMalformed);
            run.SetRequestedClaudeMaxTurns(15);
            context.Entry(run).Property(Run.RequestedClaudeMaxTurnsStorageProperty).IsModified = true;
            await context.SaveChangesAsync();
        }

        var after = await RawAsync(runId);
        Assert.Equal("integer", after.Class);
        Assert.Equal(15L, after.Value);
        await using var verify = CreateContext();
        Assert.Equal(15, (await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId)).RequestedClaudeMaxTurns);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Clearing_repairs_the_row_to_a_true_sqlite_null(string literal, string storageClass)
    {
        var runId = await SeedRunAsync(literal);
        Assert.Equal(storageClass, (await RawAsync(runId)).Class);

        await using (var context = CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeMaxTurns(null);
            context.Entry(run).Property(Run.RequestedClaudeMaxTurnsStorageProperty).IsModified = true;
            await context.SaveChangesAsync();
        }

        Assert.Equal("null", (await RawAsync(runId)).Class);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task A_genuine_concurrent_change_is_refused_but_an_identical_out_of_band_value_is_not(string literal, string storageClass)
    {
        var runId = await SeedRunAsync(literal);
        Assert.Equal(storageClass, (await RawAsync(runId)).Class);

        await using (var identical = CreateContext())
        {
            var run = await identical.Runs.SingleAsync(candidate => candidate.Id == runId);
            await using (var other = CreateContext())
            {
                await SetLiteralAsync(other, literal, runId);
            }

            run.Claim(Now);
            await identical.SaveChangesAsync();
        }

        await using (var changed = CreateContext())
        {
            var run = await changed.Runs.SingleAsync(candidate => candidate.Id == runId);
            await using (var other = CreateContext())
            {
                await SetLiteralAsync(other, "99", runId);
            }

            run.Complete(Now.AddMinutes(1));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => changed.SaveChangesAsync());
        }

        Assert.Equal(99L, (await RawAsync(runId)).Value);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task An_attempt_snapshot_keeps_its_exact_class_and_content_reads_as_malformed_and_survives_an_unrelated_save(string literal, string storageClass)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Attempt storage", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
            context.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 9));
            await context.SaveChangesAsync();
            var sql = string.Concat("UPDATE attempts SET AgentRequestedMaxTurns = ", literal, " WHERE Id = '", attemptId.ToString().ToUpperInvariant(), "'");
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        var before = await RawAttemptAsync(attemptId);
        Assert.Equal(storageClass, before.Class);
        await using (var reader = CreateContext())
        {
            var attempt = await reader.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
            Assert.True(attempt.ReadAgentRequestedMaxTurns().IsMalformed);
            Assert.Null(attempt.GetAssignmentSnapshot());
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, attempt.GetMutationTurnLimitEvidence());
        }

        await using (var saver = CreateContext())
        {
            var attempt = await saver.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
            attempt.MarkAgentDispatched(Now);
            await saver.SaveChangesAsync();
        }

        AssertSameRaw(before, await RawAttemptAsync(attemptId));
    }

    private async Task<(string Class, object? Value)> RawAttemptAsync(Guid attemptId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(AgentRequestedMaxTurns), AgentRequestedMaxTurns FROM attempts WHERE Id = $id";
        command.Parameters.AddWithValue("$id", attemptId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetValue(1));
    }

    [Fact]
    public async Task Valid_integers_and_null_keep_their_exact_storage_class_and_round_trip()
    {
        var runId = await SeedRunAsync("7");
        Assert.Equal(("integer", (object?)7L), await RawAsync(runId));
        await using (var context = CreateContext())
        {
            var run = await context.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
            Assert.False(run.ReadRequestedClaudeMaxTurns().IsMalformed);
            Assert.Equal(7, run.RequestedClaudeMaxTurns);
        }

        await using (var context = CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeMaxTurns(100);
            await context.SaveChangesAsync();
        }

        Assert.Equal(("integer", (object?)100L), await RawAsync(runId));
        await using (var context = CreateContext())
        {
            var run = await context.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.SetRequestedClaudeMaxTurns(null);
            context.Entry(run).Property(Run.RequestedClaudeMaxTurnsStorageProperty).IsModified = true;
            await context.SaveChangesAsync();
        }

        Assert.Equal("null", (await RawAsync(runId)).Class);
    }

    [Theory]
    [InlineData("7", "7")]
    [InlineData("3.5", "r:400C000000000000")]
    [InlineData("'blob:37'", "t:blob:37")]
    [InlineData("'blob:'", "t:blob:")]
    [InlineData("''", "t:")]
    [InlineData("X'37'", "b:37")]
    [InlineData("X''", "b:")]
    [InlineData("9e999", "r:7FF0000000000000")]
    [InlineData("-9e999", "r:FFF0000000000000")]
    public async Task The_representation_is_disjoint_and_typed_by_storage_class(string literal, string expectedRepresentation)
    {
        var runId = await SeedRunAsync(literal);

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RequestedClaudeMaxTurns FROM runs WHERE Id = $id";
        command.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(expectedRepresentation, ExactStoredIntegerTextTypeMapping.Read(reader, 0));
    }

    [Fact]
    public void Sql_literals_match_the_parameter_binding()
    {
        var mapping = new ExactStoredIntegerTextTypeMapping();

        Assert.Equal("7", mapping.GenerateSqlLiteral("7"));
        Assert.Equal("X'37'", mapping.GenerateSqlLiteral("b:37"));
        Assert.Equal("X''", mapping.GenerateSqlLiteral("b:"));
        Assert.Equal("'blob:37'", mapping.GenerateSqlLiteral("t:blob:37"));
        Assert.Equal("'it''s'", mapping.GenerateSqlLiteral("t:it's"));
        Assert.Equal("9e999", mapping.GenerateSqlLiteral("r:7FF0000000000000"));
        Assert.Equal("-9e999", mapping.GenerateSqlLiteral("r:FFF0000000000000"));
        Assert.Equal("3.5", mapping.GenerateSqlLiteral("r:400C000000000000"));
        Assert.Equal("NULL", mapping.GenerateSqlLiteral(null));
    }
}
