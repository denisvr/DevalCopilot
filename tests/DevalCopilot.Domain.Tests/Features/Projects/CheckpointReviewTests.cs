using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class CheckpointReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static CheckpointReviewEvidence CreateMember(
        Guid reviewId,
        VerificationExecutionStatus status,
        VerificationExecutionOutcome? outcome,
        int? exitCode,
        string? fingerprint = null,
        Guid? verificationCommandId = null,
        Guid? verificationExecutionId = null) =>
        CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), reviewId, verificationCommandId ?? Guid.NewGuid(), verificationExecutionId ?? Guid.NewGuid(), 1,
            fingerprint ?? Fingerprint, status, outcome, exitCode);

    [Fact]
    public void Approved_review_requires_every_evidence_member_to_have_passed()
    {
        var reviewId = Guid.NewGuid();
        var member = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);

        var review = CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.Human, ReviewDecision.Approved, Now, [member]);

        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Equal(Fingerprint, review.CheckpointFingerprintSha256);
        Assert.Single(review.Evidence);
    }

    [Fact]
    public void Approved_review_rejects_a_set_where_any_member_did_not_pass()
    {
        var reviewId = Guid.NewGuid();
        var passed = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var failed = CreateMember(reviewId, VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, 1);

        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.Approved, Now, [passed, failed]));
    }

    [Fact]
    public void A_decided_review_accepts_several_distinct_evidence_members_one_per_command()
    {
        var reviewId = Guid.NewGuid();
        var first = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);
        var second = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);

        var review = CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.Approved, Now, [first, second]);

        Assert.Equal(2, review.Evidence.Count);
    }

    [Fact]
    public void A_decided_review_rejects_the_same_verification_command_contributing_twice()
    {
        var reviewId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var first = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, verificationCommandId: commandId);
        var second = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, verificationCommandId: commandId);

        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.Approved, Now, [first, second]));
    }

    [Fact]
    public void A_decided_review_rejects_the_same_verification_execution_claimed_twice()
    {
        var reviewId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var first = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, verificationExecutionId: executionId);
        var second = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, verificationExecutionId: executionId);

        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.ChangesRequested, Now, [first, second]));
    }

    [Fact]
    public void A_review_rejects_an_evidence_member_on_a_different_checkpoint_fingerprint()
    {
        var reviewId = Guid.NewGuid();
        var member = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0, fingerprint: new string('c', 64));

        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.ChangesRequested, Now, [member]));
    }

    [Fact]
    public void Review_rejects_running_execution_evidence()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, VerificationExecutionStatus.Running, VerificationExecutionOutcome.Exited, 0));
    }

    [Fact]
    public void All_terminal_execution_states_are_valid_evidence_for_non_approval_decisions()
    {
        var evidenceCases = new[]
        {
            (VerificationExecutionStatus.Interrupted, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, (int?)1),
            (VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.TimedOut, (int?)null),
            (VerificationExecutionStatus.Cancelled, VerificationExecutionOutcome.Cancelled, (int?)null),
        };

        foreach (var (status, outcome, exitCode) in evidenceCases)
        {
            var reviewId = Guid.NewGuid();
            var member = CreateMember(reviewId, status, outcome, exitCode);
            var review = CheckpointReview.Record(
                reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                Fingerprint, ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now, [member]);

            Assert.Equal(status, Assert.Single(review.Evidence).VerificationExecutionStatus);
        }
    }

    [Fact]
    public void Interrupted_evidence_rejects_a_process_outcome()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, VerificationExecutionStatus.Interrupted, VerificationExecutionOutcome.Cancelled, null));
    }

    [Fact]
    public void Undefined_outcome_and_impossible_status_outcome_pairs_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointReviewEvidence.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, VerificationExecutionStatus.Failed, (VerificationExecutionOutcome)99, null));

        var invalidPairs = new[]
        {
            (VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.Exited, (int?)1),
            (VerificationExecutionStatus.Cancelled, VerificationExecutionOutcome.Exited, (int?)1),
            (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.TimedOut, (int?)null),
            (VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, (int?)1),
        };

        foreach (var (status, outcome, exitCode) in invalidPairs)
        {
            Assert.Throws<ArgumentException>(() => CheckpointReviewEvidence.Observe(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                Fingerprint, status, outcome, exitCode));
        }
    }

    [Fact]
    public void Undefined_actor_and_decision_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, (ReviewActorKind)99, ReviewDecision.Pending, Now, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.Human, (ReviewDecision)99, Now, []));
    }

    [Fact]
    public void Pending_review_is_a_recorded_state_without_execution_evidence()
    {
        var review = CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.FutureAgent, ReviewDecision.Pending, Now, []);

        Assert.Empty(review.Evidence);
        Assert.Equal(ReviewDecision.Pending, review.Decision);
    }

    [Fact]
    public void Pending_review_rejects_execution_evidence()
    {
        var reviewId = Guid.NewGuid();
        var member = CreateMember(reviewId, VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0);

        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            reviewId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.Human, ReviewDecision.Pending, Now, [member]));
    }

    [Fact]
    public void A_decided_review_requires_at_least_one_evidence_member()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now, []));
    }

    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
