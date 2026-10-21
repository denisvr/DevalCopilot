using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0060: the dedicated pre-dispatch outcome of a local-execution-sequence refusal. Its Domain transition and
/// process-evidence policy admit only the four involved contracts with no dispatch and no process evidence, and no ordinary recorder
/// can assert it.</summary>
public sealed class AttemptLocalExecutionSequenceDispatchRefusalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static Attempt Implementation() => Attempt.ClaimAgentImplementation(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: 1);

    private static Attempt Review() => Attempt.ClaimAgentCodeReview(
        Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 2);

    private static Attempt Correction() => Attempt.ClaimAgentReviewCorrection(
        Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: 3);

    private static Attempt Diagnosis() => Attempt.ClaimAgentVerificationDiagnosis(
        Guid.NewGuid(), Guid.NewGuid(), 4, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, requestedModel: null, requestedEffort: null, agentBudgetSlot: 4);

    private static Attempt Planner() => Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), 5, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 5);

    public static TheoryData<string> InvolvedAttempts => new() { "implementation", "review", "correction", "diagnosis" };

    private static Attempt Make(string kind) => kind switch
    {
        "implementation" => Implementation(),
        "review" => Review(),
        "correction" => Correction(),
        _ => Diagnosis(),
    };

    [Theory]
    [MemberData(nameof(InvolvedAttempts))]
    public void An_undispatched_running_attempt_of_an_involved_contract_ends_failed_with_the_dedicated_outcome(string kind)
    {
        var attempt = Make(kind);

        attempt.CompleteLocalExecutionSequenceDispatchRefused(Now.AddMinutes(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.LocalExecutionSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Equal(Now.AddMinutes(1), attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentInputTokens);
        Assert.Null(attempt.AgentResultGitCheckpointId);
    }

    [Fact]
    public void A_planner_attempt_cannot_record_the_local_execution_sequence_refusal()
    {
        var attempt = Planner();

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteLocalExecutionSequenceDispatchRefused(Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public void A_non_Agent_attempt_cannot_record_the_refusal()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, Now);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteLocalExecutionSequenceDispatchRefused(Now));
    }

    [Theory]
    [MemberData(nameof(InvolvedAttempts))]
    public void A_dispatched_attempt_cannot_record_the_refusal(string kind)
    {
        var attempt = Make(kind);
        attempt.MarkAgentDispatched(Now);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteLocalExecutionSequenceDispatchRefused(Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Theory]
    [MemberData(nameof(InvolvedAttempts))]
    public void The_refusal_is_recorded_once(string kind)
    {
        var attempt = Make(kind);
        attempt.CompleteLocalExecutionSequenceDispatchRefused(Now);

        Assert.Throws<InvalidOperationException>(() => attempt.CompleteLocalExecutionSequenceDispatchRefused(Now));
    }

    [Theory]
    [MemberData(nameof(InvolvedAttempts))]
    public void The_ordinary_agent_completion_can_never_assert_the_refusal(string kind)
    {
        var attempt = Make(kind);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.LocalExecutionSequenceDispatchRefused, completionFingerprintSha256: null, Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public void The_implementation_completion_can_never_assert_the_refusal()
    {
        var attempt = Implementation();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteImplementation(AgentOutcome.LocalExecutionSequenceDispatchRefused, resultGitCheckpointId: null, Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public void The_review_correction_completion_can_never_assert_the_refusal()
    {
        var attempt = Correction();

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteReviewCorrection(AgentOutcome.LocalExecutionSequenceDispatchRefused, resultGitCheckpointId: null, Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public void The_outcome_is_a_pre_invocation_outcome_that_can_never_carry_process_evidence_and_is_appended_without_renumbering()
    {
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(AgentOutcome.LocalExecutionSequenceDispatchRefused));
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(AgentOutcome.LocalExecutionSequenceDispatchRefused));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(
                AgentOutcome.LocalExecutionSequenceDispatchRefused, dispatched: false, TestProcessEvidence.CleanExit));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(
                AgentOutcome.LocalExecutionSequenceDispatchRefused, dispatched: true, TestProcessEvidence.CleanExit));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(AgentOutcome.LocalExecutionSequenceDispatchRefused, dispatched: false, null));

        // Existing values are never renumbered; the new one is appended after the last.
        Assert.Equal(27, (int)AgentOutcome.AccountUsageEvidenceUnavailable);
        Assert.Equal(5, (int)AgentOutcome.WorkspaceNoLongerEligible);
        Assert.Equal(28, (int)AgentOutcome.PlanningSequenceDispatchRefused);
        Assert.Equal(29, (int)AgentOutcome.LocalExecutionSequenceDispatchRefused);
        Assert.Equal(29, Enum.GetValues<AgentOutcome>().Max(outcome => (int)outcome));
    }
}
