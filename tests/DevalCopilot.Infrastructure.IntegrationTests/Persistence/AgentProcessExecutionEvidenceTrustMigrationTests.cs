using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves, against a real disposable file-backed SQLite database, that
/// <see cref="Attempt.GetAgentProcessExecutionEvidence"/> never trusts a persisted row's process
/// fields while the attempt has not concluded — mirroring
/// <see cref="AgentTokenUsageMigrationTests"/>'s own "still-running"/"never-dispatched" pair, now
/// extended from token-usage evidence to host-measured process-execution evidence. Neither shape
/// below is reachable through this application's own recording path (which only ever writes
/// process-execution fields in the same transaction as a terminal Agent completion transition for a
/// dispatched attempt), so each row is constructed directly via raw SQL rather than the Domain API.
/// </summary>
public sealed class AgentProcessExecutionEvidenceTrustMigrationTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-process-evidence-trust-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task A_still_running_attempts_well_formed_persisted_process_fields_are_never_trusted()
    {
        var now = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Running process guard", $@"C:\repos\running-process-guard-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Running process", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider,
                        AgentResponseContract, AgentDispatchedAtUtc, AgentProcessOutcome, AgentProcessExitCode,
                        AgentProcessDuration)
                   VALUES
                       ({attemptId}, {run.Id}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Running)}, {now}, {""},
                        {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentResponseContract.CriticalReview)}, {now},
                        {nameof(ProcessOutcome.Exited)}, {0}, {12_345_678L})");
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Equal(AttemptStatus.Running, persisted.Status);
        Assert.NotNull(persisted.AgentDispatchedAtUtc);
        Assert.Equal(ProcessOutcome.Exited, persisted.AgentProcessOutcome);
        Assert.Null(persisted.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public async Task An_undispatched_attempts_well_formed_persisted_process_fields_are_never_trusted_even_when_terminal()
    {
        var now = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);
        var attemptId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(
                Guid.NewGuid(), "Undispatched process guard", $@"C:\repos\undispatched-process-guard-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Undispatched process", now);
            context.Projects.Add(project);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, CompletedAtUtc, ProcessArguments, AgentProvider,
                        AgentResponseContract, AgentDispatchedAtUtc, AgentProcessOutcome, AgentProcessExitCode,
                        AgentProcessDuration)
                   VALUES
                       ({attemptId}, {run.Id}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Failed)}, {now}, {now}, {""},
                        {nameof(AgentProvider.ClaudeCode)}, {nameof(AgentResponseContract.CriticalReview)}, {(DateTimeOffset?)null},
                        {nameof(ProcessOutcome.Exited)}, {0}, {12_345_678L})");
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Equal(AttemptStatus.Failed, persisted.Status);
        Assert.Null(persisted.AgentDispatchedAtUtc);
        Assert.Equal(ProcessOutcome.Exited, persisted.AgentProcessOutcome);
        Assert.Null(persisted.GetAgentProcessExecutionEvidence());
    }

    // A genuinely dispatched, terminal attempt's evidence remains trusted and reloads exactly —
    // unaffected by the fix. Covers a nonzero exit code specifically (Exited/TimedOut/Cancelled are
    // already covered through the real Domain API by AgentProcessExecutionEvidenceTests).
    [Fact]
    public async Task A_genuinely_dispatched_terminal_attempts_process_evidence_reloads_exactly()
    {
        var now = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        Guid attemptId;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            var project = Project.Register(Guid.NewGuid(), "Terminal process reload", $@"C:\repos\terminal-process-reload-{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Terminal process", now);
            context.Projects.Add(project);
            context.Runs.Add(run);

            var attempt = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 1024, 2048, now, 1);
            attempt.MarkAgentDispatched(now);
            attempt.CompleteImplementation(
                AgentOutcome.ProviderInvocationFailed, null, now.AddMinutes(1),
                AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 137, TimeSpan.FromSeconds(9)));

            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using var reopened = CreateContext();
        var persisted = await reopened.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Equal(
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 137, TimeSpan.FromSeconds(9)),
            persisted.GetAgentProcessExecutionEvidence());
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);
}
