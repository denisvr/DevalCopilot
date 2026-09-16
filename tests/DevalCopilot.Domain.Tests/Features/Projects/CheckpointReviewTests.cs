using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class CheckpointReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Approved_review_requires_passed_execution_on_the_same_fingerprint()
    {
        var review = CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, Guid.NewGuid(), 1, Fingerprint,
            VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0,
            ReviewActorKind.Human, ReviewDecision.Approved, Now);

        Assert.Equal(ReviewDecision.Approved, review.Decision);
        Assert.Equal(Fingerprint, review.CheckpointFingerprintSha256);
    }

    [Fact]
    public void Review_rejects_running_or_mismatched_execution_evidence()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, Guid.NewGuid(), 1, new string('c', 64),
            VerificationExecutionStatus.Running, VerificationExecutionOutcome.Exited, 0,
            ReviewActorKind.FutureAgent, ReviewDecision.ChangesRequested, Now));
    }

    [Fact]
    public void All_terminal_execution_states_are_valid_for_non_approval_decisions()
    {
        var evidence = new[]
        {
            (VerificationExecutionStatus.Interrupted, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, (VerificationExecutionOutcome?)null, (int?)null),
            (VerificationExecutionStatus.SourceChanged, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, (int?)0),
            (VerificationExecutionStatus.Failed, VerificationExecutionOutcome.Exited, (int?)1),
            (VerificationExecutionStatus.TimedOut, VerificationExecutionOutcome.TimedOut, (int?)null),
            (VerificationExecutionStatus.Cancelled, VerificationExecutionOutcome.Cancelled, (int?)null),
        };

        foreach (var (status, outcome, exitCode) in evidence)
        {
            var review = CheckpointReview.Record(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                Fingerprint, Guid.NewGuid(), 1, Fingerprint,
                status, outcome, exitCode,
                ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now);

            Assert.Equal(status, review.VerificationExecutionStatus);
        }
    }

    [Fact]
    public void Interrupted_evidence_rejects_a_process_outcome()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, Guid.NewGuid(), 1, Fingerprint,
            VerificationExecutionStatus.Interrupted, VerificationExecutionOutcome.Cancelled, null,
            ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now));
    }

    [Fact]
    public void Undefined_outcome_and_impossible_status_outcome_pairs_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateEvidence(
            VerificationExecutionStatus.Failed, (VerificationExecutionOutcome)99, null));

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
            Assert.Throws<ArgumentException>(() => CreateEvidence(status, outcome, exitCode));
        }
    }

    [Fact]
    public void Undefined_actor_and_decision_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, null, null, null, null, null, null,
            (ReviewActorKind)99, ReviewDecision.Pending, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, null, null, null, null, null, null,
            ReviewActorKind.Human, (ReviewDecision)99, Now));
    }

    private static CheckpointReview CreateEvidence(
        VerificationExecutionStatus status, VerificationExecutionOutcome outcome, int? exitCode) =>
        CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, Guid.NewGuid(), 1, Fingerprint,
            status, outcome, exitCode,
            ReviewActorKind.Human, ReviewDecision.ChangesRequested, Now);

    [Fact]
    public void Pending_review_is_a_recorded_state_without_execution_evidence()
    {
        var review = CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, null, null, null, null, null, null,
            ReviewActorKind.FutureAgent, ReviewDecision.Pending, Now);

        Assert.Null(review.VerificationExecutionId);
        Assert.Null(review.VerificationExecutionStatus);
        Assert.Equal(ReviewDecision.Pending, review.Decision);
    }

    [Fact]
    public void Pending_review_rejects_execution_evidence()
    {
        Assert.Throws<ArgumentException>(() => CheckpointReview.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Fingerprint, Guid.NewGuid(), 1, Fingerprint,
            VerificationExecutionStatus.Passed, VerificationExecutionOutcome.Exited, 0,
            ReviewActorKind.Human, ReviewDecision.Pending, Now));
    }

    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
