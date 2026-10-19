using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0055: the dedicated pre-dispatch outcome of a planning-sequence refusal. Its Domain transition and process-evidence policy
/// admit only the three read-only planning contracts with no dispatch and no process evidence, and no ordinary provider-result recorder
/// can assert it.</summary>
public sealed class AttemptPlanningSequenceDispatchRefusalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static Attempt Planner() => Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 1);

    private static Attempt Review() => Attempt.ClaimAgentCriticalReview(
        Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 2);

    private static Attempt Resolver() => Attempt.ClaimAgentChallengeResolution(
        Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 3);

    private static Attempt Implementer() => Attempt.ClaimAgentImplementation(
        Guid.NewGuid(), Guid.NewGuid(), 4, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: 4);

    public static TheoryData<string> ReadOnlyPlanningAttempts => new() { "planner", "review", "resolver" };

    private static Attempt Make(string kind) => kind switch
    {
        "planner" => Planner(),
        "review" => Review(),
        _ => Resolver(),
    };

    [Theory]
    [MemberData(nameof(ReadOnlyPlanningAttempts))]
    public void An_undispatched_running_attempt_of_a_read_only_planning_contract_ends_failed_with_the_dedicated_outcome(string kind)
    {
        var attempt = Make(kind);

        attempt.CompletePlanningSequenceDispatchRefused(Now.AddMinutes(1));

        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.PlanningSequenceDispatchRefused, attempt.AgentOutcome);
        Assert.Equal(Now.AddMinutes(1), attempt.CompletedAtUtc);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentInputTokens);
    }

    [Fact]
    public void A_mutating_implementation_attempt_cannot_record_the_planning_sequence_refusal()
    {
        var attempt = Implementer();

        Assert.Throws<InvalidOperationException>(() => attempt.CompletePlanningSequenceDispatchRefused(Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void A_dispatched_attempt_cannot_record_the_refusal()
    {
        var attempt = Planner();
        attempt.MarkAgentDispatched(Now);

        Assert.Throws<InvalidOperationException>(() => attempt.CompletePlanningSequenceDispatchRefused(Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public void The_refusal_is_recorded_once()
    {
        var attempt = Review();
        attempt.CompletePlanningSequenceDispatchRefused(Now);

        Assert.Throws<InvalidOperationException>(() => attempt.CompletePlanningSequenceDispatchRefused(Now));
    }

    [Theory]
    [MemberData(nameof(ReadOnlyPlanningAttempts))]
    public void The_ordinary_agent_completion_can_never_assert_the_planning_sequence_refusal(string kind)
    {
        var attempt = Make(kind);

        Assert.Throws<InvalidOperationException>(
            () => attempt.CompleteAgent(AgentOutcome.PlanningSequenceDispatchRefused, completionFingerprintSha256: null, Now));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
    }

    [Fact]
    public void The_outcome_is_a_pre_invocation_outcome_that_can_never_carry_process_evidence_and_is_appended_without_renumbering()
    {
        Assert.True(AgentProcessEvidencePolicy.IsPreInvocationOutcome(AgentOutcome.PlanningSequenceDispatchRefused));
        Assert.False(AgentProcessEvidencePolicy.RequiresCleanExit(AgentOutcome.PlanningSequenceDispatchRefused));
        Assert.Equal(
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence,
            AgentProcessEvidencePolicy.Evaluate(AgentOutcome.PlanningSequenceDispatchRefused, dispatched: false, TestProcessEvidence.CleanExit));
        Assert.Null(AgentProcessEvidencePolicy.Evaluate(AgentOutcome.PlanningSequenceDispatchRefused, dispatched: false, null));

        // Existing values are never renumbered; the new one is appended after the last.
        Assert.Equal(27, (int)AgentOutcome.AccountUsageEvidenceUnavailable);
        Assert.Equal(5, (int)AgentOutcome.WorkspaceNoLongerEligible);
        Assert.Equal(28, (int)AgentOutcome.PlanningSequenceDispatchRefused);
        Assert.NotEqual(AgentOutcome.WorkspaceNoLongerEligible, AgentOutcome.PlanningSequenceDispatchRefused);
    }
}
