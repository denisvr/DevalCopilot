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
/// Proves <c>AddClaudeMutationTurnLimit</c> is truthful: it only adds two nullable INTEGER columns with no
/// default and no backfill, so every run and attempt that predates it keeps <see langword="null"/> (no
/// request recorded) and is never given an invented turn limit; the values round-trip through the real
/// EF model; and the reverse migration drops exactly those two columns.
/// </summary>
public sealed class AddClaudeMutationTurnLimitMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20260929173534_AddRunTokenStopThresholds";

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-mutation-turn-limit-migration-{Guid.NewGuid():N}.db");

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

    private static async Task InsertLegacyRunAsync(DevalCopilotDbContext context, Guid projectId, Guid runId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO runs
                   (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                    CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, RequestedClaudeModel, RequestedClaudeEffort)
               VALUES
                   ({runId}, {projectId}, {1}, {"Historical run"},
                    {nameof(RunLifecycle.Running)}, {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)},
                    {Now}, {Now}, {0d}, {"opus"}, {"high"})");
    }

    private static async Task InsertLegacyAttemptAsync(DevalCopilotDbContext context, Guid runId, Guid attemptId, int number)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO attempts
                   (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole,
                    AgentResponseContract, AgentPermissionProfile, AgentAdapterContractVersion, AgentRequestedModel)
               VALUES
                   ({attemptId}, {runId}, {number}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)},
                    {Now}, {""}, {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentRole.Implementer)},
                    {nameof(AgentResponseContract.ImplementationReport)}, {nameof(AgentPermissionProfile.WorkspaceEditOnly)},
                    {"claude-implementation-v1"}, {"sonnet"})");
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
    public async Task Runs_and_attempts_that_predate_the_migration_keep_null_in_both_new_columns_and_their_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await InsertLegacyRunAsync(previous, projectId, runId);
            await InsertLegacyAttemptAsync(previous, runId, attemptId, 1);
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Null(run.RequestedClaudeMaxTurns);
        Assert.Equal("opus", run.RequestedClaudeModel);
        Assert.Equal("high", run.RequestedClaudeEffort);

        var attempt = await reopened.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Null(attempt.AgentRequestedMaxTurns);
        Assert.Equal("claude-implementation-v1", attempt.AgentAdapterContractVersion);
        Assert.Equal("sonnet", attempt.AgentRequestedModel);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRecorded, attempt.GetMutationTurnLimitEvidence());
        Assert.Null(attempt.GetAssignmentSnapshot()!.RequestedMaxTurns);
    }

    [Fact]
    public async Task Both_new_columns_are_nullable_integers_without_a_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var runColumn = Assert.Single(await ReadColumnsAsync("runs"), column => column.Name == "RequestedClaudeMaxTurns");
        Assert.Equal("INTEGER", runColumn.Type);
        Assert.False(runColumn.NotNull);
        Assert.False(runColumn.HasDefault);

        var attemptColumn = Assert.Single(await ReadColumnsAsync("attempts"), column => column.Name == "AgentRequestedMaxTurns");
        Assert.Equal("INTEGER", attemptColumn.Type);
        Assert.False(attemptColumn.NotNull);
        Assert.False(attemptColumn.HasDefault);
    }

    [Fact]
    public async Task A_limit_and_null_round_trip_through_the_real_model_on_a_migrated_database()
    {
        var projectId = Guid.NewGuid();
        var limitedRunId = Guid.NewGuid();
        var unlimitedRunId = Guid.NewGuid();
        var correctionRunId = Guid.NewGuid();
        Guid limitedAttemptId;
        Guid unlimitedAttemptId;
        Guid correctionAttemptId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Round trip", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var limited = Run.RecordIntent(limitedRunId, projectId, 1, "Limited", Now);
            limited.SetRequestedClaudeMaxTurns(12);
            var unlimited = Run.RecordIntent(unlimitedRunId, projectId, 2, "Unlimited", Now);
            var correctionRun = Run.RecordIntent(correctionRunId, projectId, 3, "Correction", Now);
            context.Runs.AddRange(limited, unlimited, correctionRun);

            var limitedAttempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), limitedRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, "opus", "high", AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 100);
            var unlimitedAttempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), unlimitedRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: null);
            var correctionAttempt = Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                Guid.NewGuid(), correctionRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, 1, requestedMaxTurns: 1);
            limitedAttemptId = limitedAttempt.Id;
            unlimitedAttemptId = unlimitedAttempt.Id;
            correctionAttemptId = correctionAttempt.Id;
            context.Attempts.AddRange(limitedAttempt, unlimitedAttempt, correctionAttempt);
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal(12, (await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == limitedRunId)).RequestedClaudeMaxTurns);
        Assert.Null((await reopened.Runs.AsNoTracking().SingleAsync(r => r.Id == unlimitedRunId)).RequestedClaudeMaxTurns);
        var limitedAttemptRow = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == limitedAttemptId);
        Assert.Equal(100, limitedAttemptRow.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.Requested, limitedAttemptRow.GetMutationTurnLimitEvidence());
        var unlimitedAttemptRow = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == unlimitedAttemptId);
        Assert.Null(unlimitedAttemptRow.AgentRequestedMaxTurns);
        Assert.Equal(ClaudeMutationTurnLimitEvidence.NotRequested, unlimitedAttemptRow.GetMutationTurnLimitEvidence());
        var correctionRow = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == correctionAttemptId);
        Assert.Equal(1, correctionRow.AgentRequestedMaxTurns);
        Assert.Equal("claude-review-correction-v2", correctionRow.AgentAdapterContractVersion);
    }

    [Fact]
    public async Task The_model_maps_both_columns_as_exact_stored_text_with_the_run_value_a_concurrency_token()
    {
        await using var context = CreateContext();

        var runProperty = context.Model.FindEntityType(typeof(Run))!.FindProperty(Run.RequestedClaudeMaxTurnsStorageProperty)!;
        Assert.True(runProperty.IsConcurrencyToken);
        Assert.True(runProperty.IsNullable);
        Assert.Equal(typeof(string), runProperty.ClrType);
        Assert.Equal("RequestedClaudeMaxTurns", runProperty.GetColumnName());
        Assert.Equal("INTEGER", runProperty.GetColumnType());

        var attemptProperty = context.Model.FindEntityType(typeof(Attempt))!.FindProperty(Attempt.AgentRequestedMaxTurnsStorageProperty)!;
        Assert.False(attemptProperty.IsConcurrencyToken);
        Assert.True(attemptProperty.IsNullable);
        Assert.Equal(typeof(string), attemptProperty.ClrType);
        Assert.Equal("AgentRequestedMaxTurns", attemptProperty.GetColumnName());
        Assert.Equal("INTEGER", attemptProperty.GetColumnType());

        Assert.Null(context.Model.FindEntityType(typeof(Run))!.FindProperty(nameof(Run.RequestedClaudeMaxTurns)));
        Assert.Null(context.Model.FindEntityType(typeof(Attempt))!.FindProperty(nameof(Attempt.AgentRequestedMaxTurns)));
    }

    [Fact]
    public async Task A_valid_limit_is_stored_as_a_sqlite_integer_not_text()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Storage class", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var run = Run.RecordIntent(runId, projectId, 1, "Objective", Now);
            run.SetRequestedClaudeMaxTurns(42);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var storageClass = await verify.Database
            .SqlQuery<string>($"SELECT typeof(RequestedClaudeMaxTurns) AS Value FROM runs WHERE Id = {runId}")
            .SingleAsync();
        Assert.Equal("integer", storageClass);
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
    public async Task A_malformed_stored_value_reads_as_malformed_beside_healthy_siblings_and_round_trips_exactly(object stored)
    {
        var projectId = Guid.NewGuid();
        var healthyRunId = Guid.NewGuid();
        var corruptRunId = Guid.NewGuid();
        var healthyAttemptId = Guid.NewGuid();
        var corruptAttemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Malformed storage", $@"C:\repos\{Guid.NewGuid():N}", Now));
            var healthyRun = Run.RecordIntent(healthyRunId, projectId, 1, "Healthy", Now);
            healthyRun.SetRequestedClaudeMaxTurns(9);
            context.Runs.AddRange(healthyRun, Run.RecordIntent(corruptRunId, projectId, 2, "Corrupt", Now));
            context.Attempts.AddRange(
                Attempt.ClaimAgentImplementationWithAssignment(
                    healthyAttemptId, healthyRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                    TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                    ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 9),
                Attempt.ClaimAgentImplementationWithAssignment(
                    corruptAttemptId, corruptRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                    TimeSpan.FromMinutes(20), 65536, 131072, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                    ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 9));
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET RequestedClaudeMaxTurns = {stored} WHERE Id = {corruptRunId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRequestedMaxTurns = {stored} WHERE Id = {corruptAttemptId}");
        }

        await using (var reader = CreateContext())
        {
            var runs = await reader.Runs.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync();
            var attempts = await reader.Attempts.AsNoTracking().Where(a => a.Id == healthyAttemptId || a.Id == corruptAttemptId).ToListAsync();

            Assert.Equal(9, runs.Single(r => r.Id == healthyRunId).RequestedClaudeMaxTurns);
            Assert.True(runs.Single(r => r.Id == corruptRunId).ReadRequestedClaudeMaxTurns().IsMalformed);
            Assert.Throws<InvalidOperationException>(() => runs.Single(r => r.Id == corruptRunId).RequestedClaudeMaxTurns);
            Assert.Equal(9, attempts.Single(a => a.Id == healthyAttemptId).AgentRequestedMaxTurns);
            Assert.True(attempts.Single(a => a.Id == corruptAttemptId).ReadAgentRequestedMaxTurns().IsMalformed);
            Assert.Equal(ClaudeMutationTurnLimitEvidence.Unknown, attempts.Single(a => a.Id == corruptAttemptId).GetMutationTurnLimitEvidence());
        }

        // The malformed value is preserved exactly and never rewritten as a different number by an unrelated save.
        await using (var saver = CreateContext())
        {
            var corruptRun = await saver.Runs.SingleAsync(r => r.Id == corruptRunId);
            corruptRun.Claim(Now);
            await saver.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var raw = await verify.Database
            .SqlQuery<string?>($"SELECT CAST(RequestedClaudeMaxTurns AS TEXT) AS Value FROM runs WHERE Id = {corruptRunId}")
            .SingleAsync();
        Assert.Equal(StoredText(stored), raw);
        if (stored is byte[])
        {
            Assert.Equal("blob", await verify.Database.SqlQuery<string>($"SELECT typeof(RequestedClaudeMaxTurns) AS Value FROM runs WHERE Id = {corruptRunId}").SingleAsync());
        }
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(4294967297L)]
    [InlineData("abc")]
    [MemberData(nameof(StorageClassCases))]
    public async Task Setting_a_valid_request_repairs_a_malformed_stored_run_value(object stored)
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
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET RequestedClaudeMaxTurns = {stored} WHERE Id = {runId}");
        }

        await using (var setter = CreateContext())
        {
            var run = await setter.Runs.SingleAsync(r => r.Id == runId);
            Assert.True(run.ReadRequestedClaudeMaxTurns().IsMalformed);
            run.SetRequestedClaudeMaxTurns(15);
            setter.Entry(run).Property(Run.RequestedClaudeMaxTurnsStorageProperty).IsModified = true;
            await setter.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(15, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).RequestedClaudeMaxTurns);
    }

    [Fact]
    public async Task Down_drops_exactly_the_two_new_columns_without_touching_other_data()
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
            run.SetRequestedClaudeMaxTurns(9);
            context.Runs.Add(run);
            context.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, "sonnet", null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, requestedMaxTurns: 9));
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
        // The database is at the latest migration, so the later AddRunExecutionMode and AddDirectHumanGuidance columns are dropped on the way down too.
        Assert.Equal(["ExecutionMode", "RequestedClaudeMaxTurns"], runColumnsBefore.Except(runColumnsAfter).Order());
        Assert.Empty(runColumnsAfter.Except(runColumnsBefore));
        Assert.Equal(["AgentDirectHumanGuidance", "AgentRequestedMaxTurns"], attemptColumnsBefore.Except(attemptColumnsAfter).Order());
        Assert.Empty(attemptColumnsAfter.Except(attemptColumnsBefore));

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var runCommand = probe.CreateCommand();
        runCommand.CommandText = "SELECT Objective, RequestedClaudeModel, RequestedClaudeEffort FROM runs WHERE Id = $id;";
        runCommand.Parameters.AddWithValue("$id", runId.ToString().ToUpperInvariant());
        await using (var reader = await runCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Keep me", reader.GetString(0));
            Assert.Equal("opus", reader.GetString(1));
            Assert.Equal("high", reader.GetString(2));
        }

        await using var attemptCommand = probe.CreateCommand();
        attemptCommand.CommandText =
            "SELECT AgentRequestedModel, AgentAdapterContractVersion FROM attempts WHERE Id = $id;";
        attemptCommand.Parameters.AddWithValue("$id", attemptId.ToString().ToUpperInvariant());
        await using var attemptReader = await attemptCommand.ExecuteReaderAsync();
        Assert.True(await attemptReader.ReadAsync());
        Assert.Equal("sonnet", attemptReader.GetString(0));
        Assert.Equal("claude-implementation-v2", attemptReader.GetString(1));
    }
}
