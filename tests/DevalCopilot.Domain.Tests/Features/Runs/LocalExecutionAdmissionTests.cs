using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The queue vocabulary and lifecycle rules of ADR-0062: the admission record, the order counter, the mode of a sequence and
/// the closed step-effect mapping.</summary>
public sealed class LocalExecutionAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 64);

    private static LocalExecutionAdmission Waiting(
        LocalExecutionStepKind kind = LocalExecutionStepKind.Implementation,
        int ordinal = 0,
        int pass = 0,
        int? recipe = null,
        Guid? predecessor = null,
        Guid? report = null,
        Guid? subject = null,
        long number = 2) =>
        LocalExecutionAdmission.Enqueue(
            Guid.NewGuid(), Guid.NewGuid(), number, ordinal, kind, pass, recipe, predecessor, Guid.NewGuid(), Sha, report, subject, Now);

    [Fact]
    public void Enqueue_pins_the_exact_pending_step_and_starts_waiting_with_the_first_check_pending()
    {
        var admission = Waiting();

        Assert.Equal(LocalExecutionAdmissionState.Waiting, admission.State);
        Assert.Equal(LocalExecutionWaitingReason.AwaitingAdmissionCheck, admission.WaitingReason);
        Assert.Equal(Now, admission.WaitingReasonRecordedAtUtc);
        Assert.Null(admission.StepId);
        Assert.Equal(1, admission.Revision);
        Assert.False(admission.IsHeld);
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation, 1, 0, null, false, false, false)]
    [InlineData(LocalExecutionStepKind.Implementation, 0, 1, null, false, false, false)]
    [InlineData(LocalExecutionStepKind.Verification, 1, 1, null, true, true, false)]
    [InlineData(LocalExecutionStepKind.CodeReview, 2, 1, 0, true, true, false)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection, 4, 1, null, true, true, false)]
    [InlineData(LocalExecutionStepKind.CodeReview, 2, 1, null, false, true, false)]
    public void Enqueue_refuses_a_shape_that_does_not_match_its_kind(
        LocalExecutionStepKind kind, int ordinal, int pass, int? recipe, bool predecessor, bool report, bool subject)
    {
        Assert.Throws<ArgumentException>(() => Waiting(
            kind, ordinal, pass, recipe, predecessor ? Guid.NewGuid() : null, report ? Guid.NewGuid() : null, subject ? Guid.NewGuid() : null));
    }

    [Fact]
    public void A_verification_names_its_recipe_position_and_a_correction_names_its_subject()
    {
        var verification = Waiting(LocalExecutionStepKind.Verification, 1, 1, 0, Guid.NewGuid(), Guid.NewGuid());
        var correction = Waiting(LocalExecutionStepKind.ReviewCorrection, 4, 1, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(0, verification.RecipeOrdinal);
        Assert.NotNull(correction.SubjectAttemptId);
    }

    [Fact]
    public void An_order_number_must_be_positive_and_a_fingerprint_a_lowercase_digest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Waiting(number: 0));
        Assert.Throws<ArgumentException>(() => LocalExecutionAdmission.Enqueue(
            Guid.NewGuid(), Guid.NewGuid(), 1, 0, LocalExecutionStepKind.Implementation, 0, null, null, Guid.NewGuid(), "ABC", null, null, Now));
    }

    [Fact]
    public void A_repeated_waiting_reason_records_nothing_and_a_change_advances_the_revision_and_time_once()
    {
        var admission = Waiting();
        var later = Now.AddMinutes(1);

        Assert.False(admission.RecordWaitingReason(LocalExecutionWaitingReason.AwaitingAdmissionCheck, later));
        Assert.Equal(1, admission.Revision);
        Assert.True(admission.RecordWaitingReason(LocalExecutionWaitingReason.WaitingForWorkspace, later));
        Assert.Equal(2, admission.Revision);
        Assert.Equal(later, admission.WaitingReasonRecordedAtUtc);
        Assert.False(admission.RecordWaitingReason(LocalExecutionWaitingReason.WaitingForWorkspace, later.AddMinutes(5)));
        Assert.Equal(later, admission.WaitingReasonRecordedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => admission.RecordWaitingReason("made_up", later));
    }

    [Fact]
    public void A_waiting_admission_is_reserved_once_consumed_once_and_a_consumed_one_is_final()
    {
        var admission = Waiting();
        var step = Guid.NewGuid();

        admission.Reserve(step, Now);

        Assert.Equal(LocalExecutionAdmissionState.Reserved, admission.State);
        Assert.Equal(step, admission.StepId);
        Assert.True(admission.IsHeld);
        Assert.Throws<InvalidOperationException>(() => admission.Reserve(Guid.NewGuid(), Now));
        Assert.Throws<InvalidOperationException>(() => admission.RecordWaitingReason(LocalExecutionWaitingReason.WaitingForWorkspace, Now));
        admission.Consume(Now.AddSeconds(1));
        Assert.Equal(LocalExecutionAdmissionState.Consumed, admission.State);
        Assert.False(admission.IsHeld);
        Assert.Throws<InvalidOperationException>(() => admission.Consume(Now));
        Assert.Throws<InvalidOperationException>(() => admission.Retire(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => admission.MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now));
    }

    [Fact]
    public void A_waiting_or_reserved_admission_can_be_retired_with_a_fixed_reason_and_a_retired_one_is_final()
    {
        var waiting = Waiting();
        waiting.Retire(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
        Assert.Equal(LocalExecutionAdmissionState.Retired, waiting.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, waiting.ReasonCode);
        Assert.Throws<InvalidOperationException>(() => waiting.Reserve(Guid.NewGuid(), Now));

        var reserved = Waiting();
        reserved.Reserve(Guid.NewGuid(), Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => reserved.Retire("made_up", Now));
        reserved.Retire(LocalExecutionSequenceReason.PreparationExpired, Now);
        Assert.Equal(LocalExecutionAdmissionState.Retired, reserved.State);
        Assert.False(reserved.IsHeld);
    }

    [Fact]
    public void An_unproved_reservation_becomes_unresolved_stays_held_and_is_never_retired_or_consumed_afterwards()
    {
        var admission = Waiting();
        admission.Reserve(Guid.NewGuid(), Now);

        admission.MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now);

        Assert.Equal(LocalExecutionAdmissionState.Unresolved, admission.State);
        Assert.True(admission.IsHeld);
        Assert.Throws<InvalidOperationException>(() => admission.Retire(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => admission.Consume(Now));
        Assert.Throws<InvalidOperationException>(() => Waiting().MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now));
    }

    [Fact]
    public void The_waiting_reason_vocabulary_is_the_closed_durable_text()
    {
        Assert.All(
            new[]
            {
                "AwaitingAdmissionCheck", "WaitingForWorkspace", "WaitingForCapacity", "WaitingForEarlierEligibleWork",
                "WaitingForPreparationCapacity", "Unavailable",
            },
            reason => Assert.True(LocalExecutionWaitingReason.IsKnown(reason)));
        Assert.False(LocalExecutionWaitingReason.IsKnown("waitingforworkspace"));
        Assert.False(LocalExecutionWaitingReason.IsKnown(null));
        Assert.Equal(["Waiting", "Reserved", "Consumed", "Retired", "Unresolved"], Enum.GetNames<LocalExecutionAdmissionState>());
        Assert.Equal(["Immediate", "WaitForAdmission"], Enum.GetNames<LocalExecutionAdmissionMode>());
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation, true)]
    [InlineData(LocalExecutionStepKind.Verification, true)]
    [InlineData(LocalExecutionStepKind.CodeReview, false)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis, false)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection, true)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection, true)]
    public void The_effect_of_a_step_comes_from_the_closed_step_and_contract_mapping(LocalExecutionStepKind kind, bool mutating)
    {
        Assert.Equal(mutating, LocalExecutionSequencePolicy.IsPotentiallyMutating(kind));
    }

    [Fact]
    public void An_undefined_step_kind_is_never_mutating_by_accident_and_never_throws()
    {
        Assert.False(LocalExecutionSequencePolicy.IsPotentiallyMutating((LocalExecutionStepKind)99));
    }

    [Fact]
    public void The_order_counter_hands_out_strictly_increasing_numbers_and_never_wraps()
    {
        var counter = (LocalExecutionOrderCounter)Activator.CreateInstance(typeof(LocalExecutionOrderCounter), nonPublic: true)!;
        SetField(counter, "<Id>k__BackingField", LocalExecutionOrderCounter.SingletonId);

        Assert.Equal(1, counter.Allocate());
        Assert.Equal(2, counter.Allocate());
        Assert.Equal(2, counter.Revision);

        SetField(counter, "<LastNumber>k__BackingField", long.MaxValue);
        Assert.Throws<InvalidOperationException>(() => counter.Allocate());
        SetField(counter, "<LastNumber>k__BackingField", -1L);
        Assert.Throws<InvalidOperationException>(() => counter.Allocate());
        SetField(counter, "<LastNumber>k__BackingField", 5L);
        SetField(counter, "<Id>k__BackingField", 2);
        Assert.Throws<InvalidOperationException>(() => counter.Allocate());
    }

    private static void SetField(object target, string name, object value) =>
        typeof(LocalExecutionOrderCounter).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);

    [Fact]
    public void A_queued_sequence_starts_waiting_and_advances_only_through_the_queue_transitions()
    {
        var sequence = LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "settings", 1, 2, Now, 5, LocalExecutionAdmissionMode.WaitForAdmission);

        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, sequence.State);
        Assert.Equal(LocalExecutionAdmissionMode.WaitForAdmission, sequence.AdmissionMode);
        Assert.Equal(5, sequence.CoordinationNumber);
        Assert.True(sequence.IsAuthorityLive);
        Assert.Equal(Now.AddHours(2), sequence.AdvancementDeadlineUtc);

        sequence.BeginPreparing(Now.AddSeconds(1));
        Assert.Equal(LocalExecutionSequenceState.PreparingStep, sequence.State);
        sequence.MarkAwaitingResult(Now.AddSeconds(2));
        sequence.BeginWaiting(Now.AddSeconds(3));
        Assert.Equal(LocalExecutionSequenceState.WaitingForAdmission, sequence.State);
        Assert.Equal(Now.AddHours(2), sequence.AdvancementDeadlineUtc);

        sequence.End(LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, null, Now.AddSeconds(4));
        Assert.Equal(LocalExecutionSequenceState.Stopped, sequence.State);
    }

    [Fact]
    public void An_immediate_sequence_never_waits_and_a_queued_one_is_never_armed_or_given_an_invalid_order()
    {
        var immediate = LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "settings", 1, 2, Now, 1);
        Assert.Equal(LocalExecutionSequenceState.Armed, immediate.State);
        Assert.Equal(LocalExecutionAdmissionMode.Immediate, immediate.AdmissionMode);
        immediate.BeginPreparing(Now);
        immediate.MarkAwaitingResult(Now);
        Assert.Throws<InvalidOperationException>(() => immediate.BeginWaiting(Now));

        Assert.Throws<ArgumentOutOfRangeException>(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "settings", 1, 2, Now, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "settings", 1, 2, Now, 1, (LocalExecutionAdmissionMode)7));

        var queued = LocalExecutionSequence.Arm(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha, Guid.NewGuid(),
            LocalExecutionPlanAuthority.AcceptedRoot, Sha, Sha, "settings", 1, 2, Now, 2, LocalExecutionAdmissionMode.WaitForAdmission);
        Assert.Throws<InvalidOperationException>(() => queued.BeginWaiting(Now));
        Assert.Throws<InvalidOperationException>(() => queued.MarkAwaitingResult(Now));
        Assert.Throws<InvalidOperationException>(() => queued.BeginStopping(Now));
    }

    [Fact]
    public void The_queue_bounds_are_the_adr_values_and_the_state_vocabulary_keeps_its_durable_values()
    {
        Assert.Equal(64, LocalExecutionSequencePolicy.MaximumQueuedCycles);
        Assert.Equal(4, LocalExecutionSequencePolicy.MaximumHeldPreparations);
        Assert.Equal(32, LocalExecutionSequencePolicy.ObservationPageSize);
        Assert.Equal(9, (int)LocalExecutionSequenceState.WaitingForAdmission);
        Assert.True(LocalExecutionSequencePolicy.IsAuthorityLive(LocalExecutionSequenceState.WaitingForAdmission));
        Assert.False(LocalExecutionSequencePolicy.IsAdvancing(LocalExecutionSequenceState.WaitingForAdmission));
        Assert.False(LocalExecutionSequencePolicy.IsEnded(LocalExecutionSequenceState.WaitingForAdmission));
    }
}
