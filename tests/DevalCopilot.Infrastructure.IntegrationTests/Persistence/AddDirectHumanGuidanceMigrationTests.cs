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
/// Proves <c>AddDirectHumanGuidance</c> is truthful and additive against the real parent schema: it adds one nullable
/// TEXT column without a default or backfill, so every historical attempt stays null (never inferred from artifacts,
/// authorizations, or messages), nothing else changes, unrelated saves preserve the stored value exactly (even an
/// out-of-band malformed one), a fresh database round-trips accepted guidance, and the reverse migration drops only
/// that column.
/// </summary>
public sealed class AddDirectHumanGuidanceMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20261001105945_AddRunExecutionMode";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-direct-guidance-migration-{Guid.NewGuid():N}.db");

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

    private static async Task InsertAgentAttemptAsync(
        DevalCopilotDbContext context, Guid runId, Guid attemptId, int number, string role, string contract, string version)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO attempts
                   (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider, AgentRole,
                    AgentResponseContract, AgentPermissionProfile, AgentAdapterContractVersion)
               VALUES
                   ({attemptId}, {runId}, {number}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {Now}, {""},
                    {nameof(AgentProvider.ClaudeCode)}, {role}, {contract}, {nameof(AgentPermissionProfile.WorkspaceEditOnly)}, {version})");
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

    private async Task<string?> ReadRawGuidanceAsync(Guid attemptId)
    {
        await using var context = CreateContext();
        return await context.Database
            .SqlQuery<string?>($"SELECT AgentDirectHumanGuidance AS Value FROM attempts WHERE Id = {attemptId}")
            .SingleAsync();
    }

    [Fact]
    public async Task Every_historical_attempt_stays_null_without_inference_and_keeps_all_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var v1Implementation = Guid.NewGuid();
        var v2Implementation = Guid.NewGuid();
        var v1Correction = Guid.NewGuid();
        var v2Correction = Guid.NewGuid();
        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, MaximumReviewCorrectionAttempts,
                        MaximumAgentAttempts)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical run"}, {nameof(RunLifecycle.Running)}, {nameof(RunStage.Execute)},
                        {nameof(ParticipantKind.None)}, {Now}, {Now}, {0d}, {2}, {16})");
            await InsertAgentAttemptAsync(previous, runId, v1Implementation, 1, nameof(AgentRole.Implementer), nameof(AgentResponseContract.ImplementationReport), ClaudeMutationAdapterContract.ImplementationV1);
            await InsertAgentAttemptAsync(previous, runId, v2Implementation, 2, nameof(AgentRole.Implementer), nameof(AgentResponseContract.ImplementationReport), ClaudeMutationAdapterContract.ImplementationV2);
            await InsertAgentAttemptAsync(previous, runId, v1Correction, 3, nameof(AgentRole.Implementer), nameof(AgentResponseContract.ReviewCorrection), ClaudeMutationAdapterContract.ReviewCorrectionV1);
            await InsertAgentAttemptAsync(previous, runId, v2Correction, 4, nameof(AgentRole.Implementer), nameof(AgentResponseContract.ReviewCorrection), ClaudeMutationAdapterContract.ReviewCorrectionV2);
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        foreach (var attemptId in new[] { v1Implementation, v2Implementation, v1Correction, v2Correction })
        {
            Assert.Null(await ReadRawGuidanceAsync(attemptId));
        }

        await using var reopened = CreateContext();
        var attempts = await reopened.Attempts.AsNoTracking().Where(a => a.RunId == runId).OrderBy(a => a.AttemptNumber).ToListAsync();
        Assert.Equal(4, attempts.Count);
        Assert.Equal(
            [DirectHumanGuidanceEvidence.NotRecorded, DirectHumanGuidanceEvidence.NotRecorded, DirectHumanGuidanceEvidence.NotRecorded, DirectHumanGuidanceEvidence.NotRecorded],
            attempts.Select(a => a.GetDirectHumanGuidanceEvidence()));
        Assert.All(attempts, a => Assert.True(a.HasDispatchCoherentDirectHumanGuidance()));
        Assert.Equal(0, await reopened.Artifacts.CountAsync());
        Assert.Equal(0, await reopened.CollaborationMessages.CountAsync());
        Assert.Equal(0, await reopened.ReviewCorrectionAuthorizations.CountAsync());
        Assert.Equal(1, await reopened.Runs.CountAsync());
    }

    [Fact]
    public async Task The_new_column_is_a_nullable_text_column_without_a_default()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        var column = Assert.Single(await ReadColumnsAsync("attempts"), candidate => candidate.Name == "AgentDirectHumanGuidance");
        Assert.Equal("TEXT", column.Type);
        Assert.False(column.NotNull);
        Assert.Null(column.Default);
        await using var model = CreateContext();
        Assert.False(model.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task A_fresh_database_round_trips_accepted_guidance_and_unrelated_saves_preserve_even_a_malformed_value_exactly()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var guidedId = Guid.NewGuid();
        var corruptedId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Fresh", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Objective", Now));
            context.Runs.Add(Run.RecordIntent(secondRunId, projectId, 2, "Second", Now));
            context.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
                guidedId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, null, "Prefer the existing helper.\nKeep it small."));
            context.Attempts.Add(Attempt.ClaimAgentReviewCorrectionWithModelRequest(
                corruptedId, secondRunId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, 1, null, "Accepted text."));
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentDirectHumanGuidance = {" padded, malformed "} WHERE Id = {corruptedId}");
        }

        Assert.Equal("Prefer the existing helper.\nKeep it small.", await ReadRawGuidanceAsync(guidedId));

        // An unrelated save of each tracked attempt (a write-once provider observation) must not rewrite the column.
        await using (var unrelated = CreateContext())
        {
            foreach (var attempt in await unrelated.Attempts.Where(a => a.RunId == runId || a.RunId == secondRunId).ToListAsync())
            {
                attempt.RecordAgentObservedAssignment("opus", "high");
            }

            await unrelated.SaveChangesAsync();
        }

        Assert.Equal("Prefer the existing helper.\nKeep it small.", await ReadRawGuidanceAsync(guidedId));
        Assert.Equal(" padded, malformed ", await ReadRawGuidanceAsync(corruptedId));
        await using var reopened = CreateContext();
        var corrupted = await reopened.Attempts.AsNoTracking().SingleAsync(a => a.Id == corruptedId);
        Assert.True(corrupted.ReadAgentDirectHumanGuidance().IsMalformed);
        Assert.Equal(DirectHumanGuidanceEvidence.Unknown, corrupted.GetDirectHumanGuidanceEvidence());
    }

    [Fact]
    public async Task Down_drops_only_the_new_column_without_touching_other_data()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Projects.Add(Project.Register(projectId, "Down", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Keep me", Now));
            context.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, AgentPermissionProfile.WorkspaceEditOnly,
                ClaudeMutationAdapterContract.ImplementationV2, 1, null, "Guided."));
            await context.SaveChangesAsync();
        }

        var before = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();

        await using (var downgrade = CreateContext())
        {
            await downgrade.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var after = (await ReadColumnsAsync("attempts")).Select(column => column.Name).ToArray();
        // The database is at the latest migration, so the later AddAgentModelContextLimits, AddCodexAccountUsageStop and AddCodexAccountUsageWarning columns are dropped on the way down too.
        Assert.Equal(
            ["AgentAccountUsageDecisionSnapshot", "AgentCodexAccountUsageStopPercent", "AgentDirectHumanGuidance", "AgentModelContextLimitsSnapshot"],
            before.Except(after).Order());
        Assert.Empty(after.Except(before));

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT AgentAdapterContractVersion FROM attempts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", attemptId.ToString().ToUpperInvariant());
        Assert.Equal(ClaudeMutationAdapterContract.ImplementationV2, (string?)await command.ExecuteScalarAsync());
    }
}
