using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the purely additive <c>AddAgentProcessExecutionEvidence</c> migration against a
/// disposable file-backed SQLite database upgraded by the production migrations: attempts recorded
/// before it existed stay valid, keep every assignment fact, outcome, and timestamp, and project
/// their process evidence as truthfully unknown; new evidence round-trips exactly.
/// </summary>
public sealed class AgentProcessEvidenceMigrationTests : IDisposable
{
    private const string PreviousMigration = "AddAgentAssignmentFacts";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-process-evidence-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task Historical_attempts_survive_the_upgrade_with_unknown_evidence_and_every_existing_fact_intact()
    {
        var claimedAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        var dispatchedAt = claimedAt.AddSeconds(5);
        var completedAt = claimedAt.AddMinutes(3);
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var implementationId = Guid.NewGuid();
        var interruptedPlannerId = Guid.NewGuid();
        var processId = Guid.NewGuid();

        await using (var previous = CreateContext())
        {
            await previous.Database.MigrateAsync(PreviousMigration);
            previous.Projects.Add(Project.Register(projectId, "Evidence migration", $@"C:\repos\evidence-migration-{Guid.NewGuid():N}", claimedAt));
            await previous.SaveChangesAsync();
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {projectId}, {1}, {"Historical evidence"}, {nameof(RunLifecycle.Running)},
                        {nameof(RunStage.Execute)}, {nameof(ParticipantKind.None)}, {claimedAt}, {claimedAt}, {0d})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        AgentProvider, AgentRole, AgentProtocolVersion, AgentExpectedMessageType, AgentResponseContract,
                        AgentTimeout, AgentDispatchedAtUtc, AgentOutcome, AgentRequestedModel, AgentObservedModel,
                        AgentRequestedEffort, AgentObservedEffort, AgentPermissionProfile, AgentAdapterContractVersion)
                   VALUES
                       ({implementationId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)},
                        {claimedAt}, {completedAt}, {""}, {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentRole.Implementer)},
                        {"1.0"}, {nameof(CollaborationMessageType.Proposal)}, {nameof(AgentResponseContract.ImplementationReport)},
                        {1_200_000L}, {dispatchedAt}, {nameof(AgentOutcome.Implemented)}, {"requested-model"}, {"observed-model"},
                        {"high"}, {"medium"}, {nameof(AgentPermissionProfile.WorkspaceEditOnly)}, {"claude-implementation-v1"})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        AgentProvider, AgentRole, AgentResponseContract, AgentTimeout, AgentDispatchedAtUtc)
                   VALUES
                       ({interruptedPlannerId}, {runId}, {2}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Interrupted)},
                        {claimedAt}, {completedAt}, {""}, {nameof(AgentProvider.Codex)}, {nameof(AgentRole.Planner)},
                        {nameof(AgentResponseContract.Proposal)}, {600_000L}, {dispatchedAt})");
            await previous.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments,
                        ProcessOutcome, ProcessExitCode, ProcessDispatchedAtUtc)
                   VALUES
                       ({processId}, {runId}, {3}, {nameof(AttemptKind.Process)}, {nameof(AttemptStatus.Failed)},
                        {claimedAt}, {completedAt}, {"[]"}, {nameof(ProcessOutcome.Exited)}, {7}, {dispatchedAt})");
        }

        await using (var upgraded = CreateContext())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Contains(
                (await upgraded.Database.GetAppliedMigrationsAsync()).Select(id => id.Split('_').Last()),
                name => name == "AddAgentProcessExecutionEvidence");
        }

        await using var reopened = CreateContext();

        var implementation = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == implementationId);
        Assert.Equal(AttemptStatus.Completed, implementation.Status);
        Assert.Equal(AgentOutcome.Implemented, implementation.AgentOutcome);
        Assert.Equal(claimedAt, implementation.ClaimedAtUtc);
        Assert.Equal(dispatchedAt, implementation.AgentDispatchedAtUtc);
        Assert.Equal(completedAt, implementation.CompletedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), implementation.AgentTimeout);
        Assert.Equal(AgentRole.Implementer, implementation.AgentRole);
        Assert.Equal(
            new AgentAssignmentSnapshot(
                AgentProvider.ClaudeCode, "requested-model", "observed-model", "high", "medium",
                AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v1"),
            implementation.GetAssignmentSnapshot());
        AssertEvidenceUnknown(implementation);

        var interrupted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == interruptedPlannerId);
        Assert.Equal(AttemptStatus.Interrupted, interrupted.Status);
        Assert.Null(interrupted.AgentOutcome);
        Assert.Equal(dispatchedAt, interrupted.AgentDispatchedAtUtc);
        Assert.Equal(AgentProvider.Codex, interrupted.AgentProvider);
        AssertEvidenceUnknown(interrupted);

        var process = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == processId);
        Assert.Equal(ProcessOutcome.Exited, process.ProcessOutcome);
        Assert.Equal(7, process.ProcessExitCode);
        AssertEvidenceUnknown(process);

        var connection = reopened.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM attempts WHERE AgentProcessOutcome IS NOT NULL OR AgentProcessExitCode IS NOT NULL OR AgentProcessDuration IS NOT NULL";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Evidence_recorded_after_the_upgrade_round_trips_exactly_with_its_outcome()
    {
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        Guid timedOutId;
        Guid cleanExitId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Evidence round trip", $@"C:\repos\evidence-round-trip-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip evidence", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var timedOut = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 1024, 2048, now, 1);
            timedOut.MarkAgentDispatched(now);
            timedOut.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(20),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMilliseconds(1_200_345)));

            var clean = Attempt.ClaimAgent(
                Guid.NewGuid(), run.Id, 2, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 2);
            clean.MarkAgentDispatched(now);
            clean.CompleteAgent(AgentOutcome.Proposed, "fingerprint", now.AddMinutes(1), TestProcessEvidence.CleanExit);

            context.Attempts.AddRange(timedOut, clean);
            await context.SaveChangesAsync();
            timedOutId = timedOut.Id;
            cleanExitId = clean.Id;
        }

        await using var reopened = CreateContext();
        var persistedTimeout = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == timedOutId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, persistedTimeout.AgentOutcome);
        Assert.Equal(
            AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMilliseconds(1_200_345)),
            persistedTimeout.GetAgentProcessExecutionEvidence());

        var persistedClean = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == cleanExitId);
        Assert.Equal(AgentOutcome.Proposed, persistedClean.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, persistedClean.GetAgentProcessExecutionEvidence());
    }

    // Defect-3 regression: a real host-measured TimeSpan can land on a sub-millisecond tick value.
    // AgentProcessDuration must persist the exact tick count, not a value already truncated to
    // whole milliseconds, so this duration must round-trip exactly through a real file-backed
    // SQLite database.
    [Fact]
    public async Task A_non_integral_millisecond_duration_round_trips_exactly()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var subMillisecondDuration = TimeSpan.FromTicks(12_345_678); // 1234.5678 ms — not a whole millisecond.
        Guid attemptId;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Duration precision", $@"C:\repos\duration-precision-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Round trip sub-millisecond duration", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, now, 1);
            attempt.MarkAgentDispatched(now);
            attempt.CompleteAgent(
                AgentOutcome.Proposed, "fingerprint", now.AddSeconds(2),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, subMillisecondDuration));

            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);

        Assert.Equal(subMillisecondDuration, persisted.AgentProcessDuration);
        Assert.Equal(subMillisecondDuration.Ticks, persisted.AgentProcessDuration!.Value.Ticks);
        Assert.Equal(
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, subMillisecondDuration),
            persisted.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public async Task An_inconsistent_persisted_evidence_row_is_projected_as_unknown_rather_than_partially_trusted()
    {
        var now = new DateTimeOffset(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Evidence guard", $@"C:\repos\evidence-guard-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inconsistent evidence", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider,
                        AgentResponseContract, AgentProcessOutcome, AgentProcessExitCode, AgentProcessDuration)
                   VALUES
                       ({attemptId}, {run.Id}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)}, {now}, {""},
                        {nameof(AgentProvider.Codex)}, {nameof(AgentResponseContract.Proposal)},
                        {nameof(ProcessOutcome.TimedOut)}, {9}, {100L})");
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Null(persisted.GetAgentProcessExecutionEvidence());
    }

    private static void AssertEvidenceUnknown(Attempt attempt)
    {
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentProcessExitCode);
        Assert.Null(attempt.AgentProcessDuration);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
