using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055 host startup: after the artifact and Agent/implementation reconciliation, every formerly advancing sequence
/// (Armed, preparation-only, awaiting a result) becomes Interrupted before any hosted worker can start, no attempt is created,
/// dispatched or adopted, and the side-effect-free compositions start no coordinator and interrupt nothing.</summary>
public sealed class PlanningSequenceStartupTests
{
    private const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private static bool coordinatorWasRegistered;

    private static WebApplicationFactory<Program> HostOver(string databasePath, string? environment = null, bool removeHosted = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                coordinatorWasRegistered = services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(PlanningSequenceCoordinator));
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

    private static async Task<(Guid Armed, Guid Preparing, Guid Awaiting, Guid AwaitingAttempt)> SeedLiveSequencesAsync(
        PlanningSequenceHostedHarness harness)
    {
        var armed = await harness.ArmAsync(await harness.SeedSceneAsync());

        var preparingScene = await harness.SeedSceneAsync();
        var preparing = await harness.ArmAsync(preparingScene);
        var reserved = await harness.SendAsync(new ObservePlanningSequenceCommand(preparing.SequenceId));
        Assert.NotNull(reserved.Value.ReservedStep);

        var awaitingScene = await harness.SeedSceneAsync();
        var awaiting = await harness.ArmAsync(awaitingScene);
        var ticket = (await harness.SendAsync(new ObservePlanningSequenceCommand(awaiting.SequenceId))).Value.ReservedStep!;
        var claim = await harness.SendAsync(new ClaimPlanningSequenceStepCommand(ticket));
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (armed.SequenceId, preparing.SequenceId, awaiting.SequenceId, claim.Value.AttemptId);
    }

    [Fact]
    public async Task Host_startup_interrupts_every_formerly_advancing_sequence_without_creating_or_adopting_anything()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var (armed, preparing, awaiting, awaitingAttempt) = await SeedLiveSequencesAsync(harness);

        using (var host = HostOver(harness.DatabasePath))
        {
            // Creating the client builds the host, which runs the ordered startup recovery.
            using var client = host.CreateClient();
            Assert.True(coordinatorWasRegistered, "the coordinator is part of the normal composition");
        }

        foreach (var id in new[] { armed, preparing, awaiting })
        {
            var sequence = await harness.WaitForSequenceAsync(id, candidate => candidate.State == PlanningSequenceState.Interrupted);
            Assert.Equal(PlanningSequenceReason.HostRestarted, sequence.ReasonCode);
        }

        await using var context = harness.OpenContext();
        var preparingStep = await context.PlanningSequenceSteps.AsNoTracking().SingleAsync(step => step.SequenceId == preparing);
        Assert.Equal(PlanningSequenceStepState.Revoked, preparingStep.State);
        Assert.Empty(await context.Attempts.AsNoTracking().Where(attempt => attempt.Id == preparingStep.ReservedAttemptId).ToListAsync());
        var attempt = await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == awaitingAttempt);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Single(await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == awaiting).ToListAsync());
        Assert.Empty(await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed).ToListAsync());
        Assert.Equal(0, harness.Planner.InvocationCount + harness.Reviewer.InvocationCount + harness.Resolver.InvocationCount);
    }

    [Theory]
    [InlineData("OpenApiGeneration")]
    [InlineData("SideEffectFreeIntegrationTest")]
    public async Task Side_effect_free_compositions_start_no_coordinator_and_interrupt_nothing(string environment)
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var (armed, _, _, _) = await SeedLiveSequencesAsync(harness);

        using (var host = HostOver(harness.DatabasePath, environment, removeHosted: false))
        {
            using var client = host.CreateClient();
            Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service =>
                service.GetType().Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true);
        }

        await using var context = harness.OpenContext();
        Assert.Equal(PlanningSequenceState.Armed, (await context.PlanningSequences.AsNoTracking().SingleAsync(sequence => sequence.Id == armed)).State);
    }

    [Fact]
    public void The_startup_interruption_follows_the_artifact_and_attempt_reconciliation_and_precedes_the_coordinator_starting()
    {
        var program = File.ReadAllText(FindProgram());

        var attemptRecovery = program.IndexOf("AgentAttemptOutputRecovery.RunAsync", StringComparison.Ordinal);
        var agentReconcile = program.IndexOf("new ReconcileInterruptedAgentAttemptsCommand()", StringComparison.Ordinal);
        var implementationReconcile = program.IndexOf("new ReconcileInterruptedImplementationAttemptsCommand()", StringComparison.Ordinal);
        var interrupt = program.IndexOf("new InterruptPlanningSequencesCommand()", StringComparison.Ordinal);
        var hostStart = program.IndexOf("await app.RunAsync()", StringComparison.Ordinal);

        Assert.True(attemptRecovery > 0 && attemptRecovery < agentReconcile, "artifact recovery precedes attempt reconciliation");
        Assert.True(agentReconcile < implementationReconcile, "the current reconciliation order is preserved");
        Assert.True(implementationReconcile < interrupt, "sequences are interrupted after every attempt reconciliation");
        Assert.True(interrupt < hostStart, "sequences are interrupted before any hosted worker starts");
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
