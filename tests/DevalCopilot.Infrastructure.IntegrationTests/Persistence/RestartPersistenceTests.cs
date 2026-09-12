using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the walking-skeleton acceptance criterion "closing and reopening the
/// application preserves the run" against the real file-backed database, not an
/// in-memory approximation of it.
/// </summary>
public sealed class RestartPersistenceTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    [Fact]
    public async Task A_run_completed_before_host_shutdown_is_readable_after_reopening_the_same_database_file()
    {
        var now = DateTimeOffset.UtcNow;
        Guid runId;

        // First "process lifetime": create and complete a run, then dispose the context
        // and clear the SQLite connection pool to simulate the host process exiting.
        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\restart-test");
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Survive a restart", now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;

            run.Claim(now);
            var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, attemptNumber: 1, now);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();

            run.Complete(now);
            attempt.Complete(now);
            context.Events.Add(
                RunEvent.Record(Guid.NewGuid(), run.Id, attempt.Id, RunEventType.RunCompleted, ParticipantKind.Orchestrator, "{}", now));
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        // Second "process lifetime": a fresh context against the same on-disk file, as a
        // restarted host would open.
        await using var reopenedContext = fixture.CreateContext();
        var persistedRun = await reopenedContext.Runs.FindAsync(runId);

        Assert.NotNull(persistedRun);
        Assert.Equal(RunLifecycle.Completed, persistedRun.Lifecycle);
        Assert.Equal(RunStage.Completed, persistedRun.Stage);

        var persistedEventCount = reopenedContext.Events.Count(runEvent => runEvent.RunId == runId);
        Assert.Equal(1, persistedEventCount);
    }
}
