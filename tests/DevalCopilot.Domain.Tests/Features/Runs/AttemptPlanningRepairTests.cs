using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AttemptPlanningRepairTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "fingerprint-1";

    private static Attempt ClaimOrdinary(int number = 1) => Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: number);

    private static Attempt ClaimRepair(Guid sourceId, string? model = null, string? effort = null) =>
        Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, model, effort, agentBudgetSlot: 2, sourceId);

    private static Attempt Dispatched(Attempt attempt)
    {
        attempt.MarkAgentDispatched(Now);
        return attempt;
    }

    private static Attempt InvalidOutput(Attempt attempt)
    {
        Dispatched(attempt).CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, Now, TestProcessEvidence.CleanExit);
        return attempt;
    }

    [Fact]
    public void A_repair_claim_is_an_ordinary_read_only_planner_attempt_with_an_immutable_source_link()
    {
        var sourceId = Guid.NewGuid();

        var repair = ClaimRepair(sourceId, "gpt-6-sol", "high");

        Assert.Equal(sourceId, repair.AgentRepairSourceAttemptId);
        Assert.Equal(AttemptKind.Agent, repair.Kind);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Equal(AgentProvider.Codex, repair.AgentProvider);
        Assert.Equal(AgentRole.Planner, repair.AgentRole);
        Assert.Equal(AgentResponseContract.Proposal, repair.AgentResponseContract);
        Assert.Equal(CollaborationMessageType.Proposal, repair.AgentExpectedMessageType);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal("codex-planning-v1", repair.AgentAdapterContractVersion);
        Assert.Equal("gpt-6-sol", repair.AgentRequestedModel);
        Assert.Equal("high", repair.AgentRequestedEffort);
        Assert.Equal(2, repair.AgentBudgetSlot);
    }

    [Fact]
    public void An_ordinary_planner_claim_has_no_source_link()
    {
        Assert.Null(ClaimOrdinary().AgentRepairSourceAttemptId);
    }

    [Fact]
    public void A_repair_claim_rejects_an_empty_source_identity_and_a_self_link()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ClaimRepair(Guid.Empty));
        Assert.Throws<ArgumentException>(() => Attempt.ClaimAgentPlanningRepair(
            id, Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, 2, id));
    }

    [Fact]
    public void A_repair_claim_keeps_every_ordinary_claim_validation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, 2, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ClaimRepair(Guid.NewGuid(), model: null, effort: "high"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.Zero, 1024, 2048, Now, null, null, 2, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentPlanningRepair(
            Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, null, null, 0, Guid.NewGuid()));
    }

    [Fact]
    public void A_dispatched_planner_attempt_with_invalid_structured_output_is_an_eligible_source()
    {
        var source = InvalidOutput(ClaimOrdinary());

        Assert.Equal(AttemptStatus.Failed, source.Status);
        Assert.True(source.IsEligiblePlanningRepairSource);
    }

    [Fact]
    public void A_persisted_completed_row_with_invalid_output_is_not_an_eligible_source()
    {
        var source = InvalidOutput(ClaimOrdinary());
        Assert.True(source.IsEligiblePlanningRepairSource);

        // CompleteAgent only ever records Failed for this outcome; force the incoherent persisted shape.
        typeof(Attempt).GetProperty(nameof(Attempt.Status))!.SetValue(source, AttemptStatus.Completed);

        Assert.Equal(AgentOutcome.InvalidStructuredOutput, source.AgentOutcome);
        Assert.False(source.IsEligiblePlanningRepairSource);
    }

    [Fact]
    public void A_repair_is_never_an_eligible_source_even_with_the_same_invalid_outcome()
    {
        var repair = InvalidOutput(ClaimRepair(Guid.NewGuid()));

        Assert.Equal(AgentOutcome.InvalidStructuredOutput, repair.AgentOutcome);
        Assert.False(repair.IsEligiblePlanningRepairSource);
    }

    [Fact]
    public void A_running_planner_attempt_is_not_an_eligible_source()
    {
        Assert.False(Dispatched(ClaimOrdinary()).IsEligiblePlanningRepairSource);
        Assert.False(ClaimOrdinary().IsEligiblePlanningRepairSource);
    }

    [Theory]
    [InlineData(AgentOutcome.ProviderInvocationFailed)]
    [InlineData(AgentOutcome.SourceChanged)]
    [InlineData(AgentOutcome.WorkspaceNoLongerEligible)]
    public void A_planner_attempt_with_any_other_outcome_is_not_an_eligible_source(AgentOutcome outcome)
    {
        var attempt = ClaimOrdinary();
        attempt.CompleteAgent(outcome, null, Now);

        Assert.False(attempt.IsEligiblePlanningRepairSource);
    }

    [Fact]
    public void A_critical_review_attempt_with_invalid_output_is_not_an_eligible_source()
    {
        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, agentBudgetSlot: 2);
        InvalidOutput(review);

        Assert.Equal(AgentOutcome.InvalidStructuredOutput, review.AgentOutcome);
        Assert.False(review.IsEligiblePlanningRepairSource);
    }

    [Fact]
    public void A_simulated_attempt_is_not_an_eligible_source()
    {
        Assert.False(Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, Now).IsEligiblePlanningRepairSource);
    }
}
