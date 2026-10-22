using DevalCopilot.Api.HostedServices;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060 host startup: right after migration, and before artifact recovery, attempt interruption, workspace reconciliation
/// or any supervisor, every formerly advancing sequence becomes Interrupted and its unclaimed preparation is revoked. A linked dispatched
/// operation without a recorded result stays exactly as recorded, nothing is created, adopted or resumed, and the side-effect-free
/// compositions start no coordinator and interrupt nothing.</summary>
public sealed class LocalExecutionSequenceStartupTests : IDisposable
{
    private const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private static bool coordinatorWasRegistered;

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-sequence-startup-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private DevalCopilotDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private WebApplicationFactory<Program> HostOver(string? environment = null, bool removeHosted = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                coordinatorWasRegistered = services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(LocalExecutionSequenceCoordinator));
                if (removeHosted)
                {
                    // Never start real provider supervisors or probes in a test host: only the startup recovery is under test.
                    foreach (var descriptor in services.Where(candidate => candidate.ServiceType == typeof(IHostedService)
                                 && candidate.ImplementationType?.Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true).ToArray())
                    {
                        services.Remove(descriptor);
                    }
                }
            });

            if (environment is not null)
            {
                builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, environment);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={databasePath}"),
            ]));
        });

    private sealed record Seeded(Guid Armed, Guid Preparing, Guid PreparingOperation, Guid Awaiting, Guid AwaitingAttempt, Guid AwaitingStep);

    private async Task<Guid> ArmDirectAsync(DevalCopilotDbContext db, LocalExecutionSequenceApiScene scene)
    {
        var commands = await db.VerificationCommands.AsNoTracking()
            .Where(command => command.ProjectId == scene.ProjectId).OrderBy(command => command.CommandNumber).ToListAsync();
        var sequence = LocalExecutionSequence.Arm(
            Guid.NewGuid(), scene.RunId, scene.ProjectId, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint,
            scene.PlanId, LocalExecutionPlanAuthority.AcceptedRoot, new string('9', 64), new string('7', 64),
            "devalcopilot.local-execution-sequence.settings.v1\n", commands.Count, 2, DateTimeOffset.UtcNow.AddMinutes(-1),
            LocalExecutionSequenceNumbers.Next());
        db.LocalExecutionSequences.Add(sequence);
        for (var index = 0; index < commands.Count; index++)
        {
            db.LocalExecutionSequenceRecipes.Add(LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), sequence.Id, index, commands[index]));
        }

        await db.SaveChangesAsync();
        return sequence.Id;
    }

    private static LocalExecutionSequenceStep ReserveImplementation(LocalExecutionSequenceApiScene scene, Guid sequenceId, Guid operationId) =>
        LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.CheckpointId,
            LocalExecutionSequenceApiSeed.Fingerprint, null, null, operationId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-20));

    private async Task<Seeded> SeedLiveSequencesAsync()
    {
        await using (var migrate = OpenContext())
        {
            await migrate.Database.MigrateAsync();
        }

        await using var db = OpenContext();
        var armedScene = await LocalExecutionSequenceApiSeed.SeedAsync(db);
        var armed = await ArmDirectAsync(db, armedScene);

        var preparingScene = await LocalExecutionSequenceApiSeed.SeedAsync(db);
        var preparing = await ArmDirectAsync(db, preparingScene);
        var preparingOperation = Guid.NewGuid();
        var preparingSequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == preparing);
        preparingSequence.BeginPreparing(DateTimeOffset.UtcNow.AddSeconds(-20));
        db.LocalExecutionSequenceSteps.Add(ReserveImplementation(preparingScene, preparing, preparingOperation));
        await db.SaveChangesAsync();

        var awaitingScene = await LocalExecutionSequenceApiSeed.SeedAsync(db);
        var awaiting = await ArmDirectAsync(db, awaitingScene);
        var awaitingOperation = Guid.NewGuid();
        var awaitingStep = ReserveImplementation(awaitingScene, awaiting, awaitingOperation);
        var awaitingSequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == awaiting);
        awaitingSequence.BeginPreparing(DateTimeOffset.UtcNow.AddSeconds(-20));
        db.LocalExecutionSequenceSteps.Add(awaitingStep);
        await db.SaveChangesAsync();
        var attempt = Attempt.ClaimAgentImplementation(
            awaitingOperation, awaitingScene.RunId, 3, awaitingScene.WorkspaceId, awaitingScene.CheckpointId,
            LocalExecutionSequenceApiSeed.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, DateTimeOffset.UtcNow.AddSeconds(-15), 3);
        attempt.MarkAgentDispatched(DateTimeOffset.UtcNow.AddSeconds(-10));
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        awaitingStep.MarkClaimed(awaitingOperation, DateTimeOffset.UtcNow.AddSeconds(-15), DateTimeOffset.UtcNow.AddMinutes(30));
        awaitingSequence.MarkAwaitingResult(DateTimeOffset.UtcNow.AddSeconds(-15));
        await db.SaveChangesAsync();
        return new Seeded(armed, preparing, preparingOperation, awaiting, awaitingOperation, awaitingStep.Id);
    }

    [Fact]
    public async Task Host_startup_interrupts_every_formerly_advancing_sequence_without_creating_resuming_or_adopting_anything()
    {
        var seeded = await SeedLiveSequencesAsync();
        int attemptsBefore;
        await using (var before = OpenContext())
        {
            attemptsBefore = await before.Attempts.AsNoTracking().CountAsync();
        }

        using (var host = HostOver())
        {
            // Creating the client builds the host, which runs the ordered startup recovery.
            using var client = host.CreateClient();
            Assert.True(coordinatorWasRegistered, "the coordinator is part of the normal composition");
        }

        await using var context = OpenContext();
        foreach (var id in new[] { seeded.Armed, seeded.Preparing, seeded.Awaiting })
        {
            var sequence = await context.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == id);
            Assert.Equal(LocalExecutionSequenceState.Interrupted, sequence.State);
            Assert.Equal(LocalExecutionSequenceReason.HostRestarted, sequence.ReasonCode);
        }

        Assert.Empty(await context.LocalExecutionSequenceSteps.AsNoTracking().Where(step => step.SequenceId == seeded.Armed).ToListAsync());
        var preparingStep = await context.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync(step => step.SequenceId == seeded.Preparing);
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, preparingStep.State);
        Assert.Null(preparingStep.OperationId);
        Assert.False(await context.Attempts.AsNoTracking().AnyAsync(attempt => attempt.Id == seeded.PreparingOperation));

        // A linked dispatched operation with no recorded result is neither interrupted, completed nor released: it stays as recorded.
        var awaitingStep = await context.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync(step => step.Id == seeded.AwaitingStep);
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, awaitingStep.State);
        var linked = await context.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == seeded.AwaitingAttempt);
        Assert.Equal(AttemptStatus.Running, linked.Status);
        Assert.NotNull(linked.AgentDispatchedAtUtc);
        Assert.Null(linked.AgentOutcome);
        Assert.Equal(attemptsBefore, await context.Attempts.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Host_startup_closes_a_waiting_queued_cycle_and_retires_its_pending_admission_without_advancing_anything()
    {
        await using (var migrate = OpenContext())
        {
            await migrate.Database.MigrateAsync();
        }

        Guid sequenceId;
        Guid admissionId;
        int attemptsBefore;
        await using (var db = OpenContext())
        {
            var scene = await LocalExecutionSequenceApiSeed.SeedAsync(db);
            var commands = await db.VerificationCommands.AsNoTracking()
                .Where(command => command.ProjectId == scene.ProjectId).OrderBy(command => command.CommandNumber).ToListAsync();
            var sequence = LocalExecutionSequence.Arm(
                Guid.NewGuid(), scene.RunId, scene.ProjectId, scene.WorkspaceId, scene.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint,
                scene.PlanId, LocalExecutionPlanAuthority.AcceptedRoot, new string('9', 64), new string('7', 64),
                "devalcopilot.local-execution-sequence.settings.v1\n", commands.Count, 2, DateTimeOffset.UtcNow.AddMinutes(-1),
                LocalExecutionSequenceNumbers.Next(), LocalExecutionAdmissionMode.WaitForAdmission);
            db.LocalExecutionSequences.Add(sequence);
            for (var index = 0; index < commands.Count; index++)
            {
                db.LocalExecutionSequenceRecipes.Add(LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), sequence.Id, index, commands[index]));
            }

            var admission = LocalExecutionAdmission.Enqueue(
                Guid.NewGuid(), sequence.Id, LocalExecutionSequenceNumbers.Next(), 0, LocalExecutionStepKind.Implementation, 0, null, null,
                scene.CheckpointId, LocalExecutionSequenceApiSeed.Fingerprint, null, null, DateTimeOffset.UtcNow.AddMinutes(-1));
            db.LocalExecutionAdmissions.Add(admission);
            await db.SaveChangesAsync();
            sequenceId = sequence.Id;
            admissionId = admission.Id;
            attemptsBefore = await db.Attempts.AsNoTracking().CountAsync();
        }

        using (var host = HostOver())
        {
            using var client = host.CreateClient();
        }

        await using var context = OpenContext();
        var closed = await context.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId);
        Assert.Equal(LocalExecutionSequenceState.Interrupted, closed.State);
        Assert.Equal(LocalExecutionSequenceReason.HostRestarted, closed.ReasonCode);
        var retired = await context.LocalExecutionAdmissions.AsNoTracking().SingleAsync(candidate => candidate.Id == admissionId);
        Assert.Equal(LocalExecutionAdmissionState.Retired, retired.State);
        Assert.Null(retired.StepId);
        Assert.Empty(await context.LocalExecutionSequenceSteps.AsNoTracking().Where(step => step.SequenceId == sequenceId).ToListAsync());
        Assert.Equal(attemptsBefore, await context.Attempts.AsNoTracking().CountAsync());
    }

    [Theory]
    [InlineData("OpenApiGeneration")]
    [InlineData("SideEffectFreeIntegrationTest")]
    public async Task Side_effect_free_compositions_start_no_coordinator_and_interrupt_nothing(string environment)
    {
        var seeded = await SeedLiveSequencesAsync();

        using (var host = HostOver(environment, removeHosted: false))
        {
            using var client = host.CreateClient();
            Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service =>
                service.GetType().Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true);
        }

        await using var context = OpenContext();
        Assert.Equal(
            LocalExecutionSequenceState.Armed,
            (await context.LocalExecutionSequences.AsNoTracking().SingleAsync(sequence => sequence.Id == seeded.Armed)).State);
        Assert.Equal(
            LocalExecutionSequenceState.PreparingStep,
            (await context.LocalExecutionSequences.AsNoTracking().SingleAsync(sequence => sequence.Id == seeded.Preparing)).State);
        Assert.Equal(
            LocalExecutionSequenceStepState.Reserved,
            (await context.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync(step => step.SequenceId == seeded.Preparing)).State);
    }

    [Fact]
    public void The_local_sequence_closure_follows_migration_and_precedes_every_recovery_reconciliation_and_supervisor()
    {
        var program = File.ReadAllText(FindProgram());

        var migrate = program.IndexOf("dbContext.Database.MigrateAsync()", StringComparison.Ordinal);
        var localInterrupt = program.IndexOf("new InterruptLocalExecutionSequencesCommand()", StringComparison.Ordinal);
        var attemptRecovery = program.IndexOf("ProcessAttemptOutputRecovery.RunAsync", StringComparison.Ordinal);
        var processReconcile = program.IndexOf("new ReconcileInterruptedProcessAttemptsCommand()", StringComparison.Ordinal);
        var localCommitRecovery = program.IndexOf("new RecoverLocalCommitOperationsCommand()", StringComparison.Ordinal);
        var workspaceReconcile = program.IndexOf("new ReconcileWorkspacesCommand()", StringComparison.Ordinal);
        var verificationOutputRecovery = program.IndexOf("new RecoverInterruptedVerificationOutputArtifactsCommand()", StringComparison.Ordinal);
        var verificationReconcile = program.IndexOf("new ReconcileInterruptedVerificationExecutionsCommand()", StringComparison.Ordinal);
        var agentOutputRecovery = program.IndexOf("AgentAttemptOutputRecovery.RunAsync", StringComparison.Ordinal);
        var agentReconcile = program.IndexOf("new ReconcileInterruptedAgentAttemptsCommand()", StringComparison.Ordinal);
        var implementationReconcile = program.IndexOf("new ReconcileInterruptedImplementationAttemptsCommand()", StringComparison.Ordinal);
        var planningInterrupt = program.IndexOf("new InterruptPlanningSequencesCommand()", StringComparison.Ordinal);
        var supervisionClose = program.IndexOf("new CloseInterruptedAgentSupervisionsCommand()", StringComparison.Ordinal);
        var hostStart = program.IndexOf("await app.RunAsync()", StringComparison.Ordinal);

        Assert.True(migrate > 0 && migrate < localInterrupt, "the closure follows the migration");
        foreach (var (name, position) in new[]
                 {
                     ("artifact recovery", attemptRecovery), ("process reconciliation", processReconcile),
                     ("local commit recovery", localCommitRecovery), ("workspace reconciliation", workspaceReconcile),
                     ("verification output recovery", verificationOutputRecovery), ("verification reconciliation", verificationReconcile),
                     ("Agent output recovery", agentOutputRecovery), ("Agent reconciliation", agentReconcile),
                     ("implementation reconciliation", implementationReconcile), ("planning interruption", planningInterrupt),
                     ("supervision close", supervisionClose), ("host start", hostStart),
                 })
        {
            Assert.True(position > 0, $"{name} is present in the startup composition");
            Assert.True(localInterrupt < position, $"local sequences are closed before {name}");
        }

        Assert.Contains("builder.Services.AddHostedService<LocalExecutionSequenceCoordinator>()", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddHostedService<PlanningSequenceCoordinator>()", program, StringComparison.Ordinal);
    }

    private static string FindProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "backend", "DevalCopilot.Api", "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Program.cs was not found above the test binaries.");
    }
}
