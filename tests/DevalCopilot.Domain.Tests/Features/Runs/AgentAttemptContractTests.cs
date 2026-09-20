using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentAttemptContractTests
{
    // -------------------------------------------------------------------------
    // For(AgentResponseContract) — happy-path table shape
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AgentResponseContract.Proposal, AgentRole.Planner, AgentEffectKind.ReadOnly)]
    [InlineData(AgentResponseContract.CriticalReview, AgentRole.CriticalReviewer, AgentEffectKind.ReadOnly)]
    [InlineData(AgentResponseContract.ChallengeResolution, AgentRole.Resolver, AgentEffectKind.ReadOnly)]
    [InlineData(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentEffectKind.WorkspaceMutating)]
    [InlineData(AgentResponseContract.ImplementationReview, AgentRole.CodeReviewer, AgentEffectKind.ReadOnly)]
    [InlineData(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentEffectKind.WorkspaceMutating)]
    public void For_returns_the_exact_response_contract_role_and_effect(
        AgentResponseContract responseContract, AgentRole expectedRole, AgentEffectKind expectedEffect)
    {
        var contract = AgentAttemptContract.For(responseContract);

        Assert.Equal(responseContract, contract.ResponseContract);
        Assert.Equal(expectedRole, contract.Role);
        Assert.Equal(expectedEffect, contract.Effect);
    }

    [Fact]
    public void For_returns_exactly_the_proposed_outcome_as_completed_for_Proposal()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.Proposed },
            AgentAttemptContract.For(AgentResponseContract.Proposal).CompletedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_accepted_and_challenged_as_completed_for_CriticalReview()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.Accepted, AgentOutcome.Challenged },
            AgentAttemptContract.For(AgentResponseContract.CriticalReview).CompletedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_the_resolved_outcome_as_completed_for_ChallengeResolution()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.Resolved },
            AgentAttemptContract.For(AgentResponseContract.ChallengeResolution).CompletedOutcomes);
    }

    /// <summary>NoChangesProduced is a legitimate terminal outcome for an ImplementationReport
    /// attempt, but it leaves the attempt AttemptStatus.Failed — it must never appear in the
    /// completed-outcome set alongside Implemented.</summary>
    [Fact]
    public void For_returns_exactly_the_implemented_outcome_as_completed_for_ImplementationReport_never_no_changes_produced()
    {
        var completedOutcomes = AgentAttemptContract.For(AgentResponseContract.ImplementationReport).CompletedOutcomes;

        Assert.Equal(new HashSet<AgentOutcome> { AgentOutcome.Implemented }, completedOutcomes);
        Assert.DoesNotContain(AgentOutcome.NoChangesProduced, completedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_review_approved_and_review_changes_requested_as_completed_for_ImplementationReview()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.ReviewApproved, AgentOutcome.ReviewChangesRequested },
            AgentAttemptContract.For(AgentResponseContract.ImplementationReview).CompletedOutcomes);
    }

    /// <summary>CorrectionNoChangesProduced is a legitimate terminal failure outcome for a
    /// ReviewCorrection attempt — it must never appear in the completed-outcome set alongside
    /// CorrectionApplied.</summary>
    [Fact]
    public void For_returns_exactly_the_correction_applied_outcome_as_completed_for_ReviewCorrection()
    {
        var completedOutcomes = AgentAttemptContract.For(AgentResponseContract.ReviewCorrection).CompletedOutcomes;

        Assert.Equal(new HashSet<AgentOutcome> { AgentOutcome.CorrectionApplied }, completedOutcomes);
        Assert.DoesNotContain(AgentOutcome.CorrectionNoChangesProduced, completedOutcomes);
    }

    // -------------------------------------------------------------------------
    // For(AgentResponseContract) — error cases
    // -------------------------------------------------------------------------

    [Fact]
    public void For_throws_for_an_undefined_response_contract()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentAttemptContract.For((AgentResponseContract)999));
    }

    [Fact]
    public void For_never_throws_for_any_currently_defined_response_contract()
    {
        foreach (var rc in Enum.GetValues<AgentResponseContract>())
        {
            var contract = AgentAttemptContract.For(rc);
            Assert.Equal(rc, contract.ResponseContract);
        }
    }

    // -------------------------------------------------------------------------
    // Immutability
    // -------------------------------------------------------------------------

    [Fact]
    public void CompletedOutcomes_cannot_be_mutated_through_the_exposed_interface()
    {
        var completedOutcomes = AgentAttemptContract.For(AgentResponseContract.Proposal).CompletedOutcomes;

        Assert.IsNotType<HashSet<AgentOutcome>>(completedOutcomes);
        Assert.Throws<NotSupportedException>(() => ((ICollection<AgentOutcome>)completedOutcomes).Add(AgentOutcome.Resolved));
    }

    // -------------------------------------------------------------------------
    // RolesForEffect
    // -------------------------------------------------------------------------

    /// <summary>Both ImplementationReport and ReviewCorrection are WorkspaceMutating Implementer
    /// contracts, so Implementer still appears exactly once in the mutating set returned by
    /// RolesForEffect — the set deduplicates across the two contracts sharing the same role.
    /// </summary>
    [Fact]
    public void RolesForEffect_partitions_every_defined_role_into_exactly_two_disjoint_sets()
    {
        var readOnlyRoles = AgentAttemptContract.RolesForEffect(AgentEffectKind.ReadOnly);
        var mutatingRoles = AgentAttemptContract.RolesForEffect(AgentEffectKind.WorkspaceMutating);

        Assert.Equal(
            new HashSet<AgentRole> { AgentRole.Planner, AgentRole.CriticalReviewer, AgentRole.Resolver, AgentRole.CodeReviewer },
            readOnlyRoles);
        Assert.Equal(new HashSet<AgentRole> { AgentRole.Implementer }, mutatingRoles);
        Assert.Empty(readOnlyRoles.Intersect(mutatingRoles));

        var everyDefinedRole = Enum.GetValues<AgentRole>().ToHashSet();
        Assert.Equal(everyDefinedRole, readOnlyRoles.Union(mutatingRoles).ToHashSet());
    }

    [Fact]
    public void RolesForEffect_result_cannot_be_mutated_through_the_exposed_interface()
    {
        var readOnlyRoles = AgentAttemptContract.RolesForEffect(AgentEffectKind.ReadOnly);

        Assert.Throws<NotSupportedException>(() => ((ICollection<AgentRole>)readOnlyRoles).Add(AgentRole.Implementer));
    }

    [Fact]
    public void RolesForEffect_throws_for_an_undefined_effect()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentAttemptContract.RolesForEffect((AgentEffectKind)999));
    }

    // -------------------------------------------------------------------------
    // CompletedOutcomesForEffect
    // -------------------------------------------------------------------------

    [Fact]
    public void CompletedOutcomesForEffect_unions_every_read_only_contracts_completed_outcomes()
    {
        var completedOutcomes = AgentAttemptContract.CompletedOutcomesForEffect(AgentEffectKind.ReadOnly);

        Assert.Equal(
            new HashSet<AgentOutcome>
            {
                AgentOutcome.Proposed, AgentOutcome.Accepted, AgentOutcome.Challenged, AgentOutcome.Resolved,
                AgentOutcome.ReviewApproved, AgentOutcome.ReviewChangesRequested,
            },
            completedOutcomes);
    }

    /// <summary>Both WorkspaceMutating contracts (ImplementationReport and ReviewCorrection) must
    /// contribute their own success outcomes to the mutating completed-outcomes union.</summary>
    [Fact]
    public void CompletedOutcomesForEffect_unions_both_workspace_mutating_contracts_completed_outcomes()
    {
        var completedOutcomes = AgentAttemptContract.CompletedOutcomesForEffect(AgentEffectKind.WorkspaceMutating);

        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.Implemented, AgentOutcome.CorrectionApplied },
            completedOutcomes);
    }

    [Fact]
    public void CompletedOutcomesForEffect_throws_for_an_undefined_effect()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentAttemptContract.CompletedOutcomesForEffect((AgentEffectKind)999));
    }
}
