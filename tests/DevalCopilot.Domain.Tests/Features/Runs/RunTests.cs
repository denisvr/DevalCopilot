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
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
        Assert.Equal(0, run.AccumulatedAutonomousSeconds);
    }

    [Fact]
    public void Claim_transitions_created_run_to_running_with_orchestrator_active()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        run.Claim(BaseTime.AddSeconds(1));

        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), run.ActiveParticipant);
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
            () => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void AdvanceStage_rejects_moving_backward_or_staying_on_the_same_stage()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(
            () => run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(
            () => run.AdvanceStage(RunStage.Intake, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void AdvanceStage_accumulates_autonomous_time_between_transitions()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);

        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(5));
        run.AdvanceStage(RunStage.Critique, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), BaseTime.AddSeconds(12));

        Assert.Equal(12, run.AccumulatedAutonomousSeconds);
    }

    [Fact]
    public void Complete_sets_terminal_lifecycle_and_clears_active_participant()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(1));

        run.Complete(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(RunStage.Completed, run.Stage);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
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
        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(1));

        run.Fail(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
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
        run.AdvanceStage(RunStage.Plan, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), BaseTime.AddSeconds(1));

        run.MarkInterrupted(BaseTime.AddSeconds(2));

        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Equal(ParticipantIdentity.None(), run.ActiveParticipant);
    }

    [Fact]
    public void MarkInterrupted_throws_when_run_is_not_running()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<InvalidOperationException>(() => run.MarkInterrupted(BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void A_new_run_has_no_requested_codex_assignment()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Null(run.RequestedCodexModel);
        Assert.Null(run.RequestedCodexEffort);
    }

    [Fact]
    public void SetRequestedCodexAssignment_sets_a_model_and_effort_pair_while_created()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        run.SetRequestedCodexAssignment("gpt-6-sol", "high");

        Assert.Equal("gpt-6-sol", run.RequestedCodexModel);
        Assert.Equal("high", run.RequestedCodexEffort);
    }

    [Fact]
    public void SetRequestedCodexAssignment_sets_a_model_only_while_running()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);

        run.SetRequestedCodexAssignment("gpt-6-sol", requestedEffort: null);

        Assert.Equal("gpt-6-sol", run.RequestedCodexModel);
        Assert.Null(run.RequestedCodexEffort);
    }

    [Fact]
    public void SetRequestedCodexAssignment_changes_a_previously_set_pair()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.SetRequestedCodexAssignment("gpt-6-sol", "high");

        run.SetRequestedCodexAssignment("gpt-6-mini", "low");

        Assert.Equal("gpt-6-mini", run.RequestedCodexModel);
        Assert.Equal("low", run.RequestedCodexEffort);
    }

    [Fact]
    public void SetRequestedCodexAssignment_clears_a_previously_set_pair()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.SetRequestedCodexAssignment("gpt-6-sol", "high");

        run.SetRequestedCodexAssignment(null, null);

        Assert.Null(run.RequestedCodexModel);
        Assert.Null(run.RequestedCodexEffort);
    }

    [Fact]
    public void SetRequestedCodexAssignment_rejects_an_effort_without_a_model()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<ArgumentException>(() => run.SetRequestedCodexAssignment(null, "high"));
    }

    [Fact]
    public void SetRequestedCodexAssignment_rejects_a_model_exceeding_the_bounded_length()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<ArgumentException>(() => run.SetRequestedCodexAssignment(new string('a', 129), null));
    }

    [Fact]
    public void SetRequestedCodexAssignment_rejects_a_blank_model()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Throws<ArgumentException>(() => run.SetRequestedCodexAssignment("   ", null));
    }

    [Theory]
    [InlineData(RunLifecycle.Completed)]
    [InlineData(RunLifecycle.Failed)]
    [InlineData(RunLifecycle.Interrupted)]
    public void SetRequestedCodexAssignment_throws_once_the_run_reaches_a_terminal_lifecycle(RunLifecycle terminalLifecycle)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        switch (terminalLifecycle)
        {
            case RunLifecycle.Completed:
                run.Complete(BaseTime.AddSeconds(1));
                break;
            case RunLifecycle.Failed:
                run.Fail(BaseTime.AddSeconds(1));
                break;
            case RunLifecycle.Interrupted:
                run.MarkInterrupted(BaseTime.AddSeconds(1));
                break;
        }

        Assert.Throws<InvalidOperationException>(() => run.SetRequestedCodexAssignment("gpt-6-sol", null));
    }

    [Fact]
    public void RequestedClaudeModel_defaults_to_null_for_a_new_run()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        Assert.Null(run.RequestedClaudeModel);
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("haiku")]
    public void SetRequestedClaudeModel_accepts_each_closed_alias_while_created_or_running(string alias)
    {
        var created = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        created.SetRequestedClaudeModelRequest(alias, null);
        Assert.Equal(alias, created.RequestedClaudeModel);

        var running = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        running.Claim(BaseTime);
        running.SetRequestedClaudeModelRequest(alias, null);
        Assert.Equal(alias, running.RequestedClaudeModel);
    }

    [Fact]
    public void SetRequestedClaudeModel_changes_and_clears_a_previous_request()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.SetRequestedClaudeModelRequest("opus", null);
        run.SetRequestedClaudeModelRequest("haiku", null);
        Assert.Equal("haiku", run.RequestedClaudeModel);

        run.SetRequestedClaudeModelRequest(null, null);
        Assert.Null(run.RequestedClaudeModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Opus")]
    [InlineData("opus ")]
    [InlineData("fable")]
    [InlineData("claude-opus-5-5")]
    public void SetRequestedClaudeModel_rejects_anything_outside_the_closed_alias_set_and_keeps_the_prior_value(string value)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.SetRequestedClaudeModelRequest("sonnet", null);

        Assert.Throws<ArgumentException>(() => run.SetRequestedClaudeModelRequest(value, null));
        Assert.Equal("sonnet", run.RequestedClaudeModel);
    }

    [Fact]
    public void SetRequestedClaudeModel_rejects_a_terminal_run_and_keeps_the_prior_value()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.SetRequestedClaudeModelRequest("sonnet", null);
        run.Complete(BaseTime.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeModelRequest(null, null));
        Assert.Equal("sonnet", run.RequestedClaudeModel);
    }

    [Theory]
    [InlineData("sonnet", "low")]
    [InlineData("sonnet", "medium")]
    [InlineData("sonnet", "high")]
    [InlineData("opus", "low")]
    [InlineData("opus", "medium")]
    [InlineData("opus", "high")]
    [InlineData("opus", null)]
    [InlineData("haiku", null)]
    [InlineData(null, null)]
    public void SetRequestedClaudeModelRequest_stores_each_valid_pair_atomically(string? model, string? effort)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);

        run.SetRequestedClaudeModelRequest(model, effort);

        Assert.Equal(model, run.RequestedClaudeModel);
        Assert.Equal(effort, run.RequestedClaudeEffort);
    }

    [Theory]
    [InlineData(null, "low")]
    [InlineData("haiku", "low")]
    [InlineData("haiku", "high")]
    [InlineData("sonnet", "Low")]
    [InlineData("sonnet", "HIGH")]
    [InlineData("sonnet", "high ")]
    [InlineData("sonnet", "")]
    [InlineData("opus", "max")]
    [InlineData("opus", "xhigh")]
    [InlineData("Opus", "low")]
    [InlineData("fable", "low")]
    public void SetRequestedClaudeModelRequest_rejects_an_invalid_pair_and_keeps_the_prior_pair(string? model, string? effort)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.SetRequestedClaudeModelRequest("opus", "medium");

        Assert.Throws<ArgumentException>(() => run.SetRequestedClaudeModelRequest(model, effort));

        Assert.Equal("opus", run.RequestedClaudeModel);
        Assert.Equal("medium", run.RequestedClaudeEffort);
    }

    [Fact]
    public void SetRequestedClaudeModelRequest_clears_both_values_and_a_new_run_has_neither()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        Assert.Null(run.RequestedClaudeEffort);

        run.SetRequestedClaudeModelRequest("sonnet", "high");
        run.SetRequestedClaudeModelRequest(null, null);

        Assert.Null(run.RequestedClaudeModel);
        Assert.Null(run.RequestedClaudeEffort);
    }

    [Fact]
    public void SetRequestedClaudeModelRequest_rejects_a_terminal_run_for_the_effort_too()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token budgets", BaseTime);
        run.Claim(BaseTime);
        run.SetRequestedClaudeModelRequest("sonnet", "low");
        run.Complete(BaseTime.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.SetRequestedClaudeModelRequest("opus", "high"));

        Assert.Equal("sonnet", run.RequestedClaudeModel);
        Assert.Equal("low", run.RequestedClaudeEffort);
    }

    [Fact]
    public void Token_warning_thresholds_default_to_null_for_a_new_run()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);

        Assert.Null(run.CodexTokenWarningThreshold);
        Assert.Null(run.ClaudeTokenWarningThreshold);
    }

    [Fact]
    public void SetTokenWarningThreshold_sets_each_provider_independently_and_clears_one_without_the_other()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);
        run.Claim(BaseTime);

        run.SetTokenWarningThreshold(AgentProvider.Codex, 1_000);
        run.SetTokenWarningThreshold(AgentProvider.ClaudeCode, 50_000);
        Assert.Equal(1_000, run.CodexTokenWarningThreshold);
        Assert.Equal(50_000, run.ClaudeTokenWarningThreshold);

        run.SetTokenWarningThreshold(AgentProvider.Codex, null);
        Assert.Null(run.CodexTokenWarningThreshold);
        Assert.Equal(50_000, run.ClaudeTokenWarningThreshold);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(Run.MaxTokenWarningThreshold)]
    public void SetTokenWarningThreshold_accepts_the_inclusive_bounds(long threshold)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);

        run.SetTokenWarningThreshold(AgentProvider.ClaudeCode, threshold);

        Assert.Equal(threshold, run.ClaudeTokenWarningThreshold);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(Run.MaxTokenWarningThreshold + 1)]
    [InlineData(long.MaxValue)]
    public void SetTokenWarningThreshold_rejects_zero_negative_and_overflowing_values_keeping_the_prior_value(long threshold)
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);
        run.SetTokenWarningThreshold(AgentProvider.Codex, 7);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.SetTokenWarningThreshold(AgentProvider.Codex, threshold));
        Assert.Equal(7, run.CodexTokenWarningThreshold);
    }

    [Fact]
    public void SetTokenWarningThreshold_rejects_an_undefined_provider()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);

        Assert.Throws<ArgumentOutOfRangeException>(() => run.SetTokenWarningThreshold((AgentProvider)99, 5));
    }

    [Fact]
    public void SetTokenWarningThreshold_rejects_a_terminal_run_and_keeps_the_prior_value()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Add token warnings", BaseTime);
        run.Claim(BaseTime);
        run.SetTokenWarningThreshold(AgentProvider.Codex, 9);
        run.Complete(BaseTime.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.SetTokenWarningThreshold(AgentProvider.Codex, null));
        Assert.Equal(9, run.CodexTokenWarningThreshold);
    }
}
