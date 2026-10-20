using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Projects.Queries.GetWorkspaceExecutionStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// The shared ADR-0057 scenario, run against each of the seven real Agent supervisors through its own composition (the real
/// <c>BackgroundService</c>, scoped mediator, EF-transaction pipeline, sealed artifact store and a real file-backed SQLite database with
/// production migrations, faking only the provider adapter and the Git evidence reader). One claimed Agent attempt is the workspace's
/// owner from its claim: a verification request is refused beside it before any Git capture, and when another owner already exists the
/// supervisor neither captures Git nor invokes its adapter for the claimed attempt, which is left as recorded (never terminalized,
/// never cancelled); when that owner ends, the same attempt runs exactly as it does alone, with the same cardinality and outcome, and
/// the workspace reads Idle again once it completes. Nothing here starts a real provider, process or Git command.
/// </summary>
internal static class WorkspaceExecutionHostedScenarios
{
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(2000);

    private static async Task<T> WithDbAsync<T>(ServiceProvider provider, Func<DevalCopilotDbContext, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

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

    public static async Task AClaimedAttemptWaitsForAnExistingOwnerAndThenRunsOnceAsync(AgentSupervisionHostedComposition composition)
    {
        var baseline = await BaselineAsync(composition);

        AgentSupervisionHostedSupport.ResetDatabase(composition.DatabasePath());
        var reader = new GateableEvidenceReader(composition.EvidenceForCall);
        await using var provider = composition.Build(reader, _ => { });
        var (runId, attemptId) = await composition.Seed(provider, reader);
        var adapter = composition.AdapterOf(provider);
        composition.OpenAdapter?.Invoke(adapter);

        var (projectId, workspaceId, checkpointId) = await WithDbAsync(provider, async db =>
        {
            var run = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
            var workspace = await db.GitWorkspaces.AsNoTracking().Where(candidate => candidate.ProjectId == run.ProjectId)
                .OrderByDescending(candidate => candidate.WorkspaceNumber).FirstAsync();
            var checkpoint = await db.GitCheckpoints.AsNoTracking().Where(candidate => candidate.WorkspaceId == workspace.Id)
                .OrderByDescending(candidate => candidate.CheckpointNumber).FirstAsync();
            return (run.ProjectId, workspace.Id, checkpoint.Id);
        });

        // The claimed, undispatched attempt already owns the workspace: the status says so, and a verification request is refused
        // before any Git capture with nothing recorded.
        var recipeId = await WithDbAsync(provider, async db =>
        {
            var number = 1 + (await db.VerificationCommands.AsNoTracking().Where(candidate => candidate.ProjectId == projectId)
                .Select(candidate => (int?)candidate.CommandNumber).MaxAsync() ?? 0);
            var recipe = VerificationCommand.Configure(
                Guid.NewGuid(), projectId, number, "Unit", @"C:\tools\verify.exe", ["test"], 60, true, DateTimeOffset.UtcNow);
            db.VerificationCommands.Add(recipe);
            await db.SaveChangesAsync();
            return recipe.Id;
        });
        var capturesBefore = reader.CallCount;
        var executionsBefore = await WithDbAsync(provider, db => db.VerificationExecutions.CountAsync());
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
            var status = await mediator.SendAsync(new GetWorkspaceExecutionStatusQuery(projectId), CancellationToken.None);
            Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, status.Value.Status);
            Assert.Equal(workspaceId, status.Value.WorkspaceId);

            var refused = await mediator.SendAsync(
                new ClaimVerificationExecutionCommand(projectId, recipeId, checkpointId), CancellationToken.None);
            Assert.True(refused.IsFailure);
            Assert.Equal(WorkspaceExecutionErrors.AgentAttemptActiveCode, Assert.Single(refused.Errors).Code);
        }

        Assert.Equal(capturesBefore, reader.CallCount);
        // The probe recipe is removed again so the composition's own verification evidence set is exactly what it claimed against.
        await WithDbAsync(provider, db => db.VerificationCommands.Where(candidate => candidate.Id == recipeId).ExecuteDeleteAsync());
        Assert.Equal(executionsBefore, await WithDbAsync(provider, db => db.VerificationExecutions.CountAsync()));

        // Another owner of the workspace already exists (an unresolved Agent attempt of another run, recorded before the supervisor looks).
        var competingAttemptId = await WithDbAsync(provider, async db =>
        {
            var number = 1 + await db.Runs.AsNoTracking().Where(candidate => candidate.ProjectId == projectId)
                .Select(candidate => candidate.ExecutionNumber).MaxAsync();
            var other = Run.RecordIntent(Guid.NewGuid(), projectId, number, "A competing run", DateTimeOffset.UtcNow);
            other.Claim(DateTimeOffset.UtcNow);
            db.Runs.Add(other);
            await db.SaveChangesAsync();
            var attempt = Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), other.Id, 1, workspaceId, checkpointId, new string('a', 64), Guid.NewGuid(), TimeSpan.FromMinutes(10), 1024,
                2048, DateTimeOffset.UtcNow, 1);
            attempt.MarkAgentDispatched(DateTimeOffset.UtcNow);
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            return attempt.Id;
        });
        var capturesWithOwner = reader.CallCount;

        var supervisor = composition.CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(SettleWindow);

            // No capture, no dispatch marker, no adapter call: the attempt is exactly as claimed.
            Assert.Equal(capturesWithOwner, reader.CallCount);
            Assert.Equal(0, AgentSupervisionHostedSupport.InvocationCount(adapter));
            var waiting = await AgentSupervisionHostedSupport.AttemptAsync(provider, attemptId);
            Assert.Equal(AttemptStatus.Running, waiting.Status);
            Assert.Null(waiting.AgentDispatchedAtUtc);
            Assert.Null(waiting.AgentOutcome);

            // The other owner ends through its existing reconciliation-style terminal state; nothing here released or chose anything.
            await WithDbAsync(provider, db => db.Attempts.Where(candidate => candidate.Id == competingAttemptId)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, AttemptStatus.Interrupted)));

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

        await using var after = provider.CreateAsyncScope();
        var idle = await after.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new GetWorkspaceExecutionStatusQuery(projectId), CancellationToken.None);
        Assert.Equal(WorkspaceExecutionStatus.Idle, idle.Value.Status);
    }
}
