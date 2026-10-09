using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Configurations.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves <c>AddRunAbandonment</c> (ADR-0031) is a focused, truthful schema change on a real file-backed SQLite database: it only adds
/// two nullable TEXT columns to runs with no default and no backfill, so every run and lifecycle that predates it keeps NULL there
/// and all of its other data; no trigger, index or other table changes; the new columns are deliberately not concurrency tokens while
/// the lifecycle stays one; an abandonment round-trips exactly; and the reverse migration drops exactly those two columns.
/// </summary>
public sealed class AddRunAbandonmentMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261008090914_AddLocalCommitAttentionExclusion";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-run-abandonment-{Guid.NewGuid():N}.db");

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

    private async Task<string[]> ReadSchemaObjectsAsync()
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT type || ':' || name FROM sqlite_master WHERE type IN ('trigger', 'index', 'table') ORDER BY 1;";
        await using var reader = await command.ExecuteReaderAsync();
        var objects = new List<string>();
        while (await reader.ReadAsync())
        {
            objects.Add(reader.GetString(0));
        }

        return [.. objects];
    }

    private static string RunInsert =>
        @"INSERT INTO runs
              (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
               CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, ExecutionMode)
          VALUES
              ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {7}, {8}, {9})";

    [Fact]
    public async Task Runs_of_every_historical_lifecycle_keep_null_and_all_of_their_other_data()
    {
        var projectId = Guid.NewGuid();
        var lifecycles = new[] { "Created", "Running", "Completed", "Failed", "Interrupted" };
        var runIds = lifecycles.Select(_ => Guid.NewGuid()).ToArray();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            for (var index = 0; index < lifecycles.Length; index++)
            {
                await previous.Database.ExecuteSqlRawAsync(
                    RunInsert, runIds[index], projectId, index + 1, "Historical " + lifecycles[index], lifecycles[index], nameof(RunStage.Plan),
                    nameof(ParticipantKind.None), Now, 12.5, 2);
            }
        }

        var objectsBefore = await ReadSchemaObjectsAsync();
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        for (var index = 0; index < lifecycles.Length; index++)
        {
            var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runIds[index]);
            Assert.Equal(Enum.Parse<RunLifecycle>(lifecycles[index]), run.Lifecycle);
            Assert.Equal(RunStage.Plan, run.Stage);
            Assert.Equal(12.5, run.AccumulatedAutonomousSeconds);
            Assert.Equal("Historical " + lifecycles[index], run.Objective);
            Assert.Equal(RunExecutionMode.ManualAgent, run.ExecutionMode);
            Assert.Null(run.AbandonmentReason);
            Assert.Null(run.AbandonedAtUtc);
            Assert.Equal("null", await reopened.Database
                .SqlQuery<string>($"SELECT typeof(AbandonmentReason) AS Value FROM runs WHERE Id = {runIds[index]}").SingleAsync());
            Assert.Equal("null", await reopened.Database
                .SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runIds[index]}").SingleAsync());
        }

        // Nothing but the two columns changed: the same tables, indexes and local-commit seam triggers.
        Assert.Equal(objectsBefore.Where(name => !name.StartsWith("table:__EFMigrationsHistory", StringComparison.Ordinal)),
            (await ReadSchemaObjectsAsync()).Where(name => !name.StartsWith("table:__EFMigrationsHistory", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_new_columns_are_nullable_text_with_no_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var columns = await ReadColumnsAsync("runs");
        var reason = Assert.Single(columns, column => column.Name == "AbandonmentReason");
        var at = Assert.Single(columns, column => column.Name == "AbandonedAtUtc");
        Assert.Equal(("TEXT", false, false), (reason.Type, reason.NotNull, reason.HasDefault));
        Assert.Equal(("TEXT", false, false), (at.Type, at.NotNull, at.HasDefault));
    }

    [Fact]
    public async Task Only_the_lifecycle_is_a_concurrency_token_and_the_reason_is_bounded()
    {
        await using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Run))!;

        Assert.False(entity.FindProperty(nameof(Run.AbandonmentReason))!.IsConcurrencyToken);
        Assert.False(entity.FindProperty(nameof(Run.AbandonedAtUtc))!.IsConcurrencyToken);
        Assert.True(entity.FindProperty(nameof(Run.Lifecycle))!.IsConcurrencyToken);
        Assert.Equal(2048, entity.FindProperty(nameof(Run.AbandonmentReason))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(Run.AbandonmentReason))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Run.AbandonedAtUtc))!.IsNullable);
    }

    [Fact]
    public async Task An_abandonment_round_trips_exactly_and_never_touches_a_sibling_run()
    {
        var projectId = Guid.NewGuid();
        Guid abandonedId;
        Guid siblingId;
        var at = new DateTimeOffset(2026, 10, 8, 12, 34, 56, 789, TimeSpan.Zero).AddTicks(1234);
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(projectId, "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var abandoned = Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
            abandoned.Claim(Now.AddSeconds(1));
            abandoned.Abandon("First line\nSecond line, with é漢😀", at);
            var sibling = Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Other", Now);
            context.Projects.Add(project);
            context.Runs.AddRange(abandoned, sibling);
            await context.SaveChangesAsync();
            abandonedId = abandoned.Id;
            siblingId = sibling.Id;
        }

        await using var reopened = CreateContext();
        var stored = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == abandonedId);
        Assert.Equal(RunLifecycle.Abandoned, stored.Lifecycle);
        Assert.Equal("First line\nSecond line, with é漢😀", stored.AbandonmentReason);
        Assert.Equal(at, stored.AbandonedAtUtc);
        Assert.Equal(at.Ticks, stored.AbandonedAtUtc!.Value.Ticks);
        Assert.Equal("Abandoned", await reopened.Database.SqlQuery<string>($"SELECT Lifecycle AS Value FROM runs WHERE Id = {abandonedId}").SingleAsync());
        Assert.Equal("text", await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonmentReason) AS Value FROM runs WHERE Id = {abandonedId}").SingleAsync());
        var storedSibling = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == siblingId);
        Assert.Equal(RunLifecycle.Created, storedSibling.Lifecycle);
        Assert.Null(storedSibling.AbandonmentReason);
    }

    [Fact]
    public async Task A_stale_tracked_run_cannot_overwrite_a_committed_abandonment_because_the_lifecycle_token_rejects_it()
    {
        var projectId = Guid.NewGuid();
        Guid runId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(projectId, "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
            run.Claim(Now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using var stale = CreateContext();
        var staleRun = await stale.Runs.SingleAsync(candidate => candidate.Id == runId);
        await using (var winner = CreateContext())
        {
            var run = await winner.Runs.SingleAsync(candidate => candidate.Id == runId);
            run.Abandon("Done", Now.AddMinutes(1));
            await winner.SaveChangesAsync();
        }

        staleRun.Complete(Now.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = CreateContext();
        Assert.Equal(RunLifecycle.Abandoned, (await verify.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId)).Lifecycle);
    }

    [Fact]
    public async Task Down_drops_exactly_the_two_new_columns_without_touching_other_data()
    {
        var projectId = Guid.NewGuid();
        Guid runId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(projectId, "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Kept objective", Now);
            run.SetCodexAccountUsageStopPercent(55);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        var runColumnsBefore = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        var attemptColumnsBefore = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();
        var objectsBefore = await ReadSchemaObjectsAsync();

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var runColumnsAfter = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        Assert.Equal(["AbandonedAtUtc", "AbandonmentReason"], runColumnsBefore.Except(runColumnsAfter).Order(StringComparer.Ordinal));
        Assert.Empty(runColumnsAfter.Except(runColumnsBefore));
        Assert.Equal(attemptColumnsBefore, (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray());
        Assert.Equal(objectsBefore, await ReadSchemaObjectsAsync());

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT Objective, CodexAccountUsageStopPercent FROM runs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Kept objective", reader.GetString(0));
        Assert.Equal(55, reader.GetInt64(1));
    }

    // ---- the tolerant mapping of the abandonment time ------------------------------------------------------------------------------

    [Fact]
    public async Task The_tolerant_mapping_changes_neither_the_model_snapshot_nor_the_column()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        Assert.False(context.Database.HasPendingModelChanges());
        var at = Assert.Single(await ReadColumnsAsync("runs"), column => column.Name == "AbandonedAtUtc");
        Assert.Equal(("TEXT", false, false), (at.Type, at.NotNull, at.HasDefault));
        Assert.Equal(typeof(DateTimeOffset?), context.Model.FindEntityType(typeof(Run))!.FindProperty(nameof(Run.AbandonedAtUtc))!.ClrType);
    }

    [Fact]
    public async Task The_abandonment_time_is_stored_in_exactly_the_text_form_the_provider_writes_for_its_other_timestamps()
    {
        var times = new[]
        {
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 12, 34, 56, 500, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 12, 34, 56, 789, TimeSpan.Zero).AddTicks(1234),
            new DateTimeOffset(2026, 10, 8, 15, 30, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 10, 8, 4, 30, 0, TimeSpan.FromHours(-5.5)),
        };
        var ids = new List<Guid>();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            context.Projects.Add(project);
            foreach (var time in times)
            {
                var run = Run.RecordClassifiedIntent(
                    Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
                run.Abandon("Reason", time);
                context.Runs.Add(run);
                ids.Add(run.Id);
            }

            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        for (var index = 0; index < times.Length; index++)
        {
            var id = ids[index];
            var abandonedText = await reopened.Database.SqlQuery<string>($"SELECT AbandonedAtUtc AS Value FROM runs WHERE Id = {id}").SingleAsync();
            var advancedText = await reopened.Database.SqlQuery<string>($"SELECT LastAdvancedAtUtc AS Value FROM runs WHERE Id = {id}").SingleAsync();
            Assert.Equal(advancedText, abandonedText);
            var stored = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == id);
            Assert.Equal(times[index], stored.AbandonedAtUtc);
            Assert.Equal(times[index].Offset, stored.AbandonedAtUtc!.Value.Offset);
        }
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("2026-13-45 99:99:99+00:00")]
    [InlineData("2026-10-08 12:00:00")]
    [InlineData("2026-10-08T12:00:00+00:00")]
    [InlineData(" 2026-10-08 12:00:00+00:00")]
    [InlineData("5")]
    public async Task A_stored_value_that_is_not_the_written_form_reads_as_null_while_the_rest_of_the_row_still_loads(string stored)
    {
        Guid runId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
            run.Abandon("Reason", Now.AddMinutes(1));
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET AbandonedAtUtc = {stored} WHERE Id = {runId}");
        }

        await using var reopened = CreateContext();
        var untracked = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == runId);
        var tracked = await reopened.Runs.SingleAsync(run => run.Id == runId);

        Assert.Null(untracked.AbandonedAtUtc);
        Assert.Null(tracked.AbandonedAtUtc);
        Assert.Equal(RunLifecycle.Abandoned, untracked.Lifecycle);
        Assert.Equal("Reason", untracked.AbandonmentReason);
        Assert.Equal(Now.AddMinutes(1), untracked.LastAdvancedAtUtc);
    }

    [Fact]
    public void The_converter_writes_the_provider_form_and_reads_only_that_form()
    {
        var whole = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var fraction = whole.AddTicks(1234567);
        var offset = new DateTimeOffset(2026, 10, 8, 15, 30, 0, TimeSpan.FromHours(3));

        Assert.Equal("2026-10-08 12:00:00+00:00", StoredAbandonmentTimeConverter.Write(whole));
        Assert.Equal("2026-10-08 12:00:00.1234567+00:00", StoredAbandonmentTimeConverter.Write(fraction));
        Assert.Equal("2026-10-08 15:30:00+03:00", StoredAbandonmentTimeConverter.Write(offset));
        Assert.Null(StoredAbandonmentTimeConverter.Write(null));
        foreach (var time in new[] { whole, fraction, offset })
        {
            var text = StoredAbandonmentTimeConverter.Write(time);
            Assert.Equal(time, StoredAbandonmentTimeConverter.Read(text));
            Assert.Equal(time.Offset, StoredAbandonmentTimeConverter.Read(text)!.Value.Offset);
        }

        Assert.Null(StoredAbandonmentTimeConverter.Read(null));
        Assert.Null(StoredAbandonmentTimeConverter.Read("garbage"));
    }

    // ---- the storage class is preserved at the read boundary --------------------------------------------------------------------

    public static TheoryData<int> InstantIndexes() => [0, 1, 2, 3];

    private static DateTimeOffset StorageClassInstant(int index) => index switch
    {
        0 => new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
        1 => new DateTimeOffset(2026, 10, 8, 12, 34, 56, 789, TimeSpan.Zero).AddTicks(1234),
        2 => new DateTimeOffset(2026, 10, 8, 15, 30, 0, TimeSpan.FromHours(3)),
        _ => new DateTimeOffset(2026, 10, 8, 4, 30, 0, 250, TimeSpan.FromHours(-5.5)),
    };

    [Theory]
    [MemberData(nameof(InstantIndexes))]
    public async Task A_blob_holding_the_exact_text_of_a_valid_time_reads_as_null_while_the_identical_text_reads_as_the_time(int index)
    {
        var time = StorageClassInstant(index);
        Guid textId;
        Guid blobId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var text = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
            text.Abandon("Reason", time);
            var blob = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Other", Now);
            blob.Abandon("Reason", time);
            context.Projects.Add(project);
            context.Runs.AddRange(text, blob);
            await context.SaveChangesAsync();
            textId = text.Id;
            blobId = blob.Id;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = {blobId}");
        }

        await using var reopened = CreateContext();
        Assert.Equal("text", await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {textId}").SingleAsync());
        Assert.Equal("blob", await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {blobId}").SingleAsync());
        var textBytes = await reopened.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {textId}").SingleAsync();
        var blobBytes = await reopened.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {blobId}").SingleAsync();
        Assert.Equal(textBytes, blobBytes);

        var untrackedText = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == textId);
        var untrackedBlob = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == blobId);
        var tracked = await reopened.Runs.SingleAsync(run => run.Id == blobId);
        Assert.Equal(time, untrackedText.AbandonedAtUtc);
        Assert.Equal(time.Offset, untrackedText.AbandonedAtUtc!.Value.Offset);
        Assert.Null(untrackedBlob.AbandonedAtUtc);
        Assert.Null(tracked.AbandonedAtUtc);
        Assert.Equal(RunLifecycle.Abandoned, untrackedBlob.Lifecycle);
        Assert.Equal("Reason", untrackedBlob.AbandonmentReason);

        // An unrelated save of the same row neither rewrites the damaged column nor changes its storage class or bytes.
        reopened.Entry(tracked).Property(run => run.AccumulatedAutonomousSeconds).CurrentValue += 1;
        await reopened.SaveChangesAsync();
        Assert.Equal("blob", await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {blobId}").SingleAsync());
        Assert.Equal(blobBytes, await reopened.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {blobId}").SingleAsync());
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("5")]
    [InlineData("2460000.5")]
    [InlineData("X''")]
    [InlineData("X'010203'")]
    public async Task A_stored_class_other_than_text_reads_as_null_and_keeps_its_class(string literal)
    {
        Guid runId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var run = Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", Now);
            run.Abandon("Reason", Now.AddMinutes(1));
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
#pragma warning disable EF1002
            var updated = await context.Database.ExecuteSqlRawAsync($"UPDATE runs SET AbandonedAtUtc = {literal} WHERE Id = {{0}}", runId);
            Assert.Equal(1, updated);
#pragma warning restore EF1002
        }

        await using var reopened = CreateContext();
        var classBefore = await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync();

        Assert.Null((await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == runId)).AbandonedAtUtc);
        Assert.Null((await reopened.Runs.SingleAsync(run => run.Id == runId)).AbandonedAtUtc);
        Assert.Equal(classBefore, await reopened.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync());
    }
}
