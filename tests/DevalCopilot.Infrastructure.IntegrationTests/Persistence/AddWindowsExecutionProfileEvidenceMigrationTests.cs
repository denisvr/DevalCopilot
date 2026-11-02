using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Processes;
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
/// ADR-0072 acceptance C on a real file-backed SQLite database: <c>AddWindowsExecutionProfileEvidence</c> only adds nullable text
/// columns with no default and no backfill, so every earlier attempt, artifact and verification keeps "not recorded"; the new facts
/// round-trip through the real model and reopen; an unused downgrade drops exactly those columns; and a downgrade is refused, with
/// nothing changed, once any new evidence was recorded.
/// </summary>
public sealed class AddWindowsExecutionProfileEvidenceMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261023211312_AddLocalExecutionOwnershipRelease";
    private const string Migration = "20261101164117_AddWindowsExecutionProfileEvidence";

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("attempts", "AgentInvocationDiagnostic"),
        ("attempts", "AgentInvocationDiagnosticVersion"),
        ("attempts", "AgentStandardOutputCompletion"),
        ("attempts", "AgentStandardErrorCompletion"),
        ("attempts", "AgentStandardInputDelivery"),
        ("attempts", "ProcessStandardOutputCompletion"),
        ("attempts", "ProcessStandardErrorCompletion"),
        ("attempts", "ProcessStandardInputDelivery"),
        ("artifacts", "CaptureCompletion"),
        ("verification_executions", "StandardOutputCompletion"),
        ("verification_executions", "StandardErrorCompletion"),
        ("verification_executions", "StandardInputDelivery"),
        ("verification_executions", "EnvironmentProfileVersion"),
        ("verification_executions", "Diagnostic"),
        ("verification_output_artifacts", "CaptureCompletion"),
    ];

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-execution-profile-migration-{Guid.NewGuid():N}.db");

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
    public async Task Every_new_column_is_nullable_text_without_a_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        foreach (var (table, column) in NewColumns)
        {
            var found = Assert.Single(await ReadColumnsAsync(table), candidate => candidate.Name == column);
            Assert.Equal("TEXT", found.Type);
            Assert.False(found.NotNull, $"{table}.{column}");
            Assert.False(found.HasDefault, $"{table}.{column}");
        }
    }

    [Fact]
    public async Task An_attempt_that_predates_the_migration_keeps_not_recorded_facts_after_upgrade_and_reopen()
    {
        var (runId, attemptId) = await SeedPriorAsync();

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var attempt = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(runId, attempt.RunId);
        Assert.Null(attempt.AgentInvocationDiagnostic);
        Assert.Null(attempt.AgentInvocationDiagnosticVersion);
        Assert.Null(attempt.GetAgentProcessIoCompletion());
        Assert.Equal(AgentInvocationDiagnosticState.NotRecorded, AgentInvocationDiagnosticFact.ForAttempt(attempt).State);
        Assert.Equal("claude-critical-review-v1", attempt.AgentAdapterContractVersion);
    }

    [Fact]
    public async Task Recorded_facts_round_trip_through_the_real_model_and_an_unknown_version_is_never_a_category()
    {
        var (_, attemptId) = await SeedPriorAsync();
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            await upgraded.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE attempts SET AgentInvocationDiagnostic = {nameof(AgentInvocationDiagnostic.AuthenticationRequired)},
                       AgentInvocationDiagnosticVersion = {AgentInvocationDiagnosticContract.CurrentVersion},
                       AgentStandardOutputCompletion = {nameof(OutputCaptureCompletion.DeadlineExceeded)},
                       AgentStandardErrorCompletion = {nameof(OutputCaptureCompletion.Complete)},
                       AgentStandardInputDelivery = {nameof(InputDeliveryCompletion.Complete)}
                   WHERE Id = {attemptId}");
        }

        await using (var reopened = CreateContext())
        {
            var attempt = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
            var fact = AgentInvocationDiagnosticFact.ForAttempt(attempt);
            Assert.Equal(AgentInvocationDiagnosticState.Recorded, fact.State);
            Assert.Equal(AgentInvocationDiagnostic.AuthenticationRequired, fact.Category);
            Assert.Equal(
                new ProcessIoCompletion(OutputCaptureCompletion.DeadlineExceeded, OutputCaptureCompletion.Complete, InputDeliveryCompletion.Complete),
                attempt.GetAgentProcessIoCompletion());

            await reopened.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentInvocationDiagnosticVersion = {"agent-invocation-diagnostic-v0"} WHERE Id = {attemptId}");
        }

        await using var again = CreateContext();
        var unknown = await again.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AgentInvocationDiagnosticState.Unknown, AgentInvocationDiagnosticFact.ForAttempt(unknown).State);
        Assert.Null(unknown.GetKnownAgentInvocationDiagnostic());
    }

    [Fact]
    public async Task An_unused_downgrade_drops_exactly_the_new_columns_and_keeps_every_row()
    {
        var (_, attemptId) = await SeedPriorAsync();
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            await upgraded.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        foreach (var (table, column) in NewColumns)
        {
            Assert.DoesNotContain(await ReadColumnsAsync(table), candidate => candidate.Name == column);
        }

        Assert.Equal(1L, await CountAsync(CountTarget.Attempt, attemptId.ToString().ToUpperInvariant()));
        Assert.Equal(0L, await CountAsync(CountTarget.Migration, Migration));
    }

    [Theory]
    [InlineData("UPDATE attempts SET AgentInvocationDiagnostic = 'TimedOut', AgentInvocationDiagnosticVersion = 'agent-invocation-diagnostic-v1'")]
    [InlineData("UPDATE attempts SET AgentStandardInputDelivery = 'Faulted'")]
    [InlineData("UPDATE attempts SET ProcessStandardOutputCompletion = 'Complete'")]
    public async Task A_downgrade_is_refused_with_nothing_changed_once_new_evidence_was_recorded(string recordEvidence)
    {
        await SeedPriorAsync();
        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            await upgraded.Database.ExecuteSqlRawAsync(recordEvidence);
        }

        await using (var downgrade = CreateContext())
        {
            var refused = await Assert.ThrowsAsync<SqliteException>(
                () => downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration));
            Assert.Contains("used_execution_profile_evidence_blocks_downgrade", refused.Message, StringComparison.Ordinal);
        }

        foreach (var (table, column) in NewColumns)
        {
            Assert.Contains(await ReadColumnsAsync(table), candidate => candidate.Name == column);
        }

        Assert.Equal(1L, await CountAsync(CountTarget.Migration, Migration));
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    /// <summary>A historical failed Claude critical review at the previous schema, inserted by raw statement.</summary>
    private async Task<(Guid RunId, Guid AttemptId)> SeedPriorAsync()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using var previous = CreateContext();
        await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
        await previous.SaveChangesAsync();
        await previous.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO runs
                   (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                    CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
               VALUES
                   ({runId}, {projectId}, {1}, {"Historical run"},
                    {nameof(RunLifecycle.Running)}, {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)},
                    {Now}, {Now}, {0d})");
        await previous.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO attempts
                   (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole,
                    AgentResponseContract, AgentPermissionProfile, AgentAdapterContractVersion, AgentOutcome)
               VALUES
                   ({attemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)},
                    {Now}, {""}, {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentRole.CriticalReviewer)},
                    {nameof(AgentResponseContract.CriticalReview)}, {nameof(AgentPermissionProfile.ReadOnly)},
                    {"claude-critical-review-v1"}, {nameof(AgentOutcome.ProviderInvocationFailed)})");
        return (runId, attemptId);
    }

    private async Task<long> CountAsync(CountTarget target, string value)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        if (target == CountTarget.Attempt)
        {
            command.CommandText = "SELECT COUNT(*) FROM attempts WHERE Id = $value;";
        }
        else
        {
            command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = $value;";
        }
        command.Parameters.AddWithValue("$value", value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private enum CountTarget
    {
        Attempt,
        Migration,
    }

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
}
