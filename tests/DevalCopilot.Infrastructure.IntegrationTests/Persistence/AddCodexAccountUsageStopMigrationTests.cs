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
/// Proves <c>AddCodexAccountUsageStop</c> (ADR-0025) is truthful on a real file-backed SQLite database: it only adds one nullable
/// INTEGER column to runs and a nullable INTEGER and a nullable TEXT column to attempts, with no default and no backfill, so every
/// run and attempt that predates it keeps <see langword="null"/> and is never given an invented policy or decision; values round-trip
/// through the real EF model in their exact storage class; a tampered stored value reads as malformed beside healthy siblings,
/// survives unrelated saves exactly, and is repaired by a valid set; and the reverse migration drops exactly those columns.
/// </summary>
public sealed class AddCodexAccountUsageStopMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261004160723_AddAgentModelContextLimits";

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-account-usage-stop-{Guid.NewGuid():N}.db");

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

    private static Attempt ClaimCodex(Guid runId, int number, int? snapshot)
    {
        var attempt = Attempt.ClaimAgentWithAssignment(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, Now, requestedModel: null, requestedEffort: null, agentBudgetSlot: number);
        if (snapshot is { } value)
        {
            attempt.SnapshotCodexAccountUsageStop(value);
        }

        return attempt;
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

    [Fact]
    public async Task Runs_and_attempts_that_predate_the_migration_keep_null_in_every_new_column_and_their_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, RequestedClaudeModel, RequestedClaudeEffort)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical run"}, {nameof(RunLifecycle.Running)}, {nameof(RunStage.Execute)},
                        {nameof(ParticipantKind.None)}, {Now}, {Now}, {0d}, {"opus"}, {"high"})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole,
                        AgentResponseContract, AgentPermissionProfile, AgentAdapterContractVersion)
                   VALUES
                       ({attemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)}, {Now}, {""},
                        {nameof(AgentProvider.Codex)}, {nameof(AgentRole.Planner)}, {nameof(AgentResponseContract.Proposal)},
                        {nameof(AgentPermissionProfile.ReadOnly)}, {"codex-planning-v1"})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.True(run.ReadCodexAccountUsageStopPercent().IsAbsent);
        Assert.Equal("opus", run.RequestedClaudeModel);
        var attempt = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.True(attempt.ReadAgentCodexAccountUsageStopPercent().IsAbsent);
        Assert.Null(attempt.AgentAccountUsageDecisionSnapshot);
        Assert.Null(attempt.GetAgentAccountUsageDecision());
        Assert.Equal("codex-planning-v1", attempt.AgentAdapterContractVersion);
    }

    [Fact]
    public async Task The_new_columns_are_nullable_with_the_expected_types_and_no_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var run = Assert.Single(await ReadColumnsAsync("runs"), column => column.Name == "CodexAccountUsageStopPercent");
        Assert.Equal(("INTEGER", false, false), (run.Type, run.NotNull, run.HasDefault));
        var attempts = await ReadColumnsAsync("attempts");
        var threshold = Assert.Single(attempts, column => column.Name == "AgentCodexAccountUsageStopPercent");
        Assert.Equal(("INTEGER", false, false), (threshold.Type, threshold.NotNull, threshold.HasDefault));
        var decision = Assert.Single(attempts, column => column.Name == "AgentAccountUsageDecisionSnapshot");
        Assert.Equal(("TEXT", false, false), (decision.Type, decision.NotNull, decision.HasDefault));
    }

    [Fact]
    public async Task The_model_maps_exact_stored_text_with_only_the_run_value_a_concurrency_token()
    {
        await using var context = CreateContext();

        var runProperty = context.Model.FindEntityType(typeof(Run))!.FindProperty(Run.CodexAccountUsageStopStorageProperty)!;
        Assert.True(runProperty.IsConcurrencyToken);
        Assert.True(runProperty.IsNullable);
        Assert.Equal(typeof(string), runProperty.ClrType);
        Assert.Equal("CodexAccountUsageStopPercent", runProperty.GetColumnName());
        Assert.Equal("INTEGER", runProperty.GetColumnType());

        var attemptProperty = context.Model.FindEntityType(typeof(Attempt))!.FindProperty(Attempt.AgentCodexAccountUsageStopStorageProperty)!;
        Assert.False(attemptProperty.IsConcurrencyToken);
        Assert.True(attemptProperty.IsNullable);
        Assert.Equal(typeof(string), attemptProperty.ClrType);
        Assert.Equal("AgentCodexAccountUsageStopPercent", attemptProperty.GetColumnName());
        Assert.Equal("INTEGER", attemptProperty.GetColumnType());

        var decision = context.Model.FindEntityType(typeof(Attempt))!.FindProperty(nameof(Attempt.AgentAccountUsageDecisionSnapshot))!;
        Assert.False(decision.IsConcurrencyToken);
        Assert.Equal("TEXT", decision.GetColumnType());
    }

    [Fact]
    public async Task A_setting_a_snapshot_and_a_recorded_decision_round_trip_and_a_valid_value_is_a_sqlite_integer()
    {
        var projectId = Guid.NewGuid();
        var configuredRunId = Guid.NewGuid();
        var plainRunId = Guid.NewGuid();
        Guid stoppedId;
        Guid plainAttemptId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Round trip", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var configured = Run.RecordIntent(configuredRunId, projectId, 1, "Configured", Now);
            configured.SetCodexAccountUsageStopPercent(80);
            context.Runs.AddRange(configured, Run.RecordIntent(plainRunId, projectId, 2, "Plain", Now));
            var stopped = ClaimCodex(configuredRunId, 1, 80);
            stopped.CompleteAgentAccountUsageStop(
                AgentCodexAccountUsageDecision.Create(
                    CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, Now,
                    [new("codex", CodexAccountUsageWindowKind.Primary, 90)]),
                Now);
            var plain = ClaimCodex(plainRunId, 1, null);
            stoppedId = stopped.Id;
            plainAttemptId = plain.Id;
            context.Attempts.AddRange(stopped, plain);
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal(80, (await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == configuredRunId)).ReadCodexAccountUsageStopPercent().Value);
        Assert.True((await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == plainRunId)).ReadCodexAccountUsageStopPercent().IsAbsent);
        var stoppedRow = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == stoppedId);
        Assert.Equal(80, stoppedRow.ReadAgentCodexAccountUsageStopPercent().Value);
        Assert.Equal(AgentOutcome.AccountUsageStopReached, stoppedRow.AgentOutcome);
        Assert.Equal(90, Assert.Single(stoppedRow.GetAgentAccountUsageDecision()!.Windows).UsedPercent);
        var plainRow = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == plainAttemptId);
        Assert.True(plainRow.ReadAgentCodexAccountUsageStopPercent().IsAbsent);
        Assert.Null(plainRow.AgentAccountUsageDecisionSnapshot);

        Assert.Equal("integer", await reopened.Database
            .SqlQuery<string>($"SELECT typeof(CodexAccountUsageStopPercent) AS Value FROM runs WHERE Id = {configuredRunId}").SingleAsync());
        Assert.Equal("integer", await reopened.Database
            .SqlQuery<string>($"SELECT typeof(AgentCodexAccountUsageStopPercent) AS Value FROM attempts WHERE Id = {stoppedId}").SingleAsync());
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
        var healthyRunId = Guid.NewGuid();
        var corruptRunId = Guid.NewGuid();
        Guid healthyAttemptId;
        Guid corruptAttemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Malformed storage", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var healthyRun = Run.RecordIntent(healthyRunId, projectId, 1, "Healthy", Now);
            healthyRun.SetCodexAccountUsageStopPercent(9);
            context.Runs.AddRange(healthyRun, Run.RecordIntent(corruptRunId, projectId, 2, "Corrupt", Now));
            var healthyAttempt = ClaimCodex(healthyRunId, 1, 9);
            var corruptAttempt = ClaimCodex(corruptRunId, 1, 9);
            healthyAttemptId = healthyAttempt.Id;
            corruptAttemptId = corruptAttempt.Id;
            context.Attempts.AddRange(healthyAttempt, corruptAttempt);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageStopPercent = {stored} WHERE Id = {corruptRunId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {stored} WHERE Id = {corruptAttemptId}");
        }

        await using (var reader = CreateContext())
        {
            var runs = await reader.Runs.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync();
            var attempts = await reader.Attempts.AsNoTracking().Where(a => a.Id == healthyAttemptId || a.Id == corruptAttemptId).ToListAsync();

            Assert.Equal(9, runs.Single(r => r.Id == healthyRunId).ReadCodexAccountUsageStopPercent().Value);
            Assert.True(runs.Single(r => r.Id == corruptRunId).ReadCodexAccountUsageStopPercent().IsMalformed);
            Assert.Equal(9, attempts.Single(a => a.Id == healthyAttemptId).ReadAgentCodexAccountUsageStopPercent().Value);
            Assert.True(attempts.Single(a => a.Id == corruptAttemptId).ReadAgentCodexAccountUsageStopPercent().IsMalformed);
        }

        await using (var saver = CreateContext())
        {
            var corruptRun = await saver.Runs.SingleAsync(r => r.Id == corruptRunId);
            corruptRun.Claim(Now);
            await saver.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(StoredText(stored), await verify.Database
            .SqlQuery<string?>($"SELECT CAST(CodexAccountUsageStopPercent AS TEXT) AS Value FROM runs WHERE Id = {corruptRunId}").SingleAsync());
        Assert.Equal(StoredText(stored), await verify.Database
            .SqlQuery<string?>($"SELECT CAST(AgentCodexAccountUsageStopPercent AS TEXT) AS Value FROM attempts WHERE Id = {corruptAttemptId}").SingleAsync());
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    public async Task Setting_a_valid_value_or_clearing_repairs_a_malformed_stored_run_value(object stored)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Repair", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var run = Run.RecordIntent(runId, projectId, 1, "Objective", Now);
            run.Claim(Now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET CodexAccountUsageStopPercent = {stored} WHERE Id = {runId}");
        }

        await using (var setter = CreateContext())
        {
            var run = await setter.Runs.SingleAsync(r => r.Id == runId);
            Assert.True(run.ReadCodexAccountUsageStopPercent().IsMalformed);
            run.SetCodexAccountUsageStopPercent(15);
            setter.Entry(run).Property(Run.CodexAccountUsageStopStorageProperty).IsModified = true;
            await setter.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(15, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).ReadCodexAccountUsageStopPercent().Value);
    }

    [Fact]
    public async Task A_stale_tracked_run_cannot_overwrite_a_newer_setting_because_the_value_is_a_concurrency_token()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Stale", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var run = Run.RecordIntent(runId, projectId, 1, "Objective", Now);
            run.Claim(Now);
            run.SetCodexAccountUsageStopPercent(40);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
        }

        await using var stale = CreateContext();
        var staleRun = await stale.Runs.SingleAsync(r => r.Id == runId);
        await using (var newer = CreateContext())
        {
            var newerRun = await newer.Runs.SingleAsync(r => r.Id == runId);
            newerRun.SetCodexAccountUsageStopPercent(70);
            await newer.SaveChangesAsync();
        }

        staleRun.SetCodexAccountUsageStopPercent(40);
        stale.Entry(staleRun).Property(Run.CodexAccountUsageStopStorageProperty).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        await using var verify = CreateContext();
        Assert.Equal(70, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).ReadCodexAccountUsageStopPercent().Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    [InlineData("not json")]
    public async Task A_tampered_decision_text_reads_as_unknown_beside_a_healthy_decision(string tampered)
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        Guid healthyId;
        Guid tamperedId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Tamper", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
            var healthy = ClaimCodex(runId, 1, 50);
            var other = ClaimCodex(runId, 2, 50);
            var decision = AgentCodexAccountUsageDecision.Create(
                CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceUnavailable, 50, null, []);
            healthy.CompleteAgentAccountUsageStop(decision, Now);
            other.CompleteAgentAccountUsageStop(decision, Now);
            healthyId = healthy.Id;
            tamperedId = other.Id;
            context.Attempts.AddRange(healthy, other);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentAccountUsageDecisionSnapshot = {tampered} WHERE Id = {tamperedId}");
        }

        await using var reader = CreateContext();
        var rows = await reader.Attempts.AsNoTracking().Where(a => a.RunId == runId).ToListAsync();
        Assert.NotNull(rows.Single(a => a.Id == healthyId).GetAgentAccountUsageDecision());
        Assert.Null(rows.Single(a => a.Id == tamperedId).GetAgentAccountUsageDecision());
        Assert.Equal(AgentOutcome.AccountUsageEvidenceUnavailable, rows.Single(a => a.Id == tamperedId).AgentOutcome);
    }

    [Fact]
    public async Task A_rolled_back_save_leaves_neither_the_stopped_outcome_nor_a_decision()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Rollback", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
            var attempt = ClaimCodex(runId, 1, 60);
            attemptId = attempt.Id;
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
        }

        await using (var failing = CreateContext())
        {
            await using var transaction = await failing.Database.BeginTransactionAsync();
            var attempt = await failing.Attempts.SingleAsync(a => a.Id == attemptId);
            attempt.CompleteAgentAccountUsageStop(
                AgentCodexAccountUsageDecision.Create(
                    CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceUnavailable, 60, null, []),
                Now);
            await failing.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = CreateContext();
        var row = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, row.Status);
        Assert.Null(row.AgentOutcome);
        Assert.Null(row.AgentAccountUsageDecisionSnapshot);
    }

    [Fact]
    public async Task Down_drops_exactly_the_new_columns_without_touching_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Down", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var run = Run.RecordIntent(runId, projectId, 1, "Keep me", Now);
            run.SetRequestedClaudeModelRequest("opus", "high");
            run.SetCodexAccountUsageStopPercent(33);
            context.Runs.Add(run);
            var attempt = ClaimCodex(runId, 1, 33);
            context.Attempts.Add(attempt);
            attemptId = attempt.Id;
            await context.SaveChangesAsync();
        }

        var runColumnsBefore = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        var attemptColumnsBefore = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var runColumnsAfter = (await ReadColumnsAsync("runs")).Select(column => column.Name).ToArray();
        var attemptColumnsAfter = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();
        // The database is at the latest migration, so the later AddCodexAccountUsageWarning run column is dropped on the way down too.
        Assert.Equal(["CodexAccountUsageStopPercent", "CodexAccountUsageWarningPercent"], runColumnsBefore.Except(runColumnsAfter).Order());
        Assert.Empty(runColumnsAfter.Except(runColumnsBefore));
        Assert.Equal(
            ["AgentAccountUsageDecisionSnapshot", "AgentCodexAccountUsageStopPercent"],
            attemptColumnsBefore.Except(attemptColumnsAfter).Order());
        Assert.Empty(attemptColumnsAfter.Except(attemptColumnsBefore));

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var runCommand = probe.CreateCommand();
        runCommand.CommandText = "SELECT Objective, RequestedClaudeModel FROM runs WHERE Id = $id;";
        runCommand.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using (var reader = await runCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Keep me", reader.GetString(0));
            Assert.Equal("opus", reader.GetString(1));
        }

        await using var attemptCommand = probe.CreateCommand();
        attemptCommand.CommandText = "SELECT AgentAdapterContractVersion FROM attempts WHERE Id = $id;";
        attemptCommand.Parameters.AddWithValue("$id", attemptId.ToString().ToUpperInvariant());
        Assert.NotNull(await attemptCommand.ExecuteScalarAsync());
    }
}
