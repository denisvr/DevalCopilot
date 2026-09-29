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
/// Proves <c>AddRunTokenStopThresholds</c> only adds two nullable columns with no default
/// and no backfill, so a run that predates it has no stop for either provider and every claim is
/// unchanged, and that the two columns are the concurrency tokens the claim guard relies on while
/// the advisory warning columns are not.
/// </summary>
public sealed class AddRunTokenStopThresholdsMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20260929144212_AddCodexPlanningRepairLink";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-token-stop-migration-{Guid.NewGuid():N}.db");

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

    [Fact]
    public async Task A_run_that_predates_the_migration_has_no_threshold_and_the_columns_are_nullable_without_a_default()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", now));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical run"},
                        {nameof(RunLifecycle.Running)}, {nameof(RunStage.Critique)}, {nameof(ParticipantKind.None)},
                        {now}, {now}, {0d})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Null(run.CodexTokenStopThreshold);
        Assert.Null(run.ClaudeTokenStopThreshold);

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        foreach (var column in new[] { "CodexTokenStopThreshold", "ClaudeTokenStopThreshold" })
        {
            await using var command = probe.CreateCommand();
            command.CommandText = "SELECT \"notnull\", dflt_value FROM pragma_table_info('runs') WHERE name = $name;";
            command.Parameters.AddWithValue("$name", column);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), column);
            Assert.Equal(0, reader.GetInt64(0));
            Assert.True(reader.IsDBNull(1), column);
        }
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    [Fact]
    public async Task Only_the_stop_columns_are_concurrency_tokens_never_the_advisory_warning_columns()
    {
        await using var context = CreateContext();
        var run = context.Model.FindEntityType(typeof(Run))!;

        Assert.True(run.FindProperty(nameof(Run.CodexTokenStopThreshold))!.IsConcurrencyToken);
        Assert.True(run.FindProperty(nameof(Run.ClaudeTokenStopThreshold))!.IsConcurrencyToken);
        Assert.False(run.FindProperty(nameof(Run.CodexTokenWarningThreshold))!.IsConcurrencyToken);
        Assert.False(run.FindProperty(nameof(Run.ClaudeTokenWarningThreshold))!.IsConcurrencyToken);
    }
}
