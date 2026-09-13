using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves restart reconciliation against the real file-backed database: a Process attempt
/// left <see cref="AttemptStatus.Running"/> when the "process" disappears — simulated here by
/// disposing the context and clearing the connection pool without ever recording a result —
/// and its run become <see cref="AttemptStatus.Interrupted"/> / <see cref="RunLifecycle.Interrupted"/>
/// together after reopening the same database file.
/// </summary>
public sealed class ProcessAttemptRestartReconciliationTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    private static readonly DateTimeOffset ClaimedAt = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReconciledAt = new(2026, 9, 13, 9, 5, 0, TimeSpan.Zero);

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    [Fact]
    public async Task A_process_attempt_still_running_when_the_host_disappears_is_interrupted_together_with_its_run_after_reopening()
    {
        Guid runId;
        Guid attemptId;

        // First "process lifetime": claim a Process attempt and never record a result for it
        // — exactly what a crash mid-execution leaves behind.
        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\reconciliation-test");
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Orphaned by a crash", ClaimedAt);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;

            run.Claim(ClaimedAt);
            var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), ClaimedAt);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        SqliteConnection.ClearAllPools();

        // Second "process lifetime": a fresh context against the same on-disk file, as a
        // restarted host's startup reconciliation would open before any supervisor claims
        // new work.
        await using var reopenedContext = fixture.CreateContext();
        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(reopenedContext, new FixedTimeProvider(ReconciledAt));

        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);
        await reopenedContext.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        SqliteConnection.ClearAllPools();

        await using var verificationContext = fixture.CreateContext();
        var persistedAttempt = await verificationContext.Attempts.FindAsync(attemptId);
        var persistedRun = await verificationContext.Runs.FindAsync(runId);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(AttemptStatus.Interrupted, persistedAttempt.Status);
        Assert.NotNull(persistedRun);
        Assert.Equal(RunLifecycle.Interrupted, persistedRun.Lifecycle);
    }

    [Fact]
    public async Task A_process_attempt_dispatched_but_never_recorded_is_interrupted_together_with_its_run_after_reopening()
    {
        Guid runId;
        Guid attemptId;

        // First "process lifetime": claim the attempt, durably mark it dispatched (as the
        // supervisor does immediately before invoking the adapter), then disappear without
        // ever recording a terminal result — exactly what a stalled or failed recording
        // transaction leaves behind, distinct from a crash before dispatch ever happened.
        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\dispatched-reconciliation-test");
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Dispatched but never recorded", ClaimedAt);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;

            run.Claim(ClaimedAt);
            var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), ClaimedAt);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();
            attemptId = attempt.Id;

            attempt.MarkProcessDispatched(ClaimedAt.AddSeconds(1));
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using var reopenedContext = fixture.CreateContext();
        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(reopenedContext, new FixedTimeProvider(ReconciledAt));

        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);
        await reopenedContext.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        SqliteConnection.ClearAllPools();

        await using var verificationContext = fixture.CreateContext();
        var persistedAttempt = await verificationContext.Attempts.FindAsync(attemptId);
        var persistedRun = await verificationContext.Runs.FindAsync(runId);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(AttemptStatus.Interrupted, persistedAttempt.Status);
        Assert.NotNull(persistedAttempt.ProcessDispatchedAtUtc);
        Assert.NotNull(persistedRun);
        Assert.Equal(RunLifecycle.Interrupted, persistedRun.Lifecycle);
    }
}
