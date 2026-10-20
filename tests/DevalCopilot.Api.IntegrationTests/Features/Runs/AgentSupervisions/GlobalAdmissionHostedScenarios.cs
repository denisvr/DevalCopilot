using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// The shared ADR-0058 scenarios, run against a real Agent supervisor through its own composition (the real <c>BackgroundService</c>,
/// scoped mediator, EF-transaction pipeline, sealed artifact store and a real file-backed SQLite database, faking only the provider
/// adapter and the Git evidence reader). Two other recorded potentially mutating owners, an Agent attempt and a verification execution on
/// unrelated projects, make the database full. A claimed WorkspaceMutating attempt then neither captures Git nor invokes its adapter and is
/// left exactly as recorded (never terminalized, never cancelled); when one of those owners genuinely ends it runs exactly as it does
/// alone, with the same cardinality and outcome. A recognized ReadOnly attempt runs unchanged at full capacity. Nothing here starts a real
/// provider, process or Git command.
/// </summary>
internal static class GlobalAdmissionHostedScenarios
{
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(2000);
    private static readonly string Fingerprint = new('a', 64);

    private static async Task<T> WithDbAsync<T>(ServiceProvider provider, Func<DevalCopilotDbContext, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    /// <summary>One unrelated project with its own Ready, leased workspace, and either a Running Agent implementation attempt or a Running
    /// verification execution on it. Returns the immutable identifier of the owner row and which table holds it.</summary>
    private static Task<(Guid Id, bool Agent)> PlantOwnerAsync(ServiceProvider provider, bool agent) => WithDbAsync(provider, async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var project = Project.Register(Guid.NewGuid(), "Capacity neighbour", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Neighbour", now);
        run.Claim(now);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\neighbour-{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.VerificationCommands.Add(recipe);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        await db.SaveChangesAsync();
        if (agent)
        {
            var attempt = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024, 2048,
                now.AddMinutes(5), 1);
            attempt.MarkAgentDispatched(now.AddMinutes(5));
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            return (attempt.Id, true);
        }

        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, now.AddMinutes(5));
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
        return (execution.Id, false);
    });

    private static Task EndOwnerAsync(ServiceProvider provider, (Guid Id, bool Agent) owner) => WithDbAsync(provider, async db =>
    {
        if (owner.Agent)
        {
            await db.Attempts.Where(candidate => candidate.Id == owner.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, AttemptStatus.Interrupted));
        }
        else
        {
            await db.VerificationExecutions.Where(candidate => candidate.Id == owner.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, VerificationExecutionStatus.Interrupted));
        }

        return 0;
    });

    private static async Task<(AttemptStatus Status, AgentOutcome? Outcome, int Invocations)> BaselineAsync(
        AgentSupervisionHostedComposition composition)
    {
        AgentSupervisionHostedSupport.ResetDatabase(composition.DatabasePath());
        var reader = new GateableEvidenceReader(composition.EvidenceForCall);
        await using var provider = composition.Build(reader, _ => { });
        var (_, attemptId) = await composition.Seed(provider, reader);
        var adapter = composition.AdapterOf(provider);
        composition.OpenAdapter?.Invoke(adapter);
        var supervisor = composition.CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var attempt = await AgentSupervisionHostedSupport.WaitForTerminalAsync(provider, attemptId);
            return (attempt.Status, attempt.AgentOutcome, AgentSupervisionHostedSupport.InvocationCount(adapter));
        }
        finally
        {
            reader.Release();
            await AgentSupervisionHostedSupport.StopAsync(supervisor);
        }
    }

    /// <summary>A claimed WorkspaceMutating attempt beside two other recorded owners is withheld before any capture or dispatch, then runs
    /// once exactly as it does alone after one of them genuinely ends.</summary>
    public static async Task AMutatingAttemptWaitsForTwoOtherOwnersAndThenRunsOnceAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        AgentSupervisionHostedSupport.ResetDatabase(composition.DatabasePath());
        var reader = new GateableEvidenceReader(composition.EvidenceForCall);
        await using var provider = composition.Build(reader, _ => { });
        var (_, attemptId) = await composition.Seed(provider, reader);
        var adapter = composition.AdapterOf(provider);
        composition.OpenAdapter?.Invoke(adapter);
        var first = await PlantOwnerAsync(provider, agent: true);
        await PlantOwnerAsync(provider, agent: false);
        var captures = reader.CallCount;

        var supervisor = composition.CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(SettleWindow);

            // No capture, no dispatch marker, no adapter call: the attempt is exactly as claimed, and nothing was chosen or released.
            Assert.Equal(captures, reader.CallCount);
            Assert.Equal(0, AgentSupervisionHostedSupport.InvocationCount(adapter));
            var waiting = await AgentSupervisionHostedSupport.AttemptAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Running, waiting.Status);
            Assert.Null(waiting.AgentDispatchedAtUtc);
            Assert.Null(waiting.AgentOutcome);

            await EndOwnerAsync(provider, first);

            var finished = await AgentSupervisionHostedSupport.WaitForTerminalAsync(provider, attemptId);
            Assert.Equal(baseline.Status, finished.Status);
            Assert.Equal(baseline.Outcome, finished.AgentOutcome);
            Assert.Equal(baseline.Invocations, AgentSupervisionHostedSupport.InvocationCount(adapter));
        }
        finally
        {
            reader.Release();
            await AgentSupervisionHostedSupport.StopAsync(supervisor);
        }
    }

    /// <summary>A recognized ReadOnly attempt consumes no capacity and is never withheld by it: beside two recorded mutating owners it runs
    /// with the same cardinality and outcome as it does alone.</summary>
    public static async Task AReadOnlyAttemptRunsUnchangedAtFullCapacityAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        AgentSupervisionHostedSupport.ResetDatabase(composition.DatabasePath());
        var reader = new GateableEvidenceReader(composition.EvidenceForCall);
        await using var provider = composition.Build(reader, _ => { });
        var (_, attemptId) = await composition.Seed(provider, reader);
        var adapter = composition.AdapterOf(provider);
        composition.OpenAdapter?.Invoke(adapter);
        await PlantOwnerAsync(provider, agent: true);
        await PlantOwnerAsync(provider, agent: false);

        var supervisor = composition.CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var finished = await AgentSupervisionHostedSupport.WaitForTerminalAsync(provider, attemptId);
            Assert.Equal(baseline.Status, finished.Status);
            Assert.Equal(baseline.Outcome, finished.AgentOutcome);
            Assert.Equal(baseline.Invocations, AgentSupervisionHostedSupport.InvocationCount(adapter));
        }
        finally
        {
            reader.Release();
            await AgentSupervisionHostedSupport.StopAsync(supervisor);
        }
    }
}
