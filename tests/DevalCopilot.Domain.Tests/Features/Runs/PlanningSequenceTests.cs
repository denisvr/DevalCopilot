using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class PlanningSequenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 64);

    private static PlanningSequence Armed() => PlanningSequence.Arm(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, new string('b', 64), Now);

    private static PlanningSequenceStep ReservedStep(int ordinal = 0) => PlanningSequenceStep.Reserve(
        Guid.NewGuid(),
        Guid.NewGuid(),
        ordinal,
        ordinal == 0 ? null : Guid.NewGuid(),
        ordinal == 0 ? null : Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Now);

    [Fact]
    public void Arming_records_the_exact_tuple_in_the_Armed_state_at_revision_one()
    {
        var sequence = Armed();

        Assert.Equal(PlanningSequenceState.Armed, sequence.State);
        Assert.Equal(1, sequence.Revision);
        Assert.Equal(Now, sequence.ArmedAtUtc);
        Assert.Null(sequence.ReasonCode);
        Assert.True(sequence.IsAuthorityLive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("aaaa")]
    public void Arming_rejects_a_fingerprint_that_is_not_64_lowercase_hex(string fingerprint)
    {
        Assert.Throws<ArgumentException>(() => PlanningSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), fingerprint, Sha, Now));
    }

    [Fact]
    public void The_progression_advances_through_exact_states_and_every_transition_advances_the_revision()
    {
        var sequence = Armed();

        sequence.BeginPreparing(Now.AddSeconds(1));
        Assert.Equal(PlanningSequenceState.PreparingStep, sequence.State);
        Assert.Equal(2, sequence.Revision);

        sequence.MarkAwaitingResult(Now.AddSeconds(2));
        Assert.Equal(PlanningSequenceState.AwaitingStepResult, sequence.State);
        Assert.Equal(3, sequence.Revision);

        sequence.BeginPreparing(Now.AddSeconds(3));
        Assert.Equal(PlanningSequenceState.PreparingStep, sequence.State);
        Assert.Equal(4, sequence.Revision);
    }

    [Fact]
    public void A_step_cannot_be_reserved_claimed_or_stopped_from_the_wrong_state()
    {
        var armed = Armed();
        Assert.Throws<InvalidOperationException>(() => armed.MarkAwaitingResult(Now));
        Assert.Throws<InvalidOperationException>(() => armed.BeginStopping(Now));

        armed.BeginPreparing(Now);
        Assert.Throws<InvalidOperationException>(() => armed.BeginPreparing(Now));
        Assert.Throws<InvalidOperationException>(() => armed.BeginStopping(Now));
    }

    [Fact]
    public void Stopping_is_reachable_only_after_a_claim_and_records_the_stop_time()
    {
        var sequence = Armed();
        sequence.BeginPreparing(Now);
        sequence.MarkAwaitingResult(Now);

        sequence.BeginStopping(Now.AddMinutes(1));

        Assert.Equal(PlanningSequenceState.Stopping, sequence.State);
        Assert.Equal(Now.AddMinutes(1), sequence.StopRequestedAtUtc);
        Assert.True(sequence.IsAuthorityLive);
    }

    [Theory]
    [InlineData(PlanningSequenceState.ReadyForImplementation, PlanningSequenceReason.PlanAccepted)]
    [InlineData(PlanningSequenceState.AwaitingHumanDecision, PlanningSequenceReason.SecondRoundEscalated)]
    [InlineData(PlanningSequenceState.Stopped, PlanningSequenceReason.StoppedBeforeClaim)]
    [InlineData(PlanningSequenceState.NeedsAttention, PlanningSequenceReason.ResultDeadlineElapsed)]
    [InlineData(PlanningSequenceState.Interrupted, PlanningSequenceReason.HostRestarted)]
    public void An_ended_sequence_records_its_fixed_reason_and_never_leaves_that_state(PlanningSequenceState endState, string reason)
    {
        var sequence = Armed();

        sequence.End(endState, reason, "agent_attempts.budget_exhausted", Now.AddMinutes(3));

        Assert.Equal(endState, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        Assert.Equal("agent_attempts.budget_exhausted", sequence.DetailCode);
        Assert.Equal(Now.AddMinutes(3), sequence.EndedAtUtc);
        Assert.False(sequence.IsAuthorityLive);
        Assert.Throws<InvalidOperationException>(() => sequence.End(PlanningSequenceState.Stopped, PlanningSequenceReason.StoppedBeforeClaim, null, Now));
        Assert.Throws<InvalidOperationException>(() => sequence.BeginPreparing(Now));
    }

    [Fact]
    public void Ending_rejects_a_live_state_and_an_unknown_reason()
    {
        var sequence = Armed();

        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.End(PlanningSequenceState.Stopping, PlanningSequenceReason.StoppedAfterStep, null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.End(PlanningSequenceState.Stopped, "made_up", null, Now));
    }

    [Theory]
    [InlineData("agent_attempts.budget_exhausted", "agent_attempts.budget_exhausted")]
    [InlineData("UPPER", null)]
    [InlineData("has space", null)]
    [InlineData("", null)]
    public void A_detail_is_stored_only_when_it_is_a_bounded_stable_code(string detail, string? expected)
    {
        Assert.Equal(expected, PlanningSequenceReason.NormalizeDetail(detail));
        Assert.Null(PlanningSequenceReason.NormalizeDetail(new string('a', PlanningSequenceReason.MaximumDetailLength + 1)));
    }

    [Theory]
    [InlineData(0, AgentRole.Planner, AgentResponseContract.Proposal, AgentProvider.Codex)]
    [InlineData(1, AgentRole.CriticalReviewer, AgentResponseContract.CriticalReview, AgentProvider.ClaudeCode)]
    [InlineData(2, AgentRole.Resolver, AgentResponseContract.ChallengeResolution, AgentProvider.Codex)]
    [InlineData(3, AgentRole.CriticalReviewer, AgentResponseContract.CriticalReview, AgentProvider.ClaudeCode)]
    [InlineData(4, AgentRole.Resolver, AgentResponseContract.ChallengeResolution, AgentProvider.Codex)]
    public void The_fixed_progression_assigns_one_role_contract_and_provider_per_ordinal(
        int ordinal, AgentRole role, AgentResponseContract contract, AgentProvider provider)
    {
        Assert.Equal(role, PlanningSequencePolicy.RoleOf(ordinal));
        Assert.Equal(contract, PlanningSequencePolicy.ContractOf(ordinal));
        Assert.Equal(provider, PlanningSequencePolicy.ProviderOf(ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void There_is_no_sixth_step(int ordinal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanningSequencePolicy.RoleOf(ordinal));
        Assert.Equal(5, PlanningSequencePolicy.MaximumSteps);
    }

    [Fact]
    public void The_result_deadline_is_claim_time_plus_the_reserved_timeout_plus_two_minutes()
    {
        var deadline = PlanningSequencePolicy.ResultDeadlineOf(Now, TimeSpan.FromMinutes(10));

        Assert.Equal(Now.AddMinutes(12), deadline);
        Assert.Equal(Now.AddMinutes(2), PlanningSequencePolicy.PreparationDeadlineOf(Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_result_deadline_is_never_fabricated_from_a_missing_or_non_positive_timeout(int? minutes)
    {
        Assert.Null(PlanningSequencePolicy.ResultDeadlineOf(Now, minutes is { } value ? TimeSpan.FromMinutes(value) : null));
    }

    [Fact]
    public void An_unrepresentable_result_deadline_is_null_instead_of_an_overflow()
    {
        Assert.Null(PlanningSequencePolicy.ResultDeadlineOf(DateTimeOffset.MaxValue.AddMinutes(-1), TimeSpan.FromMinutes(10)));
        Assert.Null(PlanningSequencePolicy.ResultDeadlineOf(Now, TimeSpan.MaxValue));
    }

    [Theory]
    [InlineData(PlanningSequenceState.Armed, true, true)]
    [InlineData(PlanningSequenceState.PreparingStep, true, true)]
    [InlineData(PlanningSequenceState.AwaitingStepResult, true, true)]
    [InlineData(PlanningSequenceState.Stopping, false, true)]
    [InlineData(PlanningSequenceState.ReadyForImplementation, false, false)]
    [InlineData(PlanningSequenceState.AwaitingHumanDecision, false, false)]
    [InlineData(PlanningSequenceState.Stopped, false, false)]
    [InlineData(PlanningSequenceState.NeedsAttention, false, false)]
    [InlineData(PlanningSequenceState.Interrupted, false, false)]
    public void Only_the_three_advancing_states_may_advance_and_only_those_plus_Stopping_hold_authority(
        PlanningSequenceState state, bool advancing, bool authorityLive)
    {
        Assert.Equal(advancing, PlanningSequencePolicy.IsAdvancing(state));
        Assert.Equal(authorityLive, PlanningSequencePolicy.IsAuthorityLive(state));
        Assert.Equal(!authorityLive, PlanningSequencePolicy.IsEnded(state));
    }

    [Fact]
    public void Only_the_Planner_step_has_no_predecessor_and_every_later_step_names_its_exact_input()
    {
        Assert.Throws<ArgumentException>(() => PlanningSequenceStep.Reserve(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => PlanningSequenceStep.Reserve(
            Guid.NewGuid(), Guid.NewGuid(), 1, null, null, Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => PlanningSequenceStep.Reserve(
            Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Now));

        var planner = ReservedStep(0);
        var review = ReservedStep(1);
        Assert.Null(planner.PredecessorAttemptId);
        Assert.NotNull(review.PredecessorAttemptId);
        Assert.NotNull(review.InputProposalMessageId);
        Assert.Equal(AgentRole.Planner, planner.Role);
        Assert.Equal(AgentRole.CriticalReviewer, review.Role);
    }

    [Fact]
    public void A_reserved_step_owns_a_two_minute_preparation_deadline_and_one_outstanding_authority()
    {
        var step = ReservedStep();

        Assert.Equal(PlanningSequenceStepState.Reserved, step.State);
        Assert.True(step.IsOutstanding);
        Assert.Equal(Now.AddMinutes(2), step.PreparationDeadlineUtc);
        Assert.Null(step.AttemptId);
        Assert.Equal(1, step.Revision);
    }

    [Fact]
    public void A_claim_links_only_the_exact_reserved_attempt_identity_once()
    {
        var step = ReservedStep();

        Assert.Throws<ArgumentException>(() => step.MarkClaimed(Guid.NewGuid(), Now));

        step.MarkClaimed(step.ReservedAttemptId, Now.AddSeconds(5));

        Assert.Equal(PlanningSequenceStepState.Claimed, step.State);
        Assert.Equal(step.ReservedAttemptId, step.AttemptId);
        Assert.True(step.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => step.MarkClaimed(step.ReservedAttemptId, Now));
        Assert.Throws<InvalidOperationException>(() => step.Revoke(PlanningSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => step.MarkUnresolved(PlanningSequenceReason.PreparationUnresolved, Now));
    }

    [Fact]
    public void A_claimed_step_concludes_once_and_a_concluded_step_is_no_longer_outstanding()
    {
        var step = ReservedStep();
        Assert.Throws<InvalidOperationException>(() => step.Conclude(Now));
        step.MarkClaimed(step.ReservedAttemptId, Now);

        step.Conclude(Now.AddMinutes(1));

        Assert.Equal(PlanningSequenceStepState.Concluded, step.State);
        Assert.False(step.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => step.Conclude(Now));
    }

    [Fact]
    public void A_revoked_or_unresolved_step_never_links_an_attempt_and_is_final()
    {
        var revoked = ReservedStep();
        revoked.Revoke(PlanningSequenceReason.PreparationExpired, Now);
        Assert.Equal(PlanningSequenceStepState.Revoked, revoked.State);
        Assert.Null(revoked.AttemptId);
        Assert.False(revoked.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => revoked.MarkClaimed(revoked.ReservedAttemptId, Now));

        var unresolved = ReservedStep();
        unresolved.MarkUnresolved(PlanningSequenceReason.PreparationUnresolved, Now);
        Assert.Equal(PlanningSequenceStepState.Unresolved, unresolved.State);
        Assert.Null(unresolved.AttemptId);
        Assert.Throws<InvalidOperationException>(() => unresolved.MarkClaimed(unresolved.ReservedAttemptId, Now));

        Assert.Throws<ArgumentOutOfRangeException>(() => ReservedStep().Revoke("made_up", Now));
    }
}
