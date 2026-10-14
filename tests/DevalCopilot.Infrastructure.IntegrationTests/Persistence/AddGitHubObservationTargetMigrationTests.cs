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
/// Proves <c>AddGitHubObservationTarget</c> (ADR-0040) is one additive table on a real file-backed SQLite database: runs of every
/// historical lifecycle stay unbound and unchanged, no existing table, index or trigger changes, the new table has exactly the
/// recorded columns (all required, no default, nothing backfilled), its three uniqueness indexes and four restrictive foreign keys
/// exist, and the reverse migration drops only that table.
/// </summary>
public sealed class AddGitHubObservationTargetMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261008192030_AddRunAbandonment";

    private const string OwnMigration = "20261012202042_AddGitHubObservationTarget";

    private const string Table = "github_observation_targets";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-github-target-{Guid.NewGuid():N}.db");

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

    private async Task<List<string[]>> QueryAsync(string sql, int columns)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants.
        command.CommandText = sql;
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            rows.Add(Enumerable.Range(0, columns).Select(index => reader.IsDBNull(index) ? "<null>" : reader.GetValue(index).ToString()!).ToArray());
        }

        return rows;
    }

    private async Task<string[]> SchemaObjectsAsync() =>
        [.. (await QueryAsync("SELECT type || ':' || name FROM sqlite_master WHERE name <> '__EFMigrationsHistory' ORDER BY 1", 1)).Select(row => row[0])];

    [Fact]
    public async Task Historical_runs_stay_unbound_and_unchanged_and_nothing_else_in_the_schema_changes()
    {
        var projectId = Guid.NewGuid();
        var lifecycles = new[] { "Created", "Running", "Completed", "Failed", "Interrupted", "Abandoned" };
        var runIds = lifecycles.Select(_ => Guid.NewGuid()).ToArray();
        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            for (var index = 0; index < lifecycles.Length; index++)
            {
                await previous.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO runs
                          (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                           CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, ExecutionMode)
                      VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {7}, {8}, {9})",
                    runIds[index], projectId, index + 1, "Historical " + lifecycles[index], lifecycles[index], nameof(RunStage.Plan),
                    nameof(ParticipantKind.None), Now, 12.5, 2);
            }
        }

        var before = await SchemaObjectsAsync();
        var runsBefore = await QueryAsync("SELECT * FROM runs ORDER BY ExecutionNumber", 16);
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.GetService<IMigrator>().MigrateAsync(OwnMigration);
        }

        var after = await SchemaObjectsAsync();
        Assert.Empty(before.Except(after));
        Assert.All(
            after.Except(before),
            added => Assert.True(
                added.Contains(Table, StringComparison.OrdinalIgnoreCase), $"{added} is not part of the new table"));
        Assert.Contains("table:" + Table, after.Except(before));
        Assert.Equal(runsBefore, await QueryAsync("SELECT * FROM runs ORDER BY ExecutionNumber", 16));
        Assert.Empty(await QueryAsync($"SELECT * FROM {Table}", 1));
        await using var reopened = CreateContext();
        Assert.Empty(await reopened.GitHubObservationTargets.AsNoTracking().ToListAsync());
        Assert.Equal(lifecycles.Length, await reopened.Runs.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task The_new_table_has_exactly_the_recorded_required_columns_with_no_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(OwnMigration);
        }

        var columns = await QueryAsync($"SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info('{Table}') ORDER BY cid", 4);

        Assert.Equal(
            [
                "Id", "RunId", "ProjectId", "LocalCommitOperationId", "GitWorkspaceId", "CommitSha", "BranchName", "Owner", "Repository",
                "RecordedAtUtc", "EventId",
            ],
            columns.Select(column => column[0]));
        Assert.All(columns, column =>
        {
            Assert.Equal("TEXT", column[1]);
            Assert.Equal("1", column[2]);
            Assert.Equal("<null>", column[3]);
        });
    }

    [Fact]
    public async Task Run_operation_and_event_are_each_unique_and_the_four_references_are_restrictive()
    {
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(OwnMigration);
        }

        var unique = await QueryAsync(
            $"SELECT il.name, ii.name FROM pragma_index_list('{Table}') il JOIN pragma_index_info(il.name) ii WHERE il.\"unique\" = 1 AND il.origin = 'c' ORDER BY il.name",
            2);
        Assert.Equal(
            [
                ["ix_github_observation_targets_event", "EventId"],
                ["ix_github_observation_targets_operation", "LocalCommitOperationId"],
                ["ix_github_observation_targets_run", "RunId"],
            ],
            unique);

        var references = await QueryAsync(
            $"SELECT \"from\", \"table\", \"to\", on_delete FROM pragma_foreign_key_list('{Table}') ORDER BY \"from\"", 4);
        Assert.Equal(
            [
                ["GitWorkspaceId", "git_workspaces", "Id", "RESTRICT"],
                ["LocalCommitOperationId", "local_commit_operations", "Id", "RESTRICT"],
                ["ProjectId", "projects", "Id", "RESTRICT"],
                ["RunId", "runs", "Id", "RESTRICT"],
            ],
            references);
    }

    [Fact]
    public async Task The_reverse_migration_drops_only_the_new_table()
    {
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var prior = await SchemaObjectsAsync();
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(OwnMigration);
        }

        Assert.NotEqual(prior, await SchemaObjectsAsync());
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        Assert.Equal(prior, await SchemaObjectsAsync());
    }
}
