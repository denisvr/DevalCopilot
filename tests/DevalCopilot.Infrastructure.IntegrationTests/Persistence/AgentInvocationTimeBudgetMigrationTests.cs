using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the purely additive <c>AddAgentInvocationTimeBudget</c> migration against a disposable
/// file-backed SQLite database upgraded by the production migrations: a historical Run predating
/// this decision keeps <c>MaximumAgentInvocationTime</c> truthfully <see langword="null"/> — never
/// backfilled or synthesized, unlike the ADR-0012 count-budget column — while a newly recorded Run
/// created after the upgrade round-trips its own real 120-minute policy.
/// </summary>
public sealed class AgentInvocationTimeBudgetMigrationTests : IDisposable
{
    private const string PreviousMigration = "AddAgentClaimBudget";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-invocation-time-budget-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task A_historical_run_predating_this_decision_keeps_a_truthfully_null_time_budget_policy()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var agentAttemptId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Time budget migration", $@"C:\repos\time-budget-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds, MaximumReviewCorrectionAttempts,
                        MaximumAgentAttempts)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical time budget"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d}, {2}, {16})");
            // A historical Agent attempt with a real, large AgentTimeout — proving the absence of
            // a time-budget policy is never inferred from, or conflated with, its own reserved
            // time evidence.
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentTimeout, AgentBudgetSlot)
                   VALUES ({agentAttemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {claimedAt}, {""}, {1_200_000L}, {1})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Contains(
                (await upgraded.Database.GetAppliedMigrationsAsync()).Select(id => id.Split('_').Last()),
                name => name == "AddAgentInvocationTimeBudget");
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Null(run.MaximumAgentInvocationTime);
    }

    [Fact]
    public async Task A_newly_recorded_run_after_the_upgrade_round_trips_its_own_real_time_budget_policy()
    {
        var now = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        Guid runId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Time budget round trip", $@"C:\repos\time-budget-round-trip-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip time budget", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == runId);
        Assert.Equal(TimeSpan.FromMinutes(120), persisted.MaximumAgentInvocationTime);
    }

    [Fact]
    public async Task A_non_integral_millisecond_time_budget_round_trips_exactly_after_a_restart()
    {
        // 12,345 ticks = 1.2345 ms: not a whole number of milliseconds, so a millisecond-truncating
        // conversion (e.g. (long)TimeSpan.TotalMilliseconds) would silently lose precision here.
        var nonIntegralMillisecondBudget = TimeSpan.FromTicks(TimeSpan.FromMinutes(120).Ticks + 12_345);
        var now = new DateTimeOffset(2026, 9, 25, 11, 0, 0, TimeSpan.Zero);
        Guid runId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Time budget precision", $@"C:\repos\time-budget-precision-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(
                Guid.NewGuid(),
                project.Id,
                1,
                "Non-integral millisecond time budget",
                now,
                maximumAgentInvocationTime: nonIntegralMillisecondBudget);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Runs.AsNoTracking().SingleAsync(run => run.Id == runId);
        Assert.Equal(nonIntegralMillisecondBudget, persisted.MaximumAgentInvocationTime);
        Assert.Equal(nonIntegralMillisecondBudget.Ticks, persisted.MaximumAgentInvocationTime!.Value.Ticks);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
