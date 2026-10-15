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
/// Proves <c>AddGitHubBranchPublicationApproval</c> (ADR-0048) is one additive table on a real file-backed SQLite database: runs of every
/// historical lifecycle stay without an approval and unchanged, no existing table, index or trigger changes (the events table is not rebuilt), the new
/// table has exactly the recorded columns (all required, no default, nothing backfilled), its five uniqueness indexes, six
/// restrictive foreign keys and two immutability triggers exist, and the reverse migration drops only that table and its triggers.
/// </summary>
public sealed class AddGitHubBranchPublicationApprovalMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261013174647_AddGitHubRepositoryIdentityBinding";

    private const string Table = "github_branch_publication_approvals";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-github-approval-{Guid.NewGuid():N}.db");

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
        [.. (await QueryAsync("SELECT type || ':' || name || ':' || coalesce(sql, '') FROM sqlite_master WHERE name <> '__EFMigrationsHistory' ORDER BY 1", 1)).Select(row => row[0])];

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
        var targetsBefore = await QueryAsync("SELECT * FROM github_observation_targets", 11);
        var bindingsBefore = await QueryAsync("SELECT * FROM github_repository_identity_bindings", 14);
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        var after = await SchemaObjectsAsync();
        Assert.Empty(before.Except(after));
        Assert.All(
            after.Except(before),
            added => Assert.True(
                added.Contains(Table, StringComparison.OrdinalIgnoreCase), $"{added} is not part of the new table"));
        Assert.Contains(after.Except(before), entry => entry.StartsWith("table:" + Table, StringComparison.Ordinal));
        Assert.Equal(runsBefore, await QueryAsync("SELECT * FROM runs ORDER BY ExecutionNumber", 16));
        Assert.Equal(targetsBefore, await QueryAsync("SELECT * FROM github_observation_targets", 11));
        Assert.Equal(bindingsBefore, await QueryAsync("SELECT * FROM github_repository_identity_bindings", 14));
        Assert.Empty(await QueryAsync($"SELECT * FROM {Table}", 1));
        await using var reopened = CreateContext();
        Assert.Empty(await reopened.GitHubBranchPublicationApprovals.AsNoTracking().ToListAsync());
        Assert.Equal(lifecycles.Length, await reopened.Runs.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task The_new_table_has_exactly_the_recorded_required_columns_with_no_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var columns = await QueryAsync($"SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info('{Table}') ORDER BY cid", 4);

        Assert.Equal(
            [
                "Id", "EventId", "RunId", "ProjectId", "LocalCommitOperationId", "GitWorkspaceId", "CommitSha", "BranchName", "TargetId",
                "TargetEventId", "TargetOwner", "TargetRepository", "TargetRecordedAtUtc", "BindingId", "BindingEventId",
                "RepositoryNodeId", "BindingOwner", "BindingRepository", "BindingObservedAtUtc", "BindingRecordedAtUtc", "ApprovedAtUtc",
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
    public async Task Run_operation_target_binding_and_event_are_each_unique_and_the_six_references_are_restrictive()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var unique = await QueryAsync(
            $"SELECT il.name, ii.name FROM pragma_index_list('{Table}') il JOIN pragma_index_info(il.name) ii WHERE il.\"unique\" = 1 AND il.origin = 'c' ORDER BY il.name",
            2);
        Assert.Equal(
            [
                ["ix_github_branch_publication_approvals_binding", "BindingId"],
                ["ix_github_branch_publication_approvals_event", "EventId"],
                ["ix_github_branch_publication_approvals_operation", "LocalCommitOperationId"],
                ["ix_github_branch_publication_approvals_run", "RunId"],
                ["ix_github_branch_publication_approvals_target", "TargetId"],
            ],
            unique);

        var references = await QueryAsync(
            $"SELECT \"from\", \"table\", \"to\", on_delete FROM pragma_foreign_key_list('{Table}') ORDER BY \"from\"", 4);
        Assert.Equal(
            [
                ["BindingId", "github_repository_identity_bindings", "Id", "RESTRICT"],
                ["GitWorkspaceId", "git_workspaces", "Id", "RESTRICT"],
                ["LocalCommitOperationId", "local_commit_operations", "Id", "RESTRICT"],
                ["ProjectId", "projects", "Id", "RESTRICT"],
                ["RunId", "runs", "Id", "RESTRICT"],
                ["TargetId", "github_observation_targets", "Id", "RESTRICT"],
            ],
            references);
    }

    [Fact]
    public async Task Exactly_two_triggers_make_the_table_immutable_and_nothing_else_has_one_on_it()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var triggers = await QueryAsync(
            $"SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = '{Table}' ORDER BY name", 2);

        Assert.Equal(
            ["trg_github_branch_publication_approvals_no_delete", "trg_github_branch_publication_approvals_no_update"],
            triggers.Select(trigger => trigger[0]));
        Assert.Contains("BEFORE DELETE", triggers[0][1], StringComparison.Ordinal);
        Assert.Contains("BEFORE UPDATE", triggers[1][1], StringComparison.Ordinal);
        Assert.All(triggers, trigger => Assert.Contains("RAISE(ABORT, 'github_branch_publication_approval.immutable')", trigger[1], StringComparison.Ordinal));
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
            await context.Database.MigrateAsync();
        }

        Assert.NotEqual(prior, await SchemaObjectsAsync());
        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        Assert.Equal(prior, await SchemaObjectsAsync());
    }
}
