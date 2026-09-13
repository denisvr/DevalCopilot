using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class RunTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordIntent_creates_a_run_with_created_lifecycle_and_no_active_participant()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), executionNumber: 1, "Add token budgets", BaseTime);

        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Equal(ParticipantKind.None, run.ActiveParticipant);
        Assert.Equal(0, run.AccumulatedAutonomousSeconds);
    }

    [Fact]
    public void Claim_transitions_created_run_to_running_with_orchestrator_active()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        run.Claim(BaseTime.AddSeconds(1));

        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(ParticipantKind.Orchestrator, run.ActiveParticipant);
    }

    [Fact]
    public void Claim_throws_when_run_is_not_created()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => run.Claim(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void AdvanceStage_throws_when_run_has_not_been_claimed()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<InvalidOperationException>(
            () => run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void AdvanceStage_rejects_moving_backward_or_staying_on_the_same_stage()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(
            () => run.AdvanceStage(RunStage.Intake, ParticipantKind.Codex, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void AdvanceStage_accumulates_autonomous_time_between_transitions()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);

        run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(5));
        run.AdvanceStage(RunStage.Critique, ParticipantKind.Claude, BaseTime.AddSeconds(12));

        Assert.Equal(12, run.AccumulatedAutonomousSeconds);
    }

    [Fact]
    public void Complete_sets_terminal_lifecycle_and_clears_active_participant()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(1));

        run.Complete(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(RunStage.Completed, run.Stage);
        Assert.Equal(ParticipantKind.None, run.ActiveParticipant);
    }

    [Fact]
    public void Complete_throws_when_run_is_already_terminal()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.Complete(BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => run.Complete(BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void Fail_sets_terminal_lifecycle_and_leaves_stage_unchanged()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(1));

        run.Fail(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Equal(ParticipantKind.None, run.ActiveParticipant);
    }

    [Fact]
    public void Fail_throws_when_run_is_not_running()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<InvalidOperationException>(() => run.Fail(BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void MarkInterrupted_sets_terminal_lifecycle_and_leaves_stage_unchanged()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantKind.Codex, BaseTime.AddSeconds(1));

        run.MarkInterrupted(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Equal(ParticipantKind.None, run.ActiveParticipant);
    }

    [Fact]
    public void MarkInterrupted_throws_when_run_is_not_running()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<InvalidOperationException>(() => run.MarkInterrupted(BaseTime.AddSeconds(1)));
    }
}
