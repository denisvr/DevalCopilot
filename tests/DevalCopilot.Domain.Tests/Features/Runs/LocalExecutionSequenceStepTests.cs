using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0060: the fixed shape of each step kind, the single reserved operation identity and the legal step transitions.</summary>
public sealed class LocalExecutionSequenceStepTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('a', 64);

    private static LocalExecutionSequenceStep Reserve(
        LocalExecutionStepKind kind,
        int ordinal,
        int passNumber,
        int? recipeOrdinal = null,
        bool predecessor = true,
        bool report = true,
        bool subject = false,
        Guid? operationId = null,
        string? fingerprint = null,
        Guid? sourceCheckpointId = null,
        Guid? ownerToken = null,
        DateTimeOffset? reservedAt = null) => LocalExecutionSequenceStep.Reserve(
        Guid.NewGuid(),
        Guid.NewGuid(),
        ordinal,
        kind,
        passNumber,
        recipeOrdinal,
        predecessor ? Guid.NewGuid() : null,
        sourceCheckpointId ?? Guid.NewGuid(),
        fingerprint ?? Sha,
        report ? Guid.NewGuid() : null,
        subject ? Guid.NewGuid() : null,
        operationId ?? Guid.NewGuid(),
        ownerToken ?? Guid.NewGuid(),
        reservedAt ?? Now);

    private static LocalExecutionSequenceStep Implementation() => Reserve(
        LocalExecutionStepKind.Implementation, 0, 0, predecessor: false, report: false);

    private static LocalExecutionSequenceStep Verification(int recipeOrdinal = 0) => Reserve(
        LocalExecutionStepKind.Verification, 1 + recipeOrdinal, 1, recipeOrdinal);

    private static LocalExecutionSequenceStep Review() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1);

    private static LocalExecutionSequenceStep Correction(LocalExecutionStepKind kind = LocalExecutionStepKind.ReviewCorrection) =>
        Reserve(kind, 4, 1, subject: true);

    [Fact]
    public void Reserving_the_implementation_records_the_exact_shape_in_the_Reserved_state()
    {
        var operation = Guid.NewGuid();
        var checkpoint = Guid.NewGuid();
        var owner = Guid.NewGuid();

        var step = Reserve(
            LocalExecutionStepKind.Implementation, 0, 0, predecessor: false, report: false,
            operationId: operation, sourceCheckpointId: checkpoint, ownerToken: owner);

        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
        Assert.Equal(1, step.Revision);
        Assert.Equal(operation, step.ReservedOperationId);
        Assert.Equal(checkpoint, step.SourceCheckpointId);
        Assert.Equal(owner, step.PreparationOwnerToken);
        Assert.Equal(Now, step.PreparationReservedAtUtc);
        Assert.Equal(Now.AddMinutes(2), step.PreparationDeadlineUtc);
        Assert.Equal(Now, step.UpdatedAtUtc);
        Assert.Null(step.OperationId);
        Assert.Null(step.ClaimedAtUtc);
        Assert.Null(step.ResultDeadlineUtc);
        Assert.Null(step.ReasonCode);
        Assert.Null(step.PredecessorStepId);
        Assert.Null(step.ReportMessageId);
        Assert.Null(step.SubjectAttemptId);
        Assert.Null(step.RecipeOrdinal);
        Assert.True(step.IsAgent);
        Assert.True(step.IsOutstanding);
    }

    [Fact]
    public void A_verification_step_is_not_an_Agent_step_and_needs_its_recipe_ordinal()
    {
        var step = Verification(recipeOrdinal: 4);

        Assert.False(step.IsAgent);
        Assert.Equal(4, step.RecipeOrdinal);
        Assert.NotNull(step.ReportMessageId);
        Assert.NotNull(step.PredecessorStepId);

        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Verification, 1, 1, recipeOrdinal: null));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.CodeReview)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection)]
    public void A_non_verification_step_cannot_carry_a_recipe_ordinal(LocalExecutionStepKind kind)
    {
        Assert.Throws<ArgumentException>(() => Reserve(kind, 3, 1, recipeOrdinal: 0, subject: kind == LocalExecutionStepKind.ReviewCorrection));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    public void A_verification_recipe_ordinal_is_bounded(int recipeOrdinal)
    {
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Verification, 1, 1, recipeOrdinal));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.ReviewCorrection)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection)]
    public void A_correction_needs_its_subject_attempt_and_no_other_kind_may_carry_one(LocalExecutionStepKind kind)
    {
        var correction = Correction(kind);
        Assert.NotNull(correction.SubjectAttemptId);

        Assert.Throws<ArgumentException>(() => Reserve(kind, 4, 1, subject: false));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, subject: true));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Verification, 1, 1, recipeOrdinal: 0, subject: true));
        Assert.Throws<ArgumentException>(() => Reserve(
            LocalExecutionStepKind.Implementation, 0, 0, predecessor: false, report: false, subject: true));
    }

    [Fact]
    public void The_implementation_is_ordinal_zero_pass_zero_without_predecessor_or_report()
    {
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Implementation, 1, 0, predecessor: false, report: false));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Implementation, 0, 1, predecessor: false, report: false));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Implementation, 0, 0, predecessor: true, report: false));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.Implementation, 0, 0, predecessor: false, report: true));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.CodeReview)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection)]
    public void A_later_step_needs_a_predecessor_a_report_and_a_pass_above_zero(LocalExecutionStepKind kind)
    {
        var subject = LocalExecutionSequencePolicy.IsCorrectionKind(kind);

        Assert.Throws<ArgumentException>(() => Reserve(kind, 3, 1, predecessor: false, subject: subject));
        Assert.Throws<ArgumentException>(() => Reserve(kind, 3, 1, report: false, subject: subject));
        Assert.Throws<ArgumentException>(() => Reserve(kind, 3, 0, subject: subject));
        Assert.Throws<ArgumentException>(() => Reserve(kind, 0, 1, subject: subject));
    }

    [Fact]
    public void The_pass_number_is_bounded_by_the_three_passes()
    {
        Assert.Equal(3, Reserve(LocalExecutionStepKind.CodeReview, 20, 3).PassNumber);
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 20, 4));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 20, -1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(102)]
    public void The_ordinal_is_bounded_below_the_hard_maximum(int ordinal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reserve(LocalExecutionStepKind.CodeReview, ordinal, 1));
    }

    [Fact]
    public void The_highest_ordinal_is_one_hundred_and_one()
    {
        Assert.Equal(101, Reserve(LocalExecutionStepKind.CodeReview, 101, 3).Ordinal);
    }

    [Fact]
    public void Reserving_rejects_empty_identifiers_an_undefined_kind_a_bad_fingerprint_and_a_default_time()
    {
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, operationId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, ownerToken: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, sourceCheckpointId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, fingerprint: "ABC"));
        Assert.Throws<ArgumentException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, fingerprint: new string('A', 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reserve((LocalExecutionStepKind)99, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reserve(LocalExecutionStepKind.CodeReview, 3, 1, reservedAt: default(DateTimeOffset)));
    }

    [Fact]
    public void Claiming_links_only_the_reserved_operation_and_moves_to_Claimed()
    {
        var operation = Guid.NewGuid();
        var step = Reserve(LocalExecutionStepKind.CodeReview, 3, 1, operationId: operation);
        var claimedAt = Now.AddSeconds(30);
        var deadline = claimedAt.AddMinutes(12);

        step.MarkClaimed(operation, claimedAt, deadline);

        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        Assert.Equal(operation, step.OperationId);
        Assert.Equal(claimedAt, step.ClaimedAtUtc);
        Assert.Equal(deadline, step.ResultDeadlineUtc);
        Assert.Equal(2, step.Revision);
        Assert.True(step.IsOutstanding);
        Assert.Null(step.ReasonCode);
    }

    [Fact]
    public void Claiming_refuses_any_other_operation_identity_and_leaves_the_step_reserved()
    {
        var step = Review();

        Assert.Throws<ArgumentException>(() => step.MarkClaimed(Guid.NewGuid(), Now, Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => step.MarkClaimed(Guid.Empty, Now, Now.AddMinutes(1)));

        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
        Assert.Null(step.OperationId);
        Assert.Equal(1, step.Revision);
    }

    [Fact]
    public void A_step_is_claimed_once_and_only_from_Reserved()
    {
        var operation = Guid.NewGuid();
        var step = Reserve(LocalExecutionStepKind.CodeReview, 3, 1, operationId: operation);
        step.MarkClaimed(operation, Now, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => step.MarkClaimed(operation, Now, Now.AddMinutes(1)));

        var revoked = Review();
        revoked.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now);
        Assert.Throws<InvalidOperationException>(() => revoked.MarkClaimed(revoked.ReservedOperationId, Now, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_claimed_step_concludes_once_and_stops_being_outstanding()
    {
        var operation = Guid.NewGuid();
        var step = Reserve(LocalExecutionStepKind.CodeReview, 3, 1, operationId: operation);
        step.MarkClaimed(operation, Now, Now.AddMinutes(1));

        step.Conclude(Now.AddMinutes(2));

        Assert.Equal(LocalExecutionSequenceStepState.Concluded, step.State);
        Assert.Equal(3, step.Revision);
        Assert.Equal(Now.AddMinutes(2), step.UpdatedAtUtc);
        Assert.False(step.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => step.Conclude(Now));
        Assert.Throws<InvalidOperationException>(() => step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => step.MarkUnresolved(LocalExecutionSequenceReason.PreparationFailed, Now));
    }

    [Fact]
    public void A_reserved_step_cannot_be_concluded()
    {
        Assert.Throws<InvalidOperationException>(() => Review().Conclude(Now));
    }

    [Fact]
    public void A_reserved_step_can_be_revoked_with_a_fixed_reason_and_stops_being_outstanding()
    {
        var step = Review();

        step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now.AddMinutes(1));

        Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State);
        Assert.Equal(LocalExecutionSequenceReason.StoppedBeforeClaim, step.ReasonCode);
        Assert.Equal(2, step.Revision);
        Assert.False(step.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
    }

    [Fact]
    public void A_claimed_step_cannot_be_revoked_or_marked_unresolved()
    {
        var operation = Guid.NewGuid();
        var step = Reserve(LocalExecutionStepKind.CodeReview, 3, 1, operationId: operation);
        step.MarkClaimed(operation, Now, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => step.MarkUnresolved(LocalExecutionSequenceReason.PreparationFailed, Now));
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
    }

    [Fact]
    public void A_reserved_step_can_become_unresolved_and_that_keeps_the_step_outstanding_for_nothing_but_is_final()
    {
        var step = Review();

        step.MarkUnresolved(LocalExecutionSequenceReason.PreparationUnresolved, Now.AddMinutes(1));

        Assert.Equal(LocalExecutionSequenceStepState.Unresolved, step.State);
        Assert.Equal(LocalExecutionSequenceReason.PreparationUnresolved, step.ReasonCode);
        Assert.False(step.IsOutstanding);
        Assert.Throws<InvalidOperationException>(() => step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, Now));
        Assert.Throws<InvalidOperationException>(() => step.MarkUnresolved(LocalExecutionSequenceReason.PreparationFailed, Now));
        Assert.Throws<InvalidOperationException>(() => step.MarkClaimed(step.ReservedOperationId, Now, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_transition_rejects_an_unknown_reason_and_a_default_time_without_changing_the_step()
    {
        var step = Review();

        Assert.Throws<ArgumentOutOfRangeException>(() => step.Revoke("made_up", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => step.MarkUnresolved("made_up", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => step.Revoke(LocalExecutionSequenceReason.StoppedBeforeClaim, default));

        Assert.Equal(LocalExecutionSequenceStepState.Reserved, step.State);
        Assert.Equal(1, step.Revision);
        Assert.Null(step.ReasonCode);
    }
}
