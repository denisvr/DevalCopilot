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
/// Proves <c>AddCodexAccountUsageWarning</c> (ADR-0026) is truthful on a real file-backed SQLite database: it only adds one nullable
/// INTEGER column to runs, with no default and no backfill, so every run that predates it keeps <see langword="null"/>; values
/// round-trip through the real EF model in their exact storage class; the column is deliberately not a concurrency token; a tampered
/// stored value reads as malformed beside healthy siblings, survives unrelated saves exactly, and is repaired by a valid set or
/// clear; and the reverse migration drops exactly that column.
/// </summary>
public sealed class AddCodexAccountUsageWarningMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261004222517_AddCodexAccountUsageStop";

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-account-usage-warning-{Guid.NewGuid():N}.db");

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

    private async Task<IReadOnlyList<(string Name, string Type, bool NotNull, bool HasDefault)>> ReadColumnsAsync(string table)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info($table);";
        command.Parameters.AddWithValue("$table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<(string, string, bool, bool)>();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0, !reader.IsDBNull(3)));
        }

        return columns;
    }

    private async Task<Guid> SeedRunAsync(Guid projectId, int number, int? warning, int? stop = null, bool claim = true)
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        if (!await context.Projects.AnyAsync(candidate => candidate.Id == projectId))
        {
            context.Projects.Add(Project.Register(projectId, "Project", $@"C:\repos\{Guid.NewGuid():N}", Now));
        }

        var run = Run.RecordIntent(Guid.NewGuid(), projectId, number, "Objective", Now);
        if (claim)
        {
            run.Claim(Now);
        }

        run.SetCodexAccountUsageWarningPercent(warning);
        run.SetCodexAccountUsageStopPercent(stop);
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private async Task StoreAsync(Guid runId, object? stored)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageWarningPercent = {stored} WHERE Id = {runId}");
    }

    [Fact]
    public async Task Runs_that_predate_the_migration_keep_null_and_their_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, RequestedClaudeModel, RequestedClaudeEffort,
                        CodexAccountUsageStopPercent)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical run"}, {nameof(RunLifecycle.Running)}, {nameof(RunStage.Execute)},
                        {nameof(ParticipantKind.None)}, {Now}, {Now}, {0d}, {"opus"}, {"high"}, {80})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.True(run.ReadCodexAccountUsageWarningPercent().IsAbsent);
        Assert.Equal("opus", run.RequestedClaudeModel);
        Assert.Equal(80, run.ReadCodexAccountUsageStopPercent().Value);
        Assert.Equal("null", await reopened.Database
            .SqlQuery<string>($"SELECT typeof(CodexAccountUsageWarningPercent) AS Value FROM runs WHERE Id = {runId}").SingleAsync());
    }

    [Fact]
    public async Task The_new_column_is_nullable_with_the_expected_type_and_no_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var run = Assert.Single(await ReadColumnsAsync("runs"), column => column.Name == "CodexAccountUsageWarningPercent");
        Assert.Equal(("INTEGER", false, false), (run.Type, run.NotNull, run.HasDefault));
    }

    [Fact]
    public async Task The_model_maps_exact_stored_text_and_the_advisory_value_is_not_a_concurrency_token()
    {
        await using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(Run))!.FindProperty(Run.CodexAccountUsageWarningStorageProperty)!;

        Assert.False(property.IsConcurrencyToken);
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(string), property.ClrType);
        Assert.Equal("CodexAccountUsageWarningPercent", property.GetColumnName());
        Assert.Equal("INTEGER", property.GetColumnType());
        var stop = context.Model.FindEntityType(typeof(Run))!.FindProperty(Run.CodexAccountUsageStopStorageProperty)!;
        Assert.True(stop.IsConcurrencyToken);
    }

    [Fact]
    public async Task A_valid_value_round_trips_and_is_stored_as_a_sqlite_integer_and_a_clear_stores_null()
    {
        var projectId = Guid.NewGuid();
        var configured = await SeedRunAsync(projectId, 1, 80);
        var plain = await SeedRunAsync(projectId, 2, null);

        await using var reopened = CreateContext();
        Assert.Equal(80, (await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == configured)).ReadCodexAccountUsageWarningPercent().Value);
        Assert.True((await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == plain)).ReadCodexAccountUsageWarningPercent().IsAbsent);
        Assert.Equal("integer", await reopened.Database
            .SqlQuery<string>($"SELECT typeof(CodexAccountUsageWarningPercent) AS Value FROM runs WHERE Id = {configured}").SingleAsync());
        Assert.Equal("null", await reopened.Database
            .SqlQuery<string>($"SELECT typeof(CodexAccountUsageWarningPercent) AS Value FROM runs WHERE Id = {plain}").SingleAsync());
    }

    public static IEnumerable<object[]> StorageClassCases =>
        [[new byte[] { 0x37 }], ["blob:37"], ["b:37"], [double.PositiveInfinity]];

    private static string StoredText(object stored) => stored is double.PositiveInfinity ? "Inf" : stored is byte[] bytes
        ? System.Text.Encoding.UTF8.GetString(bytes)
        : Convert.ToString(stored, System.Globalization.CultureInfo.InvariantCulture)!;

    [Theory]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(101)]
    public async Task A_malformed_stored_value_reads_as_malformed_beside_healthy_siblings_and_survives_unrelated_saves_exactly(object stored)
    {
        var projectId = Guid.NewGuid();
        var healthyRunId = await SeedRunAsync(projectId, 1, 9, claim: false);
        var corruptRunId = await SeedRunAsync(projectId, 2, 9, claim: false);
        await StoreAsync(corruptRunId, stored);

        await using (var reader = CreateContext())
        {
            var runs = await reader.Runs.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync();

            Assert.Equal(9, runs.Single(r => r.Id == healthyRunId).ReadCodexAccountUsageWarningPercent().Value);
            Assert.True(runs.Single(r => r.Id == corruptRunId).ReadCodexAccountUsageWarningPercent().IsMalformed);
        }

        await using (var saver = CreateContext())
        {
            var corruptRun = await saver.Runs.SingleAsync(r => r.Id == corruptRunId);
            corruptRun.Claim(Now);
            corruptRun.SetCodexAccountUsageStopPercent(44);
            await saver.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(StoredText(stored), await verify.Database
            .SqlQuery<string?>($"SELECT CAST(CodexAccountUsageWarningPercent AS TEXT) AS Value FROM runs WHERE Id = {corruptRunId}").SingleAsync());
        Assert.Equal(44, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == corruptRunId)).ReadCodexAccountUsageStopPercent().Value);
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    public async Task Setting_a_valid_value_or_clearing_repairs_a_malformed_stored_run_value(object stored)
    {
        var projectId = Guid.NewGuid();
        var runId = await SeedRunAsync(projectId, 1, null);
        await StoreAsync(runId, stored);

        await using (var setter = CreateContext())
        {
            var run = await setter.Runs.SingleAsync(r => r.Id == runId);
            Assert.True(run.ReadCodexAccountUsageWarningPercent().IsMalformed);
            run.SetCodexAccountUsageWarningPercent(15);
            setter.Entry(run).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;
            await setter.SaveChangesAsync();
        }

        await using (var verify = CreateContext())
        {
            Assert.Equal(15, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).ReadCodexAccountUsageWarningPercent().Value);
        }

        await StoreAsync(runId, stored);
        await using (var clearer = CreateContext())
        {
            var run = await clearer.Runs.SingleAsync(r => r.Id == runId);
            run.SetCodexAccountUsageWarningPercent(null);
            clearer.Entry(run).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;
            await clearer.SaveChangesAsync();
        }

        await using var final = CreateContext();
        Assert.True((await final.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).ReadCodexAccountUsageWarningPercent().IsAbsent);
    }

    [Fact]
    public async Task A_stale_tracked_run_save_is_not_blocked_by_and_never_overwrites_a_newer_advisory_value()
    {
        var projectId = Guid.NewGuid();
        var runId = await SeedRunAsync(projectId, 1, 40);

        await using var stale = CreateContext();
        var staleRun = await stale.Runs.SingleAsync(r => r.Id == runId);
        await using (var newer = CreateContext())
        {
            var newerRun = await newer.Runs.SingleAsync(r => r.Id == runId);
            newerRun.SetCodexAccountUsageWarningPercent(70);
            newer.Entry(newerRun).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;
            await newer.SaveChangesAsync();
        }

        staleRun.Complete(Now.AddMinutes(1));
        await stale.SaveChangesAsync();

        await using var verify = CreateContext();
        var stored = await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(70, stored.ReadCodexAccountUsageWarningPercent().Value);
        Assert.Equal(RunLifecycle.Completed, stored.Lifecycle);
    }

    [Fact]
    public async Task A_stale_forced_advisory_write_to_a_run_that_became_terminal_still_fails_on_the_lifecycle_token()
    {
        var projectId = Guid.NewGuid();
        var runId = await SeedRunAsync(projectId, 1, 40);

        await using var stale = CreateContext();
        var staleRun = await stale.Runs.SingleAsync(r => r.Id == runId);
        await using (var competing = CreateContext())
        {
            var run = await competing.Runs.SingleAsync(r => r.Id == runId);
            run.Complete(Now.AddMinutes(1));
            await competing.SaveChangesAsync();
        }

        staleRun.SetCodexAccountUsageWarningPercent(40);
        stale.Entry(staleRun).Property(Run.CodexAccountUsageWarningStorageProperty).IsModified = true;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task Down_drops_exactly_the_new_column_without_touching_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = await SeedRunAsync(projectId, 1, 33, stop: 55);

        var runColumnsBefore = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        var attemptColumnsBefore = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var runColumnsAfter = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        Assert.Equal(["AbandonedAtUtc", "AbandonmentReason", "CodexAccountUsageWarningPercent"], runColumnsBefore.Except(runColumnsAfter).Order());
        Assert.Empty(runColumnsAfter.Except(runColumnsBefore));
        Assert.Equal(attemptColumnsBefore, (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray());

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT Objective, CodexAccountUsageStopPercent FROM runs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Objective", reader.GetString(0));
        Assert.Equal(55, reader.GetInt64(1));
    }
}
