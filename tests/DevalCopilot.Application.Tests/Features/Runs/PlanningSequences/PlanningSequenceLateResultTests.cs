using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// ADR-0055 result deadline for a terminal attempt that finished before the coordinator swept it. A completion at or after the
/// deadline never authorizes a successor or successful planning, whether it is observed by the coordinator or read by the status
/// query; a timely completion that is merely observed later is not late; the terminal attempt and its outputs stay as recorded; and a
/// Stop that already won keeps Stopped.
/// </summary>
public sealed class PlanningSequenceLateResultTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    /// <summary>Completes the steps that precede the step under test (each observation concludes it and reserves the next), then claims
    /// the step under test.</summary>
    private static async Task<Guid> ClaimStepUnderTestAsync(PlanningSequenceTestScene scene, Guid sequenceId, StepResult result)
    {
        StepResult[] preceding = result switch
        {
            StepResult.Proposed => [],
            StepResult.Accepted => [StepResult.Proposed],
            _ => [StepResult.Proposed, StepResult.Challenged],
        };
        foreach (var previous in preceding)
        {
            var (_, claimed) = await scene.ReserveAndClaimAsync(sequenceId);
            await scene.CompleteStepAsync(claimed.AttemptId, previous);
        }

        var (_, claim) = await scene.ReserveAndClaimAsync(sequenceId);
        return claim.AttemptId;
    }

    private async Task<(Guid AttemptId, DateTimeOffset Deadline)> DispatchedStepAsync(
        PlanningSequenceTestScene scene, Guid sequenceId, StepResult result)
    {
        var attemptId = await ClaimStepUnderTestAsync(scene, sequenceId, result);
        await using var context = fixture.CreateContext();
        var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        attempt.MarkAgentDispatched(scene.Clock.GetUtcNow());
        await context.SaveChangesAsync();
        var deadline = PlanningSequencePolicy.ResultDeadlineOf(attempt.ClaimedAtUtc, attempt.AgentTimeout);
        Assert.NotNull(deadline);
        return (attemptId, deadline!.Value);
    }

    [Theory]
    [InlineData(StepResult.Proposed)]
    [InlineData(StepResult.Accepted)]
    [InlineData(StepResult.Resolved)]
    public async Task A_completion_at_the_exact_deadline_is_late_so_it_authorizes_nothing_and_the_attempt_stays_recorded(StepResult result)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (attemptId, deadline) = await DispatchedStepAsync(scene, armed.SequenceId, result);
        var stepsBefore = (await scene.ReadStepsAsync(armed.SequenceId)).Count;

        scene.Clock.Advance(deadline - scene.Clock.GetUtcNow());
        await scene.CompleteStepAsync(attemptId, result, dispatch: false);
        await using (var context = fixture.CreateContext())
        {
            var status = await new GetPlanningSequenceQueryHandler(context, scene.Clock)
                .HandleAsync(new GetPlanningSequenceQuery(scene.RunId), CancellationToken.None);
            Assert.Equal(PlanningSequenceState.NeedsAttention, status.Value.Sequence!.State);
            Assert.Equal(PlanningSequenceState.NeedsAttention, status.Value.State);
            Assert.Equal(PlanningSequenceState.AwaitingStepResult, status.Value.Sequence.RecordedState);
            Assert.Equal(PlanningSequenceReason.ResultDeadlineElapsed, status.Value.Sequence.ReasonCode);
            Assert.True(status.Value.Sequence.ObservedBeforePersisted);
        }

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.True(observed.IsSuccess);
        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(PlanningSequenceReason.ResultDeadlineElapsed, sequence.ReasonCode);
        Assert.Equal(stepsBefore, (await scene.ReadStepsAsync(armed.SequenceId)).Count);

        // The truthful terminal attempt is retained, and its step is concluded rather than fabricated away.
        var attempt = (await scene.ReadAttemptsAsync()).Single(candidate => candidate.Id == attemptId);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.AgentOutcome);
        Assert.Equal(PlanningSequenceStepState.Concluded, (await scene.ReadStepsAsync(armed.SequenceId)).Single(step => step.AttemptId == attemptId).State);
    }

    [Theory]
    [InlineData(StepResult.Proposed)]
    [InlineData(StepResult.Accepted)]
    [InlineData(StepResult.Resolved)]
    public async Task A_completion_one_tick_before_the_deadline_is_timely_even_when_it_is_observed_after_the_deadline(StepResult result)
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (attemptId, deadline) = await DispatchedStepAsync(scene, armed.SequenceId, result);

        scene.Clock.Advance(deadline - scene.Clock.GetUtcNow() - TimeSpan.FromTicks(1));
        await scene.CompleteStepAsync(attemptId, result, dispatch: false);
        scene.Clock.Advance(TimeSpan.FromMinutes(30));
        await using (var context = fixture.CreateContext())
        {
            var status = await new GetPlanningSequenceQueryHandler(context, scene.Clock)
                .HandleAsync(new GetPlanningSequenceQuery(scene.RunId), CancellationToken.None);
            Assert.Equal(PlanningSequenceState.AwaitingStepResult, status.Value.Sequence!.State);
            Assert.False(status.Value.Sequence.ObservedBeforePersisted);
        }

        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.True(observed.IsSuccess);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        if (result == StepResult.Accepted)
        {
            Assert.Equal(PlanningSequenceState.ReadyForImplementation, sequence.State);
            Assert.Null(observed.Value.ReservedStep);
        }
        else
        {
            Assert.Equal(PlanningSequenceState.PreparingStep, sequence.State);
            Assert.NotNull(observed.Value.ReservedStep);
        }
    }

    [Fact]
    public async Task A_stop_that_won_before_a_late_completion_keeps_the_sequence_stopped()
    {
        var scene = await PlanningSequenceTestScene.CreateAsync(fixture);
        var armed = await scene.ArmAsync();
        var (attemptId, deadline) = await DispatchedStepAsync(scene, armed.SequenceId, StepResult.Proposed);
        Assert.True((await scene.StopAsync(armed.SequenceId)).IsSuccess);

        scene.Clock.Advance(deadline - scene.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        await scene.CompleteStepAsync(attemptId, StepResult.Proposed, dispatch: false);
        var observed = await scene.ObserveAsync(armed.SequenceId);

        Assert.Null(observed.Value.ReservedStep);
        var sequence = await scene.ReadSequenceAsync(armed.SequenceId);
        Assert.Equal(PlanningSequenceState.Stopped, sequence.State);
        Assert.Equal(PlanningSequenceReason.StoppedAfterStep, sequence.ReasonCode);
        Assert.Equal(AttemptStatus.Completed, Assert.Single(await scene.ReadAttemptsAsync()).Status);
    }
}
