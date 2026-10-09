using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0029 recorded these digests at admission and ADR-0032 recomputes them from historical rows. The expected values are literal
/// SHA-256 results of the original serialization, so any change of the format, the separators, the argument joiner or the ordering
/// would invalidate every recorded operation and fails here.
/// </summary>
public sealed class LocalCommitMemberDigestsTests
{
    private static readonly string Fingerprint = new('b', 64);

    private static Guid Id(char digit) => Guid.Parse(new string(digit, 32));

    [Fact]
    public void The_verification_digest_keeps_the_recorded_serialization()
    {
        var digest = LocalCommitMemberDigests.Verification(
            Id('1'), Id('2'), 7, "Backend tests", @"C:\dotnet.exe", 300, ["test", "--no-build"], new string('a', 64));

        Assert.Equal("9491da5a3c7021daa7647d6d62f183a51c782324d6f5179bc1aee92e3e51dff8", digest);
    }

    [Fact]
    public void The_verification_digest_binds_every_snapshot_field_and_the_argument_order()
    {
        string Compute(
            string name = "Backend tests", string executable = @"C:\dotnet.exe", int timeout = 300, int number = 7,
            string[]? arguments = null, string? completion = null) => LocalCommitMemberDigests.Verification(
            Id('1'), Id('2'), number, name, executable, timeout, arguments ?? ["test", "--no-build"], completion ?? new string('a', 64));

        var baseline = Compute();

        Assert.NotEqual(baseline, Compute(name: "Other"));
        Assert.NotEqual(baseline, Compute(executable: @"C:\other.exe"));
        Assert.NotEqual(baseline, Compute(timeout: 301));
        Assert.NotEqual(baseline, Compute(number: 8));
        Assert.NotEqual(baseline, Compute(arguments: ["--no-build", "test"]));
        Assert.NotEqual(baseline, Compute(arguments: ["test", "--no-build", "x"]));
        Assert.NotEqual(baseline, Compute(completion: new string('c', 64)));
        Assert.NotEqual(baseline, LocalCommitMemberDigests.Verification(
            Id('9'), Id('2'), 7, "Backend tests", @"C:\dotnet.exe", 300, ["test", "--no-build"], new string('a', 64)));
        Assert.NotEqual(baseline, LocalCommitMemberDigests.Verification(
            Id('1'), Id('9'), 7, "Backend tests", @"C:\dotnet.exe", 300, ["test", "--no-build"], new string('a', 64)));
    }

    [Fact]
    public void The_human_decision_digest_keeps_the_recorded_serialization_and_orders_evidence_by_execution()
    {
        var evidence = new[]
        {
            Observation(Id('5'), Id('1')),
            Observation(Id('6'), Id('2')),
        };
        var review = Review(evidence);

        var expected = "e50f38255fcc4e56d87258870c81914f36653becddcac900c34aa36ed4d18a09";
        Assert.Equal(expected, LocalCommitMemberDigests.HumanDecision(review, evidence));
        Assert.Equal(expected, LocalCommitMemberDigests.HumanDecision(review, evidence.Reverse()));
    }

    [Fact]
    public void The_human_decision_digest_binds_the_decision_actor_checkpoint_and_evidence()
    {
        var evidence = new[] { Observation(Id('5'), Id('1')), Observation(Id('6'), Id('2')) };
        var baseline = LocalCommitMemberDigests.HumanDecision(Review(evidence), evidence);

        Assert.NotEqual(baseline, LocalCommitMemberDigests.HumanDecision(Review(evidence, ReviewActorKind.FutureAgent), evidence));
        Assert.NotEqual(baseline, LocalCommitMemberDigests.HumanDecision(Review(evidence, checkpointId: Id('9')), evidence));
        Assert.NotEqual(baseline, LocalCommitMemberDigests.HumanDecision(Review(evidence), evidence.Take(1)));
        Assert.NotEqual(
            baseline,
            LocalCommitMemberDigests.HumanDecision(Review(evidence), [Observation(Id('5'), Id('1')), Observation(Id('6'), Id('3'))]));
    }

    private static CheckpointReviewEvidence Observation(Guid commandId, Guid executionId) => CheckpointReviewEvidence.Observe(
        Guid.NewGuid(), Id('3'), commandId, executionId, 1, Fingerprint, VerificationExecutionStatus.Passed,
        VerificationExecutionOutcome.Exited, 0);

    private static CheckpointReview Review(
        CheckpointReviewEvidence[] evidence, ReviewActorKind actor = ReviewActorKind.Human, Guid? checkpointId = null) =>
        CheckpointReview.Record(
            Id('3'), Id('7'), Id('8'), checkpointId ?? Id('4'), 2, Fingerprint, actor, ReviewDecision.Approved,
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), evidence);
}
