using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0060: arming validation, the exact state machine, revision advance and the final-reference rules of one local
/// execution sequence.</summary>
public sealed class LocalExecutionSequenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 64);

    private static LocalExecutionSequence Armed(
        string? fingerprint = null,
        string? planInputs = null,
        string? confirmation = null,
        string settings = "v1;settings",
        int recipeCount = 2,
        int allowance = 2,
        LocalExecutionPlanAuthority authority = LocalExecutionPlanAuthority.AcceptedRoot,
        DateTimeOffset? armedAt = null,
        Guid? id = null) => LocalExecutionSequence.Arm(
        id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), fingerprint ?? Sha, Guid.NewGuid(),
        authority, planInputs ?? new string('b', 64), confirmation ?? new string('c', 64), settings, recipeCount, allowance,
        armedAt ?? Now);

    [Fact]
    public void Arming_records_the_exact_tuple_in_the_Armed_state_at_revision_one_with_a_two_hour_deadline()
    {
        var sequence = Armed(recipeCount: 3, allowance: 1, authority: LocalExecutionPlanAuthority.HumanAuthorizedRevision);

        Assert.Equal(LocalExecutionSequenceState.Armed, sequence.State);
        Assert.Equal(1, sequence.Revision);
        Assert.Equal(Now, sequence.ArmedAtUtc);
        Assert.Equal(Now, sequence.UpdatedAtUtc);
        Assert.Equal(Now.AddHours(2), sequence.AdvancementDeadlineUtc);
        Assert.Equal(3, sequence.RecipeCount);
        Assert.Equal(1, sequence.CorrectionAllowance);
        Assert.Equal(LocalExecutionPlanAuthority.HumanAuthorizedRevision, sequence.PlanAuthority);
        Assert.Equal(Sha, sequence.FingerprintSha256);
        Assert.Equal("v1;settings", sequence.SettingsSnapshot);
        Assert.Null(sequence.ReasonCode);
        Assert.Null(sequence.DetailCode);
        Assert.Null(sequence.EndedAtUtc);
        Assert.Null(sequence.StopRequestedAtUtc);
        Assert.Null(sequence.FinalReportMessageId);
        Assert.Null(sequence.FinalReviewAttemptId);
        Assert.Null(sequence.FinalEscalationMessageId);
        Assert.True(sequence.IsAuthorityLive);
    }

    [Fact]
    public void Arming_rejects_an_empty_identifier()
    {
        Assert.Throws<ArgumentException>(() => Armed(id: Guid.Empty));
        var good = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => LocalExecutionSequence.Arm(
            good, Guid.Empty, good, good, good, Sha, good, LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "s", 1, 0, Now));
        Assert.Throws<ArgumentException>(() => LocalExecutionSequence.Arm(
            good, good, good, good, good, Sha, Guid.Empty, LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "s", 1, 0, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("aaaa")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Arming_rejects_a_fingerprint_that_is_not_64_lowercase_hex(string value)
    {
        Assert.Throws<ArgumentException>(() => Armed(fingerprint: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Arming_rejects_a_plan_inputs_hash_that_is_not_64_lowercase_hex(string value)
    {
        Assert.Throws<ArgumentException>(() => Armed(planInputs: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Arming_rejects_a_confirmation_hash_that_is_not_64_lowercase_hex(string value)
    {
        Assert.Throws<ArgumentException>(() => Armed(confirmation: value));
    }

    [Fact]
    public void Arming_rejects_an_undefined_plan_authority()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Armed(authority: (LocalExecutionPlanAuthority)99));
    }

    [Fact]
    public void Arming_requires_a_bounded_settings_snapshot()
    {
        Assert.Throws<ArgumentException>(() => Armed(settings: string.Empty));
        Assert.Throws<ArgumentException>(() => Armed(settings: new string('s', LocalExecutionSequence.MaximumSettingsSnapshotLength + 1)));
        Assert.Equal(
            LocalExecutionSequence.MaximumSettingsSnapshotLength,
            Armed(settings: new string('s', LocalExecutionSequence.MaximumSettingsSnapshotLength)).SettingsSnapshot.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(-1)]
    public void Arming_rejects_a_recipe_count_outside_one_to_thirty_two(int recipeCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Armed(recipeCount: recipeCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void Arming_accepts_the_recipe_count_bounds(int recipeCount)
    {
        Assert.Equal(recipeCount, Armed(recipeCount: recipeCount).RecipeCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Arming_rejects_a_correction_allowance_outside_zero_to_two(int allowance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Armed(allowance: allowance));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Arming_accepts_every_allowance_from_zero_to_two(int allowance)
    {
        Assert.Equal(allowance, Armed(allowance: allowance).CorrectionAllowance);
    }

    [Fact]
    public void Arming_rejects_a_default_arming_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Armed(armedAt: default(DateTimeOffset)));
    }

    [Fact]
    public void The_progression_advances_through_exact_states_and_every_transition_advances_the_revision()
    {
        var sequence = Armed();

        sequence.BeginPreparing(Now.AddSeconds(1));
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, sequence.State);
        Assert.Equal(2, sequence.Revision);
        Assert.Equal(Now.AddSeconds(1), sequence.UpdatedAtUtc);

        sequence.MarkAwaitingResult(Now.AddSeconds(2));
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, sequence.State);
        Assert.Equal(3, sequence.Revision);

        sequence.BeginPreparing(Now.AddSeconds(3));
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, sequence.State);
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

        armed.MarkAwaitingResult(Now);
        Assert.Throws<InvalidOperationException>(() => armed.MarkAwaitingResult(Now));

        armed.BeginStopping(Now);
        Assert.Throws<InvalidOperationException>(() => armed.BeginPreparing(Now));
        Assert.Throws<InvalidOperationException>(() => armed.BeginStopping(Now));
        Assert.Throws<InvalidOperationException>(() => armed.MarkAwaitingResult(Now));
    }

    [Fact]
    public void A_transition_rejects_a_default_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Armed().BeginPreparing(default));
    }

    [Fact]
    public void Stopping_is_reachable_only_after_a_claim_and_records_the_stop_time()
    {
        var sequence = Armed();
        sequence.BeginPreparing(Now);
        sequence.MarkAwaitingResult(Now);

        sequence.BeginStopping(Now.AddMinutes(1));

        Assert.Equal(LocalExecutionSequenceState.Stopping, sequence.State);
        Assert.Equal(Now.AddMinutes(1), sequence.StopRequestedAtUtc);
        Assert.Equal(4, sequence.Revision);
        Assert.True(sequence.IsAuthorityLive);
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim)]
    [InlineData(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.ResultDeadlineElapsed)]
    [InlineData(LocalExecutionSequenceState.Interrupted, LocalExecutionSequenceReason.HostRestarted)]
    public void An_ended_sequence_records_its_fixed_reason_and_never_leaves_that_state(
        LocalExecutionSequenceState endState, string reason)
    {
        var sequence = Armed();

        sequence.End(endState, reason, "agent_attempts.budget_exhausted", Now.AddMinutes(3));

        Assert.Equal(endState, sequence.State);
        Assert.Equal(reason, sequence.ReasonCode);
        Assert.Equal("agent_attempts.budget_exhausted", sequence.DetailCode);
        Assert.Equal(Now.AddMinutes(3), sequence.EndedAtUtc);
        Assert.Equal(2, sequence.Revision);
        Assert.False(sequence.IsAuthorityLive);
        Assert.Throws<InvalidOperationException>(
            () => sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, null, Now));
        Assert.Throws<InvalidOperationException>(() => sequence.BeginPreparing(Now));
    }

    [Fact]
    public void Stopping_an_unclaimed_sequence_records_the_stop_time_when_it_ends_as_Stopped()
    {
        var sequence = Armed();

        sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, null, Now.AddMinutes(1));

        Assert.Equal(Now.AddMinutes(1), sequence.StopRequestedAtUtc);
    }

    [Fact]
    public void A_Stopped_end_keeps_the_original_stop_request_time()
    {
        var sequence = Armed();
        sequence.BeginPreparing(Now);
        sequence.MarkAwaitingResult(Now);
        sequence.BeginStopping(Now.AddMinutes(1));

        sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedAfterOperation, null, Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), sequence.StopRequestedAtUtc);
        Assert.Equal(Now.AddMinutes(5), sequence.EndedAtUtc);
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Armed)]
    [InlineData(LocalExecutionSequenceState.PreparingStep)]
    [InlineData(LocalExecutionSequenceState.AwaitingStepResult)]
    [InlineData(LocalExecutionSequenceState.Stopping)]
    public void Ending_rejects_a_live_state(LocalExecutionSequenceState liveState)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Armed().End(liveState, LocalExecutionSequenceReason.StoppedBeforeClaim, null, Now));
    }

    [Fact]
    public void Ending_rejects_an_unknown_reason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Armed().End(LocalExecutionSequenceState.Stopped, "made_up", null, Now));
    }

    [Fact]
    public void An_unnormalizable_detail_is_dropped_instead_of_stored()
    {
        var sequence = Armed();

        sequence.End(LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, "Has Space", Now);

        Assert.Null(sequence.DetailCode);
    }

    [Fact]
    public void An_approved_end_requires_the_exact_report_and_review_and_no_escalation()
    {
        var report = Guid.NewGuid();
        var review = Guid.NewGuid();
        var reason = LocalExecutionSequenceReason.ReviewApproved;
        var ready = LocalExecutionSequenceState.ReadyForHumanApproval;

        Assert.Throws<ArgumentException>(() => Armed().End(ready, reason, null, Now));
        Assert.Throws<ArgumentException>(() => Armed().End(ready, reason, null, Now, finalReportMessageId: report));
        Assert.Throws<ArgumentException>(() => Armed().End(ready, reason, null, Now, finalReviewAttemptId: review));
        Assert.Throws<ArgumentException>(
            () => Armed().End(ready, reason, null, Now, report, review, finalEscalationMessageId: Guid.NewGuid()));

        var sequence = Armed();
        sequence.End(ready, reason, null, Now, report, review);

        Assert.Equal(report, sequence.FinalReportMessageId);
        Assert.Equal(review, sequence.FinalReviewAttemptId);
        Assert.Null(sequence.FinalEscalationMessageId);
        Assert.Equal(LocalExecutionSequenceState.ReadyForHumanApproval, sequence.State);
    }

    [Fact]
    public void An_escalated_end_requires_the_exact_report_and_escalation_and_no_review()
    {
        var report = Guid.NewGuid();
        var escalation = Guid.NewGuid();
        var reason = LocalExecutionSequenceReason.AllowanceReached;
        var ended = LocalExecutionSequenceState.HumanEscalation;

        Assert.Throws<ArgumentException>(() => Armed().End(ended, reason, null, Now));
        Assert.Throws<ArgumentException>(() => Armed().End(ended, reason, null, Now, finalReportMessageId: report));
        Assert.Throws<ArgumentException>(() => Armed().End(ended, reason, null, Now, finalEscalationMessageId: escalation));
        Assert.Throws<ArgumentException>(
            () => Armed().End(ended, reason, null, Now, report, finalReviewAttemptId: Guid.NewGuid(), finalEscalationMessageId: escalation));

        var sequence = Armed();
        sequence.End(ended, reason, null, Now, report, finalEscalationMessageId: escalation);

        Assert.Equal(report, sequence.FinalReportMessageId);
        Assert.Equal(escalation, sequence.FinalEscalationMessageId);
        Assert.Null(sequence.FinalReviewAttemptId);
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Stopped)]
    [InlineData(LocalExecutionSequenceState.NeedsAttention)]
    [InlineData(LocalExecutionSequenceState.Interrupted)]
    public void Only_an_approved_or_escalated_end_carries_final_references(LocalExecutionSequenceState endState)
    {
        var reason = LocalExecutionSequenceReason.OperationFailed;

        Assert.Throws<ArgumentException>(() => Armed().End(endState, reason, null, Now, finalReportMessageId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Armed().End(endState, reason, null, Now, finalReviewAttemptId: Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Armed().End(endState, reason, null, Now, finalEscalationMessageId: Guid.NewGuid()));
    }

    [Fact]
    public void A_refused_end_leaves_the_sequence_unchanged()
    {
        var sequence = Armed();

        Assert.Throws<ArgumentException>(
            () => sequence.End(LocalExecutionSequenceState.ReadyForHumanApproval, LocalExecutionSequenceReason.ReviewApproved, null, Now));

        Assert.Equal(LocalExecutionSequenceState.Armed, sequence.State);
        Assert.Equal(1, sequence.Revision);
        Assert.Null(sequence.EndedAtUtc);
    }

    [Fact]
    public void The_state_values_are_the_durable_closed_vocabulary()
    {
        Assert.Equal(
            ["Armed", "PreparingStep", "AwaitingStepResult", "Stopping", "ReadyForHumanApproval", "HumanEscalation", "Stopped", "NeedsAttention", "Interrupted"],
            Enum.GetNames<LocalExecutionSequenceState>());
    }
}
