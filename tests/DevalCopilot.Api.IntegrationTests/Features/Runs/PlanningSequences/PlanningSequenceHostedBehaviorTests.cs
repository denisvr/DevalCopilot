using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetPlanningSequence;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055 behavior that needs the live coordinator and supervisors together: Stop after current step while a provider call
/// is still outstanding, independent progress of unrelated projects, and two coordinators racing one sequence.</summary>
public sealed class PlanningSequenceHostedBehaviorTests
{
    [Fact]
    public async Task Stop_after_current_step_lets_the_outstanding_provider_call_finish_and_creates_no_successor()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        harness.Planner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();
        await harness.WaitForSequenceAsync(armed.SequenceId, sequence => sequence.State == PlanningSequenceState.AwaitingStepResult);
        while (harness.Planner.InvocationCount == 0)
        {
            await Task.Delay(50);
        }

        var stop = await harness.SendAsync(new StopPlanningSequenceCommand(scene.RunId, armed.SequenceId));
        Assert.Equal(PlanningSequenceState.Stopping, stop.Value.State);

        // The claimed read-only step was not cancelled: its provider call completes and its valid proposal is recorded.
        harness.Planner.Gate.SetResult();
        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.Stopped, ended.State);
        Assert.Equal(PlanningSequenceReason.StoppedAfterStep, ended.ReasonCode);
        Assert.Equal(0, harness.Reviewer.InvocationCount);
        await using var context = harness.OpenContext();
        var attempt = Assert.Single(await context.Attempts.AsNoTracking().Where(candidate => candidate.RunId == scene.RunId).ToListAsync());
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Proposed, attempt.AgentOutcome);
        Assert.Single(await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed.SequenceId).ToListAsync());
    }

    [Fact]
    public async Task A_slow_provider_of_one_project_never_blocks_another_projects_sequence_from_ending()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var slowScene = await harness.SeedSceneAsync();
        var fastScene = await harness.SeedSceneAsync();
        var slowArmed = await harness.ArmAsync(slowScene);

        // Every Planner call waits on this gate (a still-running provider process); coordination itself must stay independent.
        harness.Planner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.StartHostsAsync();
        await harness.WaitForSequenceAsync(slowArmed.SequenceId, sequence => sequence.State == PlanningSequenceState.AwaitingStepResult);
        while (harness.Planner.InvocationCount == 0)
        {
            await Task.Delay(50);
        }

        // The slow project's provider call is outstanding; the other project must still be preparing and claiming independently.
        var fastArmed = await harness.ArmAsync(fastScene);
        await harness.WaitForSequenceAsync(fastArmed.SequenceId, sequence => sequence.State == PlanningSequenceState.AwaitingStepResult);
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, (await harness.WaitForSequenceAsync(slowArmed.SequenceId, _ => true)).State);

        harness.Planner.Gate.SetResult();
        var slow = await harness.WaitForEndAsync(slowArmed.SequenceId);
        var fast = await harness.WaitForEndAsync(fastArmed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, slow.State);
        Assert.Equal(PlanningSequenceState.ReadyForImplementation, fast.State);
        Assert.Equal(2, harness.Planner.InvocationCount);
        Assert.Equal(2, harness.Reviewer.InvocationCount);
    }

    [Fact]
    public async Task Two_coordinators_over_one_database_create_each_step_once_and_never_double_invoke_a_provider()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        harness.Reviewer.Decisions.Enqueue(1);
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartHostsAsync();
        await harness.StartCoordinatorAsync();

        var ended = await harness.WaitForEndAsync(armed.SequenceId);

        Assert.Equal(PlanningSequenceState.ReadyForImplementation, ended.State);
        Assert.Equal(1, harness.Planner.InvocationCount);
        Assert.Equal(2, harness.Reviewer.InvocationCount);
        Assert.Equal(1, harness.Resolver.InvocationCount);
        await using var context = harness.OpenContext();
        var steps = await context.PlanningSequenceSteps.AsNoTracking().Where(step => step.SequenceId == armed.SequenceId).ToListAsync();
        Assert.Equal(4, steps.Count);
        Assert.Equal(4, steps.Select(step => step.Ordinal).Distinct().Count());
        Assert.Equal(4, (await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId).ToListAsync()).Count);
    }

    [Fact]
    public async Task A_manual_planning_request_for_an_armed_run_is_refused_through_the_real_pipeline_and_creates_nothing()
    {
        await using var harness = new PlanningSequenceHostedHarness();
        var scene = await harness.SeedSceneAsync();
        await harness.ArmAsync(scene);

        var result = await harness.SendAsync(
            new DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt.CreateCodexPlanningAttemptCommand(scene.RunId));

        Assert.True(result.IsFailure);
        Assert.Equal("planning_sequence.manual_control_blocked", result.Errors[0].Code);
        Assert.Equal(0, harness.Planner.InvocationCount);
        await using var context = harness.OpenContext();
        Assert.Empty(await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId).ToListAsync());
    }

    [Fact]
    public async Task A_blocked_preparation_is_expired_by_an_independent_observation_and_its_late_return_claims_nothing()
    {
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        var evidence = new BlockingEvidence();
        await using var harness = new PlanningSequenceHostedHarness((services, _) =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        });
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        await harness.StartCoordinatorAsync();
        try
        {
            // The original worker is parked inside the external Agent-context capture.
            await evidence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(PlanningSequencePolicy.PreparationDeadline + TimeSpan.FromMinutes(1));

            // A later poll expires the reservation durably while that capture is still blocked.
            var expired = await harness.WaitForSequenceAsync(armed.SequenceId, sequence => sequence.State == PlanningSequenceState.NeedsAttention);
            Assert.Equal(PlanningSequenceReason.PreparationExpired, expired.ReasonCode);
            await using (var context = harness.OpenContext())
            {
                Assert.Empty(await context.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId).ToListAsync());
                Assert.Equal(PlanningSequenceStepState.Revoked, Assert.Single(await context.PlanningSequenceSteps.AsNoTracking()
                    .Where(step => step.SequenceId == armed.SequenceId).ToListAsync()).State);
            }
        }
        finally
        {
            evidence.Release.TrySetResult();
        }

        // Releasing the capture lets the original worker return late: it loses the claim and consumes nothing.
        await Task.Delay(1500);
        await using var after = harness.OpenContext();
        Assert.Empty(await after.Attempts.AsNoTracking().Where(attempt => attempt.RunId == scene.RunId).ToListAsync());
        Assert.Equal(PlanningSequenceState.NeedsAttention, (await after.PlanningSequences.AsNoTracking()
            .SingleAsync(sequence => sequence.Id == armed.SequenceId)).State);
        Assert.Equal(0, harness.Planner.InvocationCount + harness.Reviewer.InvocationCount + harness.Resolver.InvocationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_expired_deadline_is_reported_with_one_coherent_effective_status_and_the_recorded_state_kept(bool claimed)
    {
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        await using var harness = new PlanningSequenceHostedHarness((services, _) => services.AddSingleton<TimeProvider>(clock));
        var scene = await harness.SeedSceneAsync();
        var armed = await harness.ArmAsync(scene);
        var observed = await harness.SendAsync(new ObservePlanningSequenceCommand(armed.SequenceId));
        Assert.True(observed.IsSuccess);
        if (claimed)
        {
            var claim = await harness.SendAsync(new ClaimPlanningSequenceStepCommand(observed.Value.ReservedStep!));
            Assert.True(claim.IsSuccess);
        }

        clock.Advance(claimed ? TimeSpan.FromMinutes(13) : TimeSpan.FromMinutes(3));
        await using var scope = harness.Provider.CreateAsyncScope();
        var endpoint = new GetPlanningSequenceEndpoint(scope.ServiceProvider.GetRequiredService<IApplicationMediator>(), null!);
        var result = await endpoint.GetPlanningSequence(scene.RunId, CancellationToken.None);
        var wire = Assert.IsType<GetPlanningSequenceResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // The strict frontend decoder requires the top-level status, the nested state and the observed-before-persisted flag to agree.
        Assert.Equal("NeedsAttention", wire.Sequence!.State);
        Assert.Equal(wire.Sequence.State, wire.Status);
        Assert.Equal(claimed ? "AwaitingStepResult" : "PreparingStep", wire.Sequence.RecordedState);
        Assert.True(wire.Sequence.ObservedBeforePersisted);
        Assert.Equal(claimed ? PlanningSequenceReason.ResultDeadlineElapsed : PlanningSequenceReason.PreparationExpired, wire.Sequence.ReasonCode);
        Assert.Null(wire.Sequence.EndedAtUtc);
    }

    private sealed class ControllableClock(DateTimeOffset now) : TimeProvider
    {
        private long ticks = now.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

        public void Advance(TimeSpan span) => Interlocked.Add(ref ticks, span.Ticks);
    }

    private sealed class BlockingEvidence : IGitWorkspaceEvidenceReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), PlanningSequenceHostedHarness.Fingerprint, [], null));

        public async Task<GitWorkspaceEvidenceResult> CaptureForAgentContextAsync(
            string workspacePath, bool includeUntrackedPreviews, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await CaptureAsync(workspacePath, cancellationToken);
        }
    }
}
