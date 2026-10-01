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
/// Proves <c>AddRunExecutionMode</c> is truthful: it adds one NOT NULL INTEGER column defaulting to the Legacy value
/// 0, so every run that predates it is Legacy without any inference from its attempts, providers, lifecycle, or
/// events; every other row and all evidence survive; a fresh database assigns the explicit positive modes; a stored
/// number outside the enum is representable and never recognized; and the reverse migration drops only that column.
/// </summary>
public sealed class AddRunExecutionModeMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20260930121919_AddClaudeMutationTurnLimit";

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 14, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-run-execution-mode-migration-{Guid.NewGuid():N}.db");

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

    private static async Task InsertLegacyRunAsync(
        DevalCopilotDbContext context, Guid projectId, Guid runId, int number, RunLifecycle lifecycle, RunStage stage)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO runs
                   (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                    CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, MaximumReviewCorrectionAttempts,
                    MaximumAgentAttempts)
               VALUES
                   ({runId}, {projectId}, {number}, {"Historical run " + number},
                    {lifecycle.ToString()}, {stage.ToString()}, {nameof(ParticipantKind.None)},
                    {Now}, {Now}, {0d}, {2}, {16})");
    }

    private static async Task InsertLegacyAttemptAsync(
        DevalCopilotDbContext context, Guid runId, Guid attemptId, AttemptKind kind, string provider)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO attempts
                   (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider)
               VALUES
                   ({attemptId}, {runId}, {1}, {kind.ToString()}, {nameof(AttemptStatus.Completed)}, {Now}, {""},
                    {(kind == AttemptKind.Agent ? provider : null)})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO events (Id, RunId, AttemptId, EventType, ActorKind, PayloadJson, OccurredAtUtc)
               VALUES ({Guid.NewGuid()}, {runId}, {attemptId}, {nameof(RunEventType.RunStarted)},
                       {nameof(ParticipantKind.Orchestrator)}, {"{}"}, {Now})");
    }

    private async Task<IReadOnlyList<(string Name, string Type, bool NotNull, string? Default)>> ReadColumnsAsync(string table)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info($table);";
        command.Parameters.AddWithValue("$table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<(string, string, bool, string?)>();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0, reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return columns;
    }

    [Fact]
    public async Task Every_run_that_predates_the_migration_becomes_legacy_regardless_of_its_shape_and_keeps_its_evidence()
    {
        var projectId = Guid.NewGuid();
        var created = Guid.NewGuid();
        var runningAgent = Guid.NewGuid();
        var completedSimulated = Guid.NewGuid();
        var failedProcess = Guid.NewGuid();
        var agentAttempt = Guid.NewGuid();
        var simulatedAttempt = Guid.NewGuid();
        var processAttempt = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await InsertLegacyRunAsync(previous, projectId, created, 1, RunLifecycle.Created, RunStage.Intake);
            await InsertLegacyRunAsync(previous, projectId, runningAgent, 2, RunLifecycle.Running, RunStage.Plan);
            await InsertLegacyRunAsync(previous, projectId, completedSimulated, 3, RunLifecycle.Completed, RunStage.Completed);
            await InsertLegacyRunAsync(previous, projectId, failedProcess, 4, RunLifecycle.Failed, RunStage.Execute);
            await InsertLegacyAttemptAsync(previous, runningAgent, agentAttempt, AttemptKind.Agent, nameof(AgentProvider.Codex));
            await InsertLegacyAttemptAsync(previous, completedSimulated, simulatedAttempt, AttemptKind.Simulated, string.Empty);
            await InsertLegacyAttemptAsync(previous, failedProcess, processAttempt, AttemptKind.Process, string.Empty);
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var runs = await reopened.Runs.AsNoTracking().Where(run => run.ProjectId == projectId).OrderBy(run => run.ExecutionNumber).ToListAsync();
        Assert.Equal(4, runs.Count);
        Assert.All(runs, run => Assert.Equal(RunExecutionMode.Legacy, run.ExecutionMode));
        Assert.Equal(
            [RunLifecycle.Created, RunLifecycle.Running, RunLifecycle.Completed, RunLifecycle.Failed],
            runs.Select(run => run.Lifecycle));
        Assert.All(runs, run =>
        {
            Assert.Equal(2, run.MaximumReviewCorrectionAttempts);
            Assert.Equal(16, run.MaximumAgentAttempts);
            Assert.Null(run.MaximumAgentInvocationTime);
        });
        Assert.Equal(3, await reopened.Attempts.CountAsync(attempt => attempt.RunId != Guid.Empty
            && (attempt.Id == agentAttempt || attempt.Id == simulatedAttempt || attempt.Id == processAttempt)));
        Assert.Equal(3, await reopened.Events.CountAsync(runEvent => runEvent.AttemptId != null));
    }

    [Fact]
    public async Task The_new_column_is_a_non_null_integer_defaulting_to_the_legacy_value()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var column = Assert.Single(await ReadColumnsAsync("runs"), candidate => candidate.Name == "ExecutionMode");
        Assert.Equal("INTEGER", column.Type);
        Assert.True(column.NotNull);
        Assert.Equal("0", column.Default);
    }

    [Fact]
    public async Task A_fresh_database_stores_explicit_positive_modes_and_an_undefined_number_is_never_recognized()
    {
        var projectId = Guid.NewGuid();
        var simulatedId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var undefinedId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Fresh", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.AddRange(
                Run.RecordClassifiedIntent(simulatedId, projectId, 1, RunExecutionMode.Simulated, "Demo", Now),
                Run.RecordClassifiedIntent(manualId, projectId, 2, RunExecutionMode.ManualAgent, "Manual", Now),
                Run.RecordClassifiedIntent(undefinedId, projectId, 3, RunExecutionMode.ManualAgent, "Corrupted", Now));
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET ExecutionMode = {7} WHERE Id = {undefinedId}");
        }

        await using var reopened = CreateContext();
        Assert.Equal(RunExecutionMode.Simulated, (await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == simulatedId)).ExecutionMode);
        Assert.Equal(RunExecutionMode.ManualAgent, (await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == manualId)).ExecutionMode);
        var undefined = (await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == undefinedId)).ExecutionMode;
        Assert.Equal(RunExecutionModeStorage.Unrecognized, undefined);
        Assert.False(RunExecutionModeAdmission.IsRecognized(undefined));
    }

    [Fact]
    public async Task The_model_maps_the_mode_and_the_execution_number_counter_as_concurrency_tokens()
    {
        await using var context = CreateContext();

        Assert.True(context.Model.FindEntityType(typeof(Run))!.FindProperty(Run.ExecutionModeStorageProperty)!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(Project))!.FindProperty(nameof(Project.NextExecutionNumber))!.IsConcurrencyToken);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Down_drops_only_the_new_column_without_touching_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Down", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordClassifiedIntent(runId, projectId, 1, RunExecutionMode.ManualAgent, "Keep me", Now));
            await context.SaveChangesAsync();
        }

        var before = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var after = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        Assert.Equal(["ExecutionMode"], before.Except(after));
        Assert.Empty(after.Except(before));

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT Objective, Lifecycle FROM runs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Keep me", reader.GetString(0));
        Assert.Equal(nameof(RunLifecycle.Created), reader.GetString(1));
    }
}
