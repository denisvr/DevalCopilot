using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

public sealed class MigrationTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_project_run_attempt_and_event()
    {
        var now = DateTimeOffset.UtcNow;

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-test");
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the schema", now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();

            run.Claim(now);
            var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, attemptNumber: 1, now);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();

            var runEvent = RunEvent.Record(Guid.NewGuid(), run.Id, attempt.Id, RunEventType.RunStarted, ParticipantKind.Orchestrator, "{}", now);
            context.Events.Add(runEvent);
            await context.SaveChangesAsync();

            Assert.True(runEvent.Sequence > 0);
        }
    }

    [Fact]
    public async Task Migrate_applied_twice_is_idempotent()
    {
        await using var first = fixture.CreateContext();
        await first.Database.MigrateAsync();

        await using var second = fixture.CreateContext();
        await second.Database.MigrateAsync();

        var appliedMigrations = (await second.Database.GetAppliedMigrationsAsync())
            .Select(id => id.Split('_').Last())
            .ToArray();
        Assert.Contains("InitialCreate", appliedMigrations);
        Assert.Contains("AddProcessAttempts", appliedMigrations);
        Assert.Contains("AddProcessDispatchMarker", appliedMigrations);
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_process_attempt_with_its_durable_intent_and_defaults_prior_rows_to_simulated()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\process-migration-test");
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var simulatedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Simulated", now);
        context.Runs.Add(simulatedRun);
        await context.SaveChangesAsync();
        simulatedRun.Claim(now);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), simulatedRun.Id, attemptNumber: 1, now);
        context.Attempts.Add(simulatedAttempt);
        await context.SaveChangesAsync();

        var processRun = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Process", now);
        context.Runs.Add(processRun);
        await context.SaveChangesAsync();
        processRun.Claim(now);
        var intent = new ProcessExecutionIntent(
            ExecutablePath: @"C:\tools\build.exe",
            Arguments: ["--verify"],
            WorkingDirectory: @"C:\repos\devalcopilot",
            ApprovedRoot: @"C:\repos",
            Timeout: TimeSpan.FromMinutes(5),
            MaxBytesPerStream: 65536,
            MaxTotalCapturedBytes: 131072);
        var processAttempt = Attempt.ClaimProcess(Guid.NewGuid(), processRun.Id, attemptNumber: 1, intent, now);
        context.Attempts.Add(processAttempt);
        await context.SaveChangesAsync();
        processAttempt.MarkProcessDispatched(now);
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persistedSimulated = await reopenedContext.Attempts.FindAsync(simulatedAttempt.Id);
        var persistedProcess = await reopenedContext.Attempts.FindAsync(processAttempt.Id);

        Assert.NotNull(persistedSimulated);
        Assert.Equal(AttemptKind.Simulated, persistedSimulated.Kind);
        Assert.Null(persistedSimulated.ProcessDispatchedAtUtc);

        Assert.NotNull(persistedProcess);
        Assert.Equal(AttemptKind.Process, persistedProcess.Kind);
        Assert.Equal(intent.ExecutablePath, persistedProcess.ProcessExecutablePath);
        Assert.Equal(intent.Arguments, persistedProcess.ProcessArguments);
        Assert.Equal(intent.Timeout, persistedProcess.ProcessTimeout);
        Assert.Equal(now, persistedProcess.ProcessDispatchedAtUtc);
    }

    [Fact]
    public async Task Mutating_the_caller_owned_argument_list_after_ClaimProcess_does_not_change_what_is_persisted()
    {
        var now = DateTimeOffset.UtcNow;
        var mutableArguments = new List<string> { "--verify" };

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\argument-immutability-test");
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Process", now);
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        run.Claim(now);

        var intent = new ProcessExecutionIntent(
            ExecutablePath: @"C:\tools\build.exe",
            Arguments: mutableArguments,
            WorkingDirectory: @"C:\repos\devalcopilot",
            ApprovedRoot: @"C:\repos",
            Timeout: TimeSpan.FromMinutes(5),
            MaxBytesPerStream: 65536,
            MaxTotalCapturedBytes: 131072);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, attemptNumber: 1, intent, now);
        context.Attempts.Add(attempt);

        // Mutated after ClaimProcess but before SaveChanges — a lazily-evaluated conversion
        // reading the caller's own list at save time would otherwise leak this tampering into
        // what gets persisted.
        mutableArguments.Add("--tampered");
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persistedAttempt = await reopenedContext.Attempts.FindAsync(attempt.Id);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(["--verify"], persistedAttempt.ProcessArguments);
    }
}
