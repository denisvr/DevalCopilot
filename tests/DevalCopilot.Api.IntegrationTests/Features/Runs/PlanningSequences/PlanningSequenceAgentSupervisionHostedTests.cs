using Devalente.Shared.Cqrs;
using DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0056 observation of the coordinated ADR-0055 path: every attempt the real coordinator claims is observed by the real supervisors
/// with no additional stage request, the sequence reaches exactly the outcome and invocation cardinality it reaches unobserved, and the
/// observation (healthy, faulty or absent) is never an input of the sequence.
/// </summary>
public sealed class PlanningSequenceAgentSupervisionHostedTests
{
    private static PlanningSequenceHostedHarness ObservedHarness(AgentSupervisionHostInstance host) => new(
        (services, _) => AgentSupervisionHostedSupport.Register(services, clock: null, host));

    [Fact]
    public async Task Every_coordinated_attempt_is_observed_and_closed_cleanly_without_changing_the_sequence()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        await using var harness = ObservedHarness(host);
        harness.Reviewer.Decisions.Enqueue(2);
        harness.Reviewer.Decisions.Enqueue(0);
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, ended.State);
        Assert.Equal(PlanningSequenceReason.PlanAccepted, ended.ReasonCode);
        Assert.Equal((1, 2, 1), (harness.Planner.InvocationCount, harness.Reviewer.InvocationCount, harness.Resolver.InvocationCount));

        await using var context = harness.OpenContext();
        var attempts = await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId)
            .OrderBy(attempt => attempt.AttemptNumber).ToListAsync();
        Assert.Equal(4, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(AttemptStatus.Completed, attempt.Status));

        // Closure follows the terminal result by one short write: wait for the last observation to close.
        var receipts = await AgentSupervisionHostedSupport.WaitAsync(async () =>
        {
            await using var poll = harness.OpenContext();
            var rows = await poll.AgentSupervisions.AsNoTracking().Where(receipt => receipt.RunId == scene.RunId).ToListAsync();
            return rows.Count == 4 && rows.All(row => row.ClosedAtUtc is not null) ? rows : null;
        });
        Assert.Equal(attempts.Select(attempt => attempt.Id).Order(), receipts.Select(receipt => receipt.AttemptId).Order());
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(host.Id, receipt.HostInstanceId);
            Assert.Equal(AgentSupervisionClosureReason.AttemptTerminal, receipt.ClosureReason);
            Assert.Null(receipt.AttentionReason);
            Assert.Equal(AgentSupervisionPhase.RecordingResult, receipt.Phase);
        });
        Assert.Equal(4, receipts.Select(receipt => receipt.ObserverId).Distinct().Count());
        // Each coordinated attempt's observation appended exactly its registration, two phase entries and one closure, in that order
        // per attempt; no attention and no heartbeat event.
        var events = await AgentSupervisionHostedSupport.SupervisionEventsAsync(harness.Provider, scene.RunId);
        Assert.Equal(16, events.Count);
        Assert.All(attempts, attempt => Assert.Equal(
            [
                RunEventType.AgentSupervisionRegistered, RunEventType.AgentSupervisionPhaseEntered, RunEventType.AgentSupervisionPhaseEntered,
                RunEventType.AgentSupervisionClosed,
            ],
            events.Where(runEvent => runEvent.AttemptId == attempt.Id).Select(runEvent => runEvent.EventType)));

        var read = await ReadAsync(harness, scene.RunId);
        Assert.Equal(AgentSupervisionStatus.Ended, read.Status);
        Assert.Equal(attempts[^1].Id, read.Attempt!.AttemptId);
        Assert.Equal(AgentRole.CriticalReviewer, read.Attempt.Role);
    }

    [Fact]
    public async Task A_missing_observation_store_changes_nothing_about_the_coordinated_outcome()
    {
        var host = new AgentSupervisionHostInstance(Guid.NewGuid());
        await using var harness = ObservedHarness(host);
        var scene = await harness.SeedSceneAsync();
        await AgentSupervisionHostedSupport.ExecuteRawAsync(harness.DatabasePath, "DROP TABLE agent_supervisions;");
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, ended.State);
        Assert.Equal(PlanningSequenceReason.PlanAccepted, ended.ReasonCode);
        Assert.Equal((1, 1, 0), (harness.Planner.InvocationCount, harness.Reviewer.InvocationCount, harness.Resolver.InvocationCount));
    }

    private static async Task<GetAgentSupervisionQueryResult> ReadAsync(PlanningSequenceHostedHarness harness, Guid runId)
    {
        await using var scope = harness.Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IApplicationMediator>()
            .SendAsync(new GetAgentSupervisionQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
