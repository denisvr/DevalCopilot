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
/// Proves <c>AddCodexPlanningRepairLink</c> is purely additive and that its database backstops
/// hold: the new link column is nullable with no default and no backfill, every historical attempt
/// keeps its data and existing indexes, the link is a real foreign key, at most one attempt can
/// repair a given source (even under a concurrent race), and the migration round-trips.
/// </summary>
public sealed class AddCodexPlanningRepairLinkMigrationTests : IAsyncLifetime
{
    private const string PriorMigration = "20260929104032_AddClaudeEffortPreference";
    private const string ThisMigration = "20260929144212_AddCodexPlanningRepairLink";
    private const string RepairIndex = "ix_attempts_agent_repair_source";

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-repair-link-migration-{Guid.NewGuid():N}.db");

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

    private static Attempt ClaimPlanner(Guid runId, int number) => Attempt.ClaimAgent(
        Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, number);

    /// <summary>A repair claim; `terminal` completes it so the run-wide one-Running-attempt index
    /// cannot be what rejects a second repair in the tests that isolate the repair-source index.</summary>
    private static Attempt ClaimRepair(Guid runId, int number, Guid sourceId, bool terminal = true)
    {
        var repair = Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, number, sourceId);
        if (terminal)
        {
            repair.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        }

        return repair;
    }

    /// <summary>Seeds a project, a run, and one terminal planner attempt at the latest schema.</summary>
    private async Task<(Guid RunId, Guid SourceId)> SeedSourceAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var project = Project.Register(Guid.NewGuid(), "Repair link", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan", Now, maximumAgentAttempts: 16);
        var source = ClaimPlanner(run.Id, 1);
        source.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.Attempts.Add(source);
        await context.SaveChangesAsync();
        return (run.Id, source.Id);
    }

    private static async Task<List<string>> AttemptIndexNamesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_index_list('attempts') ORDER BY name;";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task Historical_attempts_keep_their_data_and_indexes_and_get_no_invented_link()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var plannerId = Guid.NewGuid();
        var simulatedId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            previous.Projects.Add(Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical run"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Critique)}, {nameof(ParticipantKind.None)}, {Now}, {Now}, {0d})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentBudgetSlot)
                   VALUES ({plannerId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)}, {Now}, {""}, {1})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({simulatedId}, {runId}, {2}, {nameof(AttemptKind.Simulated)}, {nameof(AttemptStatus.Running)}, {Now}, {""})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Contains(ThisMigration, await upgraded.Database.GetAppliedMigrationsAsync());
        }

        await using var reopened = CreateContext();
        var attempts = await reopened.Attempts.AsNoTracking().Where(a => a.RunId == runId).OrderBy(a => a.AttemptNumber).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Null(attempt.AgentRepairSourceAttemptId));
        Assert.Equal(AttemptStatus.Failed, attempts[0].Status);
        Assert.Equal(1, attempts[0].AgentBudgetSlot);
        Assert.Equal(AttemptStatus.Running, attempts[1].Status);

        // The table rebuild SQLite needs for the new foreign key lost none of the earlier indexes.
        var indexes = await AttemptIndexNamesAsync(_databasePath);
        Assert.Contains(RepairIndex, indexes);
        Assert.Contains("ix_attempts_run_id_agent_budget_slot", indexes);
        Assert.Contains("ix_attempts_run_id_one_running", indexes);
        Assert.Contains(indexes, name => name.Contains("RunId_AttemptNumber", StringComparison.Ordinal));

        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText =
            "SELECT \"notnull\", dflt_value FROM pragma_table_info('attempts') WHERE name = 'AgentRepairSourceAttemptId';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task At_most_one_attempt_can_repair_a_source_and_unlinked_attempts_are_unrestricted()
    {
        var (runId, sourceId) = await SeedSourceAsync();

        await using (var first = CreateContext())
        {
            first.Attempts.Add(ClaimRepair(runId, 2, sourceId));
            await first.SaveChangesAsync();
        }

        await using (var second = CreateContext())
        {
            second.Attempts.Add(ClaimRepair(runId, 3, sourceId));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Contains("UNIQUE constraint failed", failure.InnerException?.Message);
            Assert.Contains("attempts.AgentRepairSourceAttemptId", failure.InnerException?.Message);
        }

        // Many attempts with no source (the ordinary case) never collide on the filtered index.
        await using (var ordinary = CreateContext())
        {
            ordinary.Attempts.Add(ClaimPlanner(runId, 4));
            await ordinary.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId == sourceId));
        Assert.Equal(2, await verify.Attempts.CountAsync(a => a.RunId == runId && a.AgentRepairSourceAttemptId == null));
    }

    [Fact]
    public async Task A_concurrent_pair_of_repair_inserts_for_one_source_commits_exactly_one()
    {
        var (runId, sourceId) = await SeedSourceAsync();
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        firstContext.Attempts.Add(ClaimRepair(runId, 2, sourceId));
        secondContext.Attempts.Add(ClaimRepair(runId, 3, sourceId));

        var outcomes = await Task.WhenAll(TrySaveAsync(firstContext), TrySaveAsync(secondContext));

        Assert.Equal(1, outcomes.Count(saved => saved));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId == sourceId));

        static async Task<bool> TrySaveAsync(DevalCopilotDbContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task The_link_is_a_real_foreign_key_and_a_source_referenced_by_a_repair_cannot_be_deleted()
    {
        var (runId, sourceId) = await SeedSourceAsync();

        await using (var dangling = CreateContext())
        {
            dangling.Attempts.Add(ClaimRepair(runId, 2, Guid.NewGuid()));
            await Assert.ThrowsAsync<DbUpdateException>(() => dangling.SaveChangesAsync());
        }

        await using (var linked = CreateContext())
        {
            linked.Attempts.Add(ClaimRepair(runId, 2, sourceId));
            await linked.SaveChangesAsync();
        }

        await using var deleting = CreateContext();
        var blocked = await Assert.ThrowsAsync<SqliteException>(() =>
            deleting.Attempts.Where(a => a.Id == sourceId).ExecuteDeleteAsync());
        Assert.Contains("FOREIGN KEY constraint failed", blocked.Message);
    }

    [Fact]
    public async Task Deleting_a_run_cascades_to_a_source_and_its_repair_while_deleting_the_source_alone_stays_blocked()
    {
        var (runId, sourceId) = await SeedSourceAsync();
        await using (var linked = CreateContext())
        {
            linked.Attempts.Add(ClaimRepair(runId, 2, sourceId));
            await linked.SaveChangesAsync();
        }

        await using (var probe = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await probe.OpenAsync();
            await using var command = probe.CreateCommand();
            command.CommandText =
                "SELECT on_delete FROM pragma_foreign_key_list('attempts') WHERE \"from\" = 'AgentRepairSourceAttemptId';";
            Assert.Equal("NO ACTION", (string?)await command.ExecuteScalarAsync());
        }

        await using (var alone = CreateContext())
        {
            var blocked = await Assert.ThrowsAsync<SqliteException>(() =>
                alone.Attempts.Where(a => a.Id == sourceId).ExecuteDeleteAsync());
            Assert.Contains("FOREIGN KEY constraint failed", blocked.Message);
        }

        await using (var cascading = CreateContext())
        {
            Assert.Equal(1, await cascading.Runs.Where(r => r.Id == runId).ExecuteDeleteAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(0, await verify.Attempts.CountAsync(a => a.RunId == runId));
        Assert.Equal(0, await verify.Runs.CountAsync(r => r.Id == runId));
    }

    [Fact]
    public async Task The_migration_round_trips_down_and_up_without_losing_attempts()
    {
        var (runId, sourceId) = await SeedSourceAsync();
        await using (var linked = CreateContext())
        {
            linked.Attempts.Add(ClaimRepair(runId, 2, sourceId));
            await linked.SaveChangesAsync();
        }

        await using (var down = CreateContext())
        {
            await down.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
            Assert.DoesNotContain(ThisMigration, await down.Database.GetAppliedMigrationsAsync());
            var count = await down.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM attempts WHERE RunId = {runId}").SingleAsync();
            Assert.Equal(2, count);
        }

        Assert.DoesNotContain(RepairIndex, await AttemptIndexNamesAsync(_databasePath));

        await using var up = CreateContext();
        await up.Database.MigrateAsync();
        Assert.Contains(RepairIndex, await AttemptIndexNamesAsync(_databasePath));
        Assert.Equal(2, await up.Attempts.CountAsync(a => a.RunId == runId));
        Assert.Equal(0, await up.Attempts.CountAsync(a => a.AgentRepairSourceAttemptId != null));
    }
}
