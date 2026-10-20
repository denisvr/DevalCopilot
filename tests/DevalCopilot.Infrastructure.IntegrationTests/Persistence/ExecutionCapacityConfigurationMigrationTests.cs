using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0059 persistence against the real file-backed SQLite schema and production migrations: the additive upgrade creates one table and
/// seeds exactly revision one with limit two while preserving every existing row, a new database has the same seed, the reverse drops only
/// that table, the real constraints refuse a revision below one, a duplicate revision and any limit other than the integers one and two
/// (including real, text and blob values), appended revisions survive a reopen in order, and the table has no trigger or foreign key.
/// </summary>
public sealed class ExecutionCapacityConfigurationMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261019180310_AddAgentSupervision";
    private const string Table = "execution_capacity_configurations";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-capacity-configuration-migration-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private async Task MigrateToAsync(string? target)
    {
        await using var context = CreateContext();
        await context.Database.GetService<IMigrator>().MigrateAsync(target);
    }

    private SqliteConnection OpenProbe()
    {
        var probe = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False");
        probe.Open();
        return probe;
    }

    private async Task<List<string>> ReadNamesAsync(string type, string like)
    {
        await using var probe = OpenProbe();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name LIKE $like ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$like", like);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<string> HistoryAsync()
    {
        await using var probe = OpenProbe();
        await using var command = probe.CreateCommand();
        command.CommandText = $"SELECT Revision, \"Limit\" FROM {Table} ORDER BY Revision;";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add($"{reader.GetInt64(0)}:{reader.GetInt64(1)}");
        }

        return string.Join(',', rows);
    }

    private async Task InsertAsync(object revision, object limit)
    {
        await using var probe = OpenProbe();
        await using var command = probe.CreateCommand();
        command.CommandText = $"INSERT INTO {Table} (Revision, \"Limit\") VALUES ($revision, $limit);";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", limit);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(Guid ProjectId, Guid RunId, Guid AttemptId)> SeedHistoricalRowsAsync()
    {
        await using var context = CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, 1, RunExecutionMode.ManualAgent, "Objective", Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        return (project.Id, run.Id, attempt.Id);
    }

    [Fact]
    public async Task The_upgrade_adds_one_table_seeds_revision_one_with_limit_two_and_preserves_every_existing_row()
    {
        await MigrateToAsync(PriorMigration);
        var (projectId, runId, attemptId) = await SeedHistoricalRowsAsync();
        Assert.Empty(await ReadNamesAsync("table", Table));

        await MigrateToAsync(null);

        Assert.Equal([Table], await ReadNamesAsync("table", Table));
        Assert.Equal("1:2", await HistoryAsync());
        await using var verify = CreateContext();
        Assert.Equal(projectId, (await verify.Projects.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(runId, (await verify.Runs.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(attemptId, (await verify.Attempts.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task A_new_database_has_the_same_seed_and_no_other_revision()
    {
        await MigrateToAsync(null);

        Assert.Equal("1:2", await HistoryAsync());
        Assert.Empty(await ReadNamesAsync("trigger", "%capacity_configuration%"));
        await using var probe = OpenProbe();
        await using var command = probe.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_foreign_key_list('{Table}');";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_reverse_drops_only_the_new_table_and_leaves_every_other_row_and_object()
    {
        await MigrateToAsync(null);
        var (projectId, runId, attemptId) = await SeedHistoricalRowsAsync();
        var tablesBefore = await ReadNamesAsync("table", "%");

        await MigrateToAsync(PriorMigration);

        var tablesAfter = await ReadNamesAsync("table", "%");
        Assert.Equal(tablesBefore.Where(name => name != Table).ToList(), tablesAfter);
        await using var verify = CreateContext();
        Assert.Equal(projectId, (await verify.Projects.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(runId, (await verify.Runs.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(attemptId, (await verify.Attempts.AsNoTracking().SingleAsync()).Id);

        await MigrateToAsync(null);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Theory]
    [InlineData(0L, 2L)]
    [InlineData(-1L, 2L)]
    [InlineData(2L, 0L)]
    [InlineData(2L, 3L)]
    [InlineData(2L, -1L)]
    [InlineData(2L, 1.5)]
    [InlineData(2L, "abc")]
    [InlineData(2L, new byte[] { 0x31 })]
    public async Task The_real_constraints_refuse_a_revision_or_limit_outside_the_stored_shape(object revision, object limit)
    {
        await MigrateToAsync(null);

        var refusal = await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(revision, limit));

        Assert.Contains("CHECK constraint failed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_duplicate_revision_is_refused_and_the_existing_revision_is_never_replaced()
    {
        await MigrateToAsync(null);

        var refusal = await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(1L, 1L));

        Assert.Contains("UNIQUE constraint failed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task Appended_revisions_survive_a_reopen_in_order_and_the_latest_one_is_current()
    {
        await MigrateToAsync(null);
        await InsertAsync(2L, 1L);
        await InsertAsync(3L, 2L);
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));

        await using var reopened = CreateContext();
        var rows = await reopened.ExecutionCapacityConfigurations.AsNoTracking().OrderBy(row => row.Revision).ToListAsync();

        Assert.Equal(["1:2", "2:1", "3:2"], rows.Select(row => $"{row.Revision}:{row.Limit}"));
        Assert.Equal(3, rows[^1].Revision);
        Assert.Equal(2, rows[^1].Limit);
    }
}
