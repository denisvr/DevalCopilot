using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentProcessExecutionEvidenceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-1";

    public static TheoryData<ProcessOutcome, int?> ValidShapes => new()
    {
        { ProcessOutcome.Exited, 0 },
        { ProcessOutcome.Exited, 1 },
        { ProcessOutcome.Exited, -1073741510 },
        { ProcessOutcome.TimedOut, null },
        { ProcessOutcome.Cancelled, null },
    };

    [Theory]
    [MemberData(nameof(ValidShapes))]
    public void Create_accepts_every_valid_shape(ProcessOutcome outcome, int? exitCode)
    {
        var evidence = AgentProcessExecutionEvidence.Create(outcome, exitCode, TimeSpan.FromMilliseconds(42));

        Assert.Equal(outcome, evidence.Outcome);
        Assert.Equal(exitCode, evidence.ExitCode);
        Assert.Equal(TimeSpan.FromMilliseconds(42), evidence.Duration);
        Assert.Equal(outcome == ProcessOutcome.Exited && exitCode == 0, evidence.IsCleanExit);
    }

    [Fact]
    public void Create_accepts_a_zero_duration()
    {
        var evidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, evidence.Duration);
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, null, 10, AgentProcessEvidenceViolation.ExitedWithoutExitCode)]
    [InlineData(ProcessOutcome.TimedOut, 1, 10, AgentProcessEvidenceViolation.ExitCodeWithoutExited)]
    [InlineData(ProcessOutcome.Cancelled, 0, 10, AgentProcessEvidenceViolation.ExitCodeWithoutExited)]
    [InlineData(ProcessOutcome.Exited, 0, -1, AgentProcessEvidenceViolation.NegativeDuration)]
    [InlineData((ProcessOutcome)99, null, 10, AgentProcessEvidenceViolation.UndefinedOutcome)]
    public void Validate_and_Create_reject_every_invalid_shape(
        ProcessOutcome outcome, int? exitCode, long durationMilliseconds, AgentProcessEvidenceViolation expected)
    {
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);

        Assert.Equal(expected, AgentProcessExecutionEvidence.Validate(outcome, exitCode, duration));
        Assert.Throws<ArgumentException>(() => AgentProcessExecutionEvidence.Create(outcome, exitCode, duration));
    }

    [Theory]
    [InlineData(AgentOutcome.Proposed)]
    [InlineData(AgentOutcome.Accepted)]
    [InlineData(AgentOutcome.Challenged)]
    [InlineData(AgentOutcome.Resolved)]
    [InlineData(AgentOutcome.Implemented)]
    [InlineData(AgentOutcome.ReviewApproved)]
    [InlineData(AgentOutcome.ReviewChangesRequested)]
    [InlineData(AgentOutcome.CorrectionApplied)]
    [InlineData(AgentOutcome.InvalidStructuredOutput)]
    // The process genuinely ran to completion and exited zero for each of these four
    // classifications too — Classify() in RecordImplementationResultCommandHandler and
    // RecordReviewCorrectionResultCommandHandler only ever reaches them once ProcessSucceeded is
    // already known to be true — so each requires the same clean-exit evidence as a real success,
    // even though the attempt itself completes AttemptStatus.Failed for three of them.
    [InlineData(AgentOutcome.NoChangesProduced)]
    [InlineData(AgentOutcome.ImplementationHeadChanged)]
    [InlineData(AgentOutcome.CorrectionNoChangesProduced)]
    [InlineData(AgentOutcome.CorrectionHeadChanged)]
    public void Policy_requires_a_clean_exit_for_every_outcome_that_can_only_follow_a_successful_process_exit(AgentOutcome outcome)
    {
        Assert.True(AgentProcessEvidencePolicy.RequiresCleanExit(outcome));
        Assert.Equal(AgentProcessEvidenceViolation.CleanExitRequired, AgentProcessEvidencePolicy.Evaluate(outcome, true, null));
        Assert.Equal(
            AgentProcessEvidenceViolation.CleanExitRequired,
            AgentProcessEvidencePolicy.Evaluate(outcome, true, AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 2, TimeSpan.Zero)));
        Assert.Equal(
            AgentProcessEvidenceViolation.CleanExitRequired,
            AgentProcessEvidencePolicy.Evaluate(outcome, true, AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.Zero)));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, true, TestProcessEvidence.CleanExit));
    }

    [Theory]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.CheckpointEvidenceUnavailable)]
    public void Policy_allows_absent_or_truthful_failure_evidence_for_outcomes_that_can_follow_a_real_invocation(AgentOutcome outcome)
    {
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(outcome));
        Assert.False(AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, false, null));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(
            outcome, true, AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(3))));
    }

    // Defect-2 regression: WorkspaceNoLongerEligible and every "input already handled" race
    // outcome are always detected before the provider is ever invoked — no child process can
    // exist for them, so none may ever carry process evidence, even if the attempt's own dispatch
    // marker were somehow already set. This must fail under the pre-correction policy, which only
    // rejected evidence when dispatched was false.
    [Theory]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    [InlineData(AgentOutcome.InputAlreadyReviewed)]
    [InlineData(AgentOutcome.InputAlreadyResolved)]
    [InlineData(AgentOutcome.InputAlreadyImplemented)]
    [InlineData(AgentOutcome.InputAlreadyCodeReviewed)]
    [InlineData(AgentOutcome.InputAlreadyCorrected)]
    public void Policy_rejects_evidence_for_every_pre_invocation_outcome_regardless_of_dispatch_state(AgentOutcome outcome)
    {
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome));
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(outcome));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(outcome, false, null));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(outcome, true, TestProcessEvidence.CleanExit));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(
                outcome, false, AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Policy_rejects_evidence_for_an_undispatched_attempt_that_is_not_a_pre_invocation_outcome()
    {
        Assert.Equal(
            AgentProcessEvidenceViolation.NotDispatched,
            AgentProcessEvidencePolicy.Evaluate(AgentOutcome.ProviderInvocationFailed, false, TestProcessEvidence.CleanExit));
    }

    [Fact]
    public void CompleteAgent_records_clean_exit_evidence_atomically_with_a_success_outcome()
    {
        var attempt = ClaimDispatchedPlanner();

        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, BaseTime.AddSeconds(2), TestProcessEvidence.CleanExit);

        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(AgentOutcome.Proposed, attempt.AgentOutcome);
        Assert.Equal(ProcessOutcome.Exited, attempt.AgentProcessOutcome);
        Assert.Equal(0, attempt.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), attempt.AgentProcessDuration);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteAgent_rejects_a_success_without_evidence_and_leaves_the_attempt_untouched()
    {
        var attempt = ClaimDispatchedPlanner();

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, BaseTime.AddSeconds(2)));

        AssertUntouched(attempt);
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 1)]
    [InlineData(ProcessOutcome.TimedOut, null)]
    [InlineData(ProcessOutcome.Cancelled, null)]
    public void CompleteAgent_rejects_a_success_or_invalid_output_against_a_non_clean_process_result(ProcessOutcome outcome, int? exitCode)
    {
        var evidence = AgentProcessExecutionEvidence.Create(outcome, exitCode, TimeSpan.FromSeconds(1));
        var proposed = ClaimDispatchedPlanner();
        var invalid = ClaimDispatchedPlanner();

        Assert.Throws<InvalidOperationException>(() => proposed.CompleteAgent(AgentOutcome.Proposed, Fingerprint, BaseTime, evidence));
        Assert.Throws<InvalidOperationException>(
            () => invalid.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, BaseTime, evidence));

        AssertUntouched(proposed);
        AssertUntouched(invalid);
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 7)]
    [InlineData(ProcessOutcome.TimedOut, null)]
    [InlineData(ProcessOutcome.Cancelled, null)]
    public void CompleteAgent_records_non_zero_timeout_and_cancellation_evidence_for_a_provider_failure(ProcessOutcome outcome, int? exitCode)
    {
        var attempt = ClaimDispatchedPlanner();
        var evidence = AgentProcessExecutionEvidence.Create(outcome, exitCode, TimeSpan.FromSeconds(600));

        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, Fingerprint, BaseTime.AddSeconds(2), evidence);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Equal(outcome, attempt.AgentProcessOutcome);
        Assert.Equal(exitCode, attempt.AgentProcessExitCode);
        Assert.Equal(TimeSpan.FromSeconds(600), attempt.AgentProcessDuration);
    }

    [Fact]
    public void CompleteAgent_never_fabricates_evidence_when_no_process_result_exists()
    {
        var attempt = ClaimDispatchedPlanner();

        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime.AddSeconds(2));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentProcessExitCode);
        Assert.Null(attempt.AgentProcessDuration);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteAgent_keeps_clean_exit_evidence_when_source_drift_overrides_the_semantic_outcome()
    {
        var attempt = ClaimDispatchedPlanner();

        attempt.CompleteAgent(AgentOutcome.Proposed, "fingerprint-drifted", BaseTime.AddSeconds(2), TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteAgent_rejects_evidence_for_a_pre_dispatch_classification()
    {
        var attempt = ClaimPlanner();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.WorkspaceNoLongerEligible, null, BaseTime, TestProcessEvidence.CleanExit));

        AssertUntouched(attempt);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public void Pre_dispatch_classifications_complete_without_any_evidence()
    {
        var attempt = ClaimPlanner();

        attempt.CompleteAgent(AgentOutcome.SourceChanged, null, BaseTime);

        Assert.Equal(AgentOutcome.SourceChanged, attempt.AgentOutcome);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void Evidence_is_write_once_because_a_terminal_attempt_cannot_be_completed_again()
    {
        var attempt = ClaimDispatchedPlanner();
        var first = AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromSeconds(30));
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime.AddSeconds(2), first);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, BaseTime.AddSeconds(3), TestProcessEvidence.CleanExit));

        Assert.Equal(first, attempt.GetAgentProcessExecutionEvidence());
        Assert.Equal(BaseTime.AddSeconds(2), attempt.CompletedAtUtc);
    }

    [Fact]
    public void CompleteImplementation_requires_clean_exit_evidence_for_Implemented_and_records_it()
    {
        var withoutEvidence = ClaimDispatchedImplementation();
        Assert.Throws<InvalidOperationException>(
            () => withoutEvidence.CompleteImplementation(AgentOutcome.Implemented, Guid.NewGuid(), BaseTime));
        AssertUntouched(withoutEvidence);
        Assert.Null(withoutEvidence.AgentResultGitCheckpointId);

        var attempt = ClaimDispatchedImplementation();
        var resultCheckpoint = Guid.NewGuid();
        attempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint, BaseTime, TestProcessEvidence.CleanExit);

        Assert.Equal(AgentOutcome.Implemented, attempt.AgentOutcome);
        Assert.Equal(resultCheckpoint, attempt.AgentResultGitCheckpointId);
        Assert.Equal(TestProcessEvidence.CleanExit, attempt.GetAgentProcessExecutionEvidence());
    }

    // Defect-1 regression: NoChangesProduced and ImplementationHeadChanged are only ever
    // classified once the process is already known to have exited cleanly, so the completion
    // transition must require the same clean-exit evidence a genuine Implemented outcome does —
    // this must fail under the pre-correction policy, which derived its required set only from
    // AttemptStatus.Completed outcomes and missed both of these AttemptStatus.Failed ones.
    [Theory]
    [InlineData(AgentOutcome.NoChangesProduced)]
    [InlineData(AgentOutcome.ImplementationHeadChanged)]
    public void CompleteImplementation_requires_clean_exit_evidence_for_a_process_succeeded_failure_classification(AgentOutcome outcome)
    {
        var withoutEvidence = ClaimDispatchedImplementation();
        Assert.Throws<InvalidOperationException>(() => withoutEvidence.CompleteImplementation(outcome, null, BaseTime));
        AssertUntouched(withoutEvidence);

        var withNonCleanEvidence = ClaimDispatchedImplementation();
        var nonClean = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(() => withNonCleanEvidence.CompleteImplementation(outcome, null, BaseTime, nonClean));
        AssertUntouched(withNonCleanEvidence);

        var withCleanEvidence = ClaimDispatchedImplementation();
        withCleanEvidence.CompleteImplementation(outcome, null, BaseTime, TestProcessEvidence.CleanExit);
        Assert.Equal(outcome, withCleanEvidence.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, withCleanEvidence.GetAgentProcessExecutionEvidence());
    }

    // Mirrors CompleteImplementation_requires_clean_exit_evidence_for_a_process_succeeded_failure_classification
    // one contract further down: CorrectionNoChangesProduced and CorrectionHeadChanged are the
    // ReviewCorrection contract's own "process succeeded but ..." classifications.
    [Theory]
    [InlineData(AgentOutcome.CorrectionNoChangesProduced)]
    [InlineData(AgentOutcome.CorrectionHeadChanged)]
    public void CompleteReviewCorrection_requires_clean_exit_evidence_for_a_process_succeeded_failure_classification(AgentOutcome outcome)
    {
        var withoutEvidence = ClaimDispatchedCorrection();
        Assert.Throws<InvalidOperationException>(() => withoutEvidence.CompleteReviewCorrection(outcome, null, BaseTime));
        AssertUntouched(withoutEvidence);

        var withCleanEvidence = ClaimDispatchedCorrection();
        withCleanEvidence.CompleteReviewCorrection(outcome, null, BaseTime, TestProcessEvidence.CleanExit);
        Assert.Equal(outcome, withCleanEvidence.AgentOutcome);
        Assert.Equal(TestProcessEvidence.CleanExit, withCleanEvidence.GetAgentProcessExecutionEvidence());
    }

    // Defect-2 regression, exercised through the real Domain transition rather than the policy
    // function alone: a pre-invocation outcome must be rejected even if a caller somehow already
    // marked the attempt dispatched.
    [Fact]
    public void CompleteAgent_rejects_evidence_for_a_pre_invocation_outcome_even_when_dispatched()
    {
        var attempt = ClaimDispatchedPlanner();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.WorkspaceNoLongerEligible, null, BaseTime, TestProcessEvidence.CleanExit));

        AssertUntouched(attempt);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public void CompleteImplementation_records_a_non_zero_exit_for_a_provider_failure()
    {
        var attempt = ClaimDispatchedImplementation();
        var evidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 137, TimeSpan.FromSeconds(9));

        attempt.CompleteImplementation(AgentOutcome.ProviderInvocationFailed, null, BaseTime, evidence);

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(evidence, attempt.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void CompleteReviewCorrection_requires_clean_exit_evidence_for_CorrectionApplied_and_records_cancellation_for_failures()
    {
        var withTimeout = ClaimDispatchedCorrection();
        var timedOut = AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMinutes(20));
        Assert.Throws<InvalidOperationException>(
            () => withTimeout.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), BaseTime, timedOut));
        AssertUntouched(withTimeout);

        var cancelled = ClaimDispatchedCorrection();
        var cancellation = AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(4));
        cancelled.CompleteReviewCorrection(AgentOutcome.ProviderInvocationFailed, null, BaseTime, cancellation);
        Assert.Equal(cancellation, cancelled.GetAgentProcessExecutionEvidence());

        var applied = ClaimDispatchedCorrection();
        applied.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, Guid.NewGuid(), BaseTime, TestProcessEvidence.CleanExit);
        Assert.Equal(AttemptStatus.Completed, applied.Status);
        Assert.Equal(TestProcessEvidence.CleanExit, applied.GetAgentProcessExecutionEvidence());
    }

    [Fact]
    public void Interrupt_never_invents_evidence_for_a_dispatched_attempt()
    {
        var attempt = ClaimDispatchedImplementation();

        attempt.Interrupt(BaseTime.AddMinutes(1));

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.GetAgentProcessExecutionEvidence());
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public void Non_agent_attempts_never_expose_agent_process_evidence()
    {
        var simulated = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);
        var process = Attempt.ClaimProcess(
            Guid.NewGuid(), Guid.NewGuid(), 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", [], @"C:\repos\sample", @"C:\repos", TimeSpan.FromMinutes(1), 1024, 2048),
            BaseTime);
        process.MarkProcessDispatched(BaseTime);
        process.CompleteProcess(ProcessOutcome.Exited, 0, BaseTime.AddSeconds(1));

        Assert.Null(simulated.GetAgentProcessExecutionEvidence());
        Assert.Null(process.GetAgentProcessExecutionEvidence());
        Assert.Null(process.AgentProcessOutcome);
    }

    private static void AssertUntouched(Attempt attempt)
    {
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentProcessExitCode);
        Assert.Null(attempt.AgentProcessDuration);
    }

    private static Attempt ClaimPlanner() => Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, BaseTime);

    private static Attempt ClaimDispatchedPlanner()
    {
        var attempt = ClaimPlanner();
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedImplementation()
    {
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 1024, 2048, BaseTime);
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimDispatchedCorrection()
    {
        var attempt = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 1024, 2048, BaseTime);
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }
}
