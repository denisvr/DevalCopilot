using DevalCopilot.Domain.Features.EnvironmentReadiness;
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

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-test", DateTimeOffset.UtcNow);
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
        Assert.Contains("AddHostCapabilitySnapshots", appliedMigrations);
        Assert.Contains("AddCollaborationMessages", appliedMigrations);
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_bounded_collaboration_message()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\ledger-migration-test", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the ledger schema", now);
        context.AddRange(project, run);
        context.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex,
            ParticipantKind.Claude,
            CollaborationMessageType.Proposal,
            null,
            "Record a bounded proposal.",
            "{\"scope\":\"Schema\",\"assumptions\":\"SQLite\",\"verification\":\"Migration test\",\"risks\":\"Schema drift\"}",
            CollaborationMessageProvenance.Simulated,
            now));

        await context.SaveChangesAsync();

        Assert.True(await context.CollaborationMessages.AnyAsync());
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_one_host_capability_snapshot_per_capability()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, now);
        snapshot.MarkDispatched(now);
        snapshot.RecordSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0", now, now.AddMinutes(5));
        context.HostCapabilitySnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persisted = await reopenedContext.HostCapabilitySnapshots.FindAsync(Capability.Git);

        Assert.NotNull(persisted);
        Assert.Equal(CapabilityProbeReason.None, persisted.ReasonCode);
        Assert.Equal("2.43.0", persisted.ObservedVersion);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", persisted.ResolvedExecutablePath);
        Assert.Null(persisted.ProbeDispatchedAtUtc);

        // Capability is the natural primary key: inserting a second row for the same
        // capability must be rejected, not silently duplicated.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            reopenedContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, now));
            await reopenedContext.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_process_attempt_with_its_durable_intent_and_defaults_prior_rows_to_simulated()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\process-migration-test", DateTimeOffset.UtcNow);
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

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\argument-immutability-test", DateTimeOffset.UtcNow);
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

    [Fact]
    public async Task Migrate_persists_project_verification_commands_with_monotonic_numbers_and_literal_arguments()
    {
        var now = DateTimeOffset.UtcNow;
        var arguments = new List<string> { "test", "--no-restore" };

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\verification-migration-test", now);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var verificationCommand = VerificationCommand.Configure(
            Guid.NewGuid(),
            project.Id,
            project.ReserveVerificationCommandNumber(),
            "Backend tests",
            @"C:\Program Files\dotnet\dotnet.exe",
            arguments,
            300,
            true,
            now);
        context.VerificationCommands.Add(verificationCommand);
        arguments.Add("--tampered");
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persisted = await reopenedContext.VerificationCommands.SingleAsync(command => command.ProjectId == project.Id);

        Assert.Equal(1, persisted.CommandNumber);
        Assert.Equal(["test", "--no-restore"], persisted.Arguments);
        Assert.Equal(2, (await reopenedContext.Projects.SingleAsync(savedProject => savedProject.Id == project.Id)).NextVerificationCommandNumber);
    }
}
