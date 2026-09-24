using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the purely additive <c>AddAgentClaimBudget</c> migration against a disposable file-backed
/// SQLite database upgraded by the production migrations: every historical Agent attempt is
/// backfilled a deterministic, permanent budget slot in real claim order; a historical run whose
/// real Agent-attempt count already exceeds the new default ceiling has its maximum truthfully
/// raised rather than appearing to have violated a budget it was never bound by; Simulated and
/// Process attempts are never assigned a slot; and the new (RunId, AgentBudgetSlot) unique index is
/// enforced for new writes.
/// </summary>
public sealed class AgentClaimBudgetMigrationTests : IDisposable
{
    private const string PreviousMigration = "AddAgentTokenUsageEvidence";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-claim-budget-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Historical_agent_attempts_are_backfilled_deterministic_slots_in_claim_order()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var plannerId = Guid.NewGuid();
        var criticalReviewId = Guid.NewGuid();
        var implementationId = Guid.NewGuid();
        var simulatedId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Budget migration", $@"C:\repos\budget-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical budget"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d})");

            // A Simulated attempt claimed between the two real Agent attempts: it must never be
            // assigned a slot, and it must never occupy a slot number that would otherwise belong
            // to a later Agent attempt (slot assignment is scoped strictly to Kind = 'Agent').
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({plannerId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {claimedAt}, {""})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({simulatedId}, {runId}, {2}, {nameof(AttemptKind.Simulated)}, {nameof(AttemptStatus.Completed)}, {claimedAt}, {""})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({criticalReviewId}, {runId}, {3}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {claimedAt}, {""})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({implementationId}, {runId}, {4}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Interrupted)}, {claimedAt}, {""})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Contains(
                (await upgraded.Database.GetAppliedMigrationsAsync()).Select(id => id.Split('_').Last()),
                name => name == "AddAgentClaimBudget");
        }

        await using var reopened = CreateContext();
        var planner = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == plannerId);
        var simulated = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == simulatedId);
        var criticalReview = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == criticalReviewId);
        var implementation = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == implementationId);

        Assert.Equal(1, planner.AgentBudgetSlot);
        Assert.Null(simulated.AgentBudgetSlot);
        Assert.Equal(2, criticalReview.AgentBudgetSlot);
        Assert.Equal(3, implementation.AgentBudgetSlot);

        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(16, run.MaximumAgentAttempts);
    }

    [Fact]
    public async Task A_run_whose_historical_agent_attempts_already_exceed_the_default_ceiling_has_its_maximum_raised()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        const int historicalAgentAttempts = 20;

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Over-budget migration", $@"C:\repos\over-budget-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Over-budget history"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d})");

            for (var attemptNumber = 1; attemptNumber <= historicalAgentAttempts; attemptNumber++)
            {
                var attemptId = Guid.NewGuid();
                await previous.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                       VALUES ({attemptId}, {runId}, {attemptNumber}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {claimedAt}, {""})");
            }
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
        }

        await using var reopened = CreateContext();
        var run = await reopened.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(historicalAgentAttempts, run.MaximumAgentAttempts);

        var slots = await reopened.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .Select(attempt => attempt.AgentBudgetSlot)
            .ToListAsync();
        Assert.Equal(Enumerable.Range(1, historicalAgentAttempts).Select(number => (int?)number), slots);
    }

    [Fact]
    public async Task New_agent_claims_after_the_upgrade_round_trip_their_slot()
    {
        var now = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);
        Guid firstAttemptId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Budget round trip", $@"C:\repos\budget-round-trip-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip budget", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var firstAttempt = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 1024, 2048, now, agentBudgetSlot: 1);
            context.Attempts.Add(firstAttempt);
            await context.SaveChangesAsync();
            firstAttemptId = firstAttempt.Id;
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == firstAttemptId);
        Assert.Equal(1, persisted.AgentBudgetSlot);
    }

    [Fact]
    public async Task Two_independent_contexts_racing_to_claim_the_same_run_wide_slot_leave_exactly_one_winner()
    {
        var now = new DateTimeOffset(2026, 9, 24, 10, 45, 0, TimeSpan.Zero);
        Guid runId;
        Guid workspaceId;
        Guid checkpointId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Budget slot race", $@"C:\repos\budget-slot-race-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Slot race", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
            workspaceId = Guid.NewGuid();
            checkpointId = Guid.NewGuid();
        }

        // Two independent DbContext instances, each unaware of the other, each building an Agent
        // attempt that claims the SAME AgentBudgetSlot for the SAME run — but at DISTINCT
        // AttemptNumber values, so the only unique constraint either insert can possibly violate
        // is (RunId, AgentBudgetSlot), never the separate (RunId, AttemptNumber) index. Without
        // that isolation, a failure here would be ambiguous evidence — see the companion fact
        // below, which proves the (RunId, AgentBudgetSlot) index specifically (rather than some
        // other constraint) is what makes this fail. Both attempts are completed immediately
        // rather than left Running, so this race also never touches the separate
        // one-Running-attempt-per-run invariant.
        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, "fingerprint", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, now, agentBudgetSlot: 2);
        firstAttempt.Fail(now);
        var secondAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, 3, workspaceId, checkpointId, "fingerprint", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, now, agentBudgetSlot: 2);
        secondAttempt.Fail(now);
        first.Attempts.Add(firstAttempt);
        second.Attempts.Add(secondAttempt);

        var outcomes = await Task.WhenAll(SaveIgnoringUniqueRaceAsync(first), SaveIgnoringUniqueRaceAsync(second));

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        Assert.Equal(1, outcomes.Count(outcome => !outcome));

        await using var verify = CreateContext();
        var slotTwoAttempts = await verify.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId && attempt.AgentBudgetSlot == 2)
            .ToListAsync();
        Assert.Single(slotTwoAttempts);
    }

    /// <summary>
    /// The companion proof for the fact above: with the exact same distinct-AttemptNumber,
    /// same-slot shape, dropping only the <c>ix_attempts_run_id_agent_budget_slot</c> index (and
    /// nothing else) makes both competing claims silently persist side by side — demonstrating
    /// that this specific index, not the (RunId, AttemptNumber) index or any other constraint, is
    /// what the fact above actually depends on to reject the race.
    /// </summary>
    [Fact]
    public async Task Without_the_agent_budget_slot_index_two_competing_claims_for_the_same_slot_would_both_silently_persist()
    {
        var now = new DateTimeOffset(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);
        Guid runId;
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync("DROP INDEX ix_attempts_run_id_agent_budget_slot;");
            var project = Project.Register(Guid.NewGuid(), "Budget slot index canary", $@"C:\repos\budget-slot-canary-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Slot index canary", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        var firstAttempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 2, workspaceId, checkpointId, "fingerprint", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, now, agentBudgetSlot: 2);
        firstAttempt.Fail(now);
        var secondAttempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, 3, workspaceId, checkpointId, "fingerprint", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, now, agentBudgetSlot: 2);
        secondAttempt.Fail(now);

        await using (var first = CreateContext())
        {
            first.Attempts.Add(firstAttempt);
            await first.SaveChangesAsync();
        }

        await using (var second = CreateContext())
        {
            second.Attempts.Add(secondAttempt);
            // With the index dropped, this no longer throws — the exact silent corruption the
            // index exists to prevent.
            await second.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var slotTwoAttempts = await verify.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId && attempt.AgentBudgetSlot == 2)
            .ToListAsync();
        Assert.Equal(2, slotTwoAttempts.Count);
    }

    private static async Task<bool> SaveIgnoringUniqueRaceAsync(DevalCopilotDbContext context)
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

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
