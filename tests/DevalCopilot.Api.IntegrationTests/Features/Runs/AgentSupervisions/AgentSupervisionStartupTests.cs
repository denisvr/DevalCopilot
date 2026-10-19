using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>ADR-0056 host startup: after the existing attempt reconciliation and before any hosted worker starts, every observation a
/// previous host launch left open is closed as interrupted with its facts preserved, nothing is adopted, renewed or resumed, no attempt
/// decision is changed by it, the order is pinned in the composition root, and the side-effect-free compositions start no sweep and
/// close nothing.</summary>
public sealed class AgentSupervisionStartupTests
{
    private const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private static WebApplicationFactory<Program> HostOver(string databasePath, string? environment = null, bool removeHosted = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (removeHosted)
            {
                builder.ConfigureTestServices(services =>
                {
                    // Never start real provider supervisors or probes in a test host: only the startup recovery is under test.
                    foreach (var descriptor in services.Where(candidate => candidate.ServiceType == typeof(IHostedService)
                                 && candidate.ImplementationType?.Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true).ToArray())
                    {
                        services.Remove(descriptor);
                    }
                });
            }

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

    /// <summary>A Running Agent attempt of a Running run with an open observation written by a launch that no longer exists.</summary>
    private static async Task<(Guid AttemptId, Guid PreviousHost, Guid ObserverId)> SeedPreviousLaunchAsync(PlanningSequenceHostedHarness harness)
    {
        var scene = await harness.SeedSceneAsync();
        await using var context = harness.OpenContext();
        var run = await context.Runs.SingleAsync(candidate => candidate.Id == scene.RunId);
        run.Claim(DateTimeOffset.UtcNow.AddMinutes(-5));
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), scene.RunId, 1, scene.WorkspaceId, scene.CheckpointId, scene.Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
            1024, 2048, DateTimeOffset.UtcNow.AddMinutes(-5), 1);
        context.Attempts.Add(attempt);
        await context.SaveChangesAsync();
        var previousHost = Guid.NewGuid();
        var observer = Guid.NewGuid();
        context.AgentSupervisions.Add(AgentSupervision.Register(
            Guid.NewGuid(), scene.RunId, attempt.Id, previousHost, observer, DateTimeOffset.UtcNow.AddMinutes(-4)));
        await context.SaveChangesAsync();
        return (attempt.Id, previousHost, observer);
    }

    [Fact]
    public async Task Host_startup_closes_the_previous_launchs_observation_as_interrupted_without_adopting_or_resuming_anything()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var (attemptId, previousHost, observer) = await SeedPreviousLaunchAsync(harness);

        Guid currentHost;
        using (var host = HostOver(harness.DatabasePath))
        {
            // Creating the client builds the host, which runs the ordered startup recovery.
            using var client = host.CreateClient();
            currentHost = host.Services.GetRequiredService<Application.Features.Runs.Policies.AgentSupervisions.AgentSupervisionHostInstance>().Id;
        }

        await using var context = harness.OpenContext();
        var receipt = await context.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.AttemptId == attemptId);
        Assert.NotEqual(previousHost, currentHost);
        Assert.Equal((previousHost, observer), (receipt.HostInstanceId, receipt.ObserverId));
        Assert.Equal(AgentSupervisionClosureReason.HostInterrupted, receipt.ClosureReason);
        Assert.Equal(AgentSupervisionAttentionReason.HostInterrupted, receipt.AttentionReason);
        Assert.NotNull(receipt.ClosedAtUtc);

        // The existing reconciliation decided the attempt; the closure changed nothing about it and invoked nothing.
        var attempt = await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(0, harness.Planner.InvocationCount + harness.Reviewer.InvocationCount + harness.Resolver.InvocationCount);
        Assert.Equal(
            [RunEventType.AgentSupervisionAttention, RunEventType.AgentSupervisionClosed],
            (await context.Events.AsNoTracking().Where(candidate => candidate.RunId == attempt.RunId).ToListAsync())
                .Where(candidate => candidate.EventType.StartsWith("agent_supervision.", StringComparison.Ordinal))
                .OrderBy(candidate => candidate.Sequence)
                .Select(candidate => candidate.EventType));
    }

    [Theory]
    [InlineData("OpenApiGeneration")]
    [InlineData("SideEffectFreeIntegrationTest")]
    public async Task Side_effect_free_compositions_start_no_sweep_and_close_nothing(string environment)
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var (attemptId, _, _) = await SeedPreviousLaunchAsync(harness);

        using (var host = HostOver(harness.DatabasePath, environment, removeHosted: false))
        {
            using var client = host.CreateClient();
            Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service => service is AgentSupervisionSweep);
            Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service =>
                service.GetType().Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true);
        }

        await using var context = harness.OpenContext();
        var receipt = await context.AgentSupervisions.AsNoTracking().SingleAsync(candidate => candidate.AttemptId == attemptId);
        Assert.Null(receipt.ClosedAtUtc);
        Assert.Null(receipt.AttentionReason);
        Assert.Equal(AttemptStatus.Running, (await context.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).Status);
    }

    [Fact]
    public async Task The_normal_composition_registers_the_sweep_and_the_observer_the_supervisors_resolve()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        await harness.MigrateAsync();

        using var host = HostOver(harness.DatabasePath, removeHosted: false);
        using var client = host.CreateClient();

        Assert.Single(host.Services.GetServices<IHostedService>(), service => service is AgentSupervisionSweep);
        Assert.NotNull(host.Services.GetService<AgentSupervisionObserver>());
        Assert.Same(
            host.Services.GetRequiredService<Application.Features.Runs.Policies.AgentSupervisions.AgentSupervisionHostInstance>(),
            host.Services.GetRequiredService<Application.Features.Runs.Policies.AgentSupervisions.AgentSupervisionHostInstance>());
    }

    [Fact]
    public void The_startup_closure_follows_every_attempt_reconciliation_and_precedes_any_hosted_worker_starting()
    {
        var program = File.ReadAllText(FindProgram());

        var agentReconcile = program.IndexOf("new ReconcileInterruptedAgentAttemptsCommand()", StringComparison.Ordinal);
        var implementationReconcile = program.IndexOf("new ReconcileInterruptedImplementationAttemptsCommand()", StringComparison.Ordinal);
        var planningInterrupt = program.IndexOf("new InterruptPlanningSequencesCommand()", StringComparison.Ordinal);
        var closure = program.IndexOf("new CloseInterruptedAgentSupervisionsCommand()", StringComparison.Ordinal);
        var hostStart = program.IndexOf("await app.RunAsync()", StringComparison.Ordinal);

        Assert.True(agentReconcile > 0 && agentReconcile < implementationReconcile, "the current reconciliation order is preserved");
        Assert.True(implementationReconcile < closure, "observations close after every attempt reconciliation");
        Assert.True(planningInterrupt < closure, "the planning-sequence recovery keeps its own position");
        Assert.True(closure < hostStart, "observations close before any hosted worker starts");
        Assert.Contains("builder.Services.AddHostedService<AgentSupervisionSweep>()", program, StringComparison.Ordinal);
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
