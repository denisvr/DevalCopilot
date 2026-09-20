using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentAttemptContractTests
{
    [Theory]
    [InlineData(AgentRole.Planner, AgentResponseContract.Proposal, AgentEffectKind.ReadOnly)]
    [InlineData(AgentRole.CriticalReviewer, AgentResponseContract.CriticalReview, AgentEffectKind.ReadOnly)]
    [InlineData(AgentRole.Resolver, AgentResponseContract.ChallengeResolution, AgentEffectKind.ReadOnly)]
    [InlineData(AgentRole.Implementer, AgentResponseContract.ImplementationReport, AgentEffectKind.WorkspaceMutating)]
    [InlineData(AgentRole.CodeReviewer, AgentResponseContract.ImplementationReview, AgentEffectKind.ReadOnly)]
    public void For_returns_the_exact_role_response_contract_and_effect(AgentRole role, AgentResponseContract expectedResponseContract, AgentEffectKind expectedEffect)
    {
        var contract = AgentAttemptContract.For(role);

        Assert.Equal(role, contract.Role);
        Assert.Equal(expectedResponseContract, contract.ResponseContract);
        Assert.Equal(expectedEffect, contract.Effect);
    }

    [Fact]
    public void For_returns_exactly_the_proposed_outcome_as_completed_for_planner()
    {
        Assert.Equal(new HashSet<AgentOutcome> { AgentOutcome.Proposed }, AgentAttemptContract.For(AgentRole.Planner).CompletedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_accepted_and_challenged_as_completed_for_critical_reviewer()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.Accepted, AgentOutcome.Challenged },
            AgentAttemptContract.For(AgentRole.CriticalReviewer).CompletedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_the_resolved_outcome_as_completed_for_resolver()
    {
        Assert.Equal(new HashSet<AgentOutcome> { AgentOutcome.Resolved }, AgentAttemptContract.For(AgentRole.Resolver).CompletedOutcomes);
    }

    /// <summary>NoChangesProduced is a legitimate terminal outcome for an Implementer attempt, but
    /// it leaves the attempt AttemptStatus.Failed today (see Attempt.CompleteImplementation) — it
    /// must never appear in the completed-outcome set alongside Implemented.</summary>
    [Fact]
    public void For_returns_exactly_the_implemented_outcome_as_completed_for_implementer_never_no_changes_produced()
    {
        var completedOutcomes = AgentAttemptContract.For(AgentRole.Implementer).CompletedOutcomes;

        Assert.Equal(new HashSet<AgentOutcome> { AgentOutcome.Implemented }, completedOutcomes);
        Assert.DoesNotContain(AgentOutcome.NoChangesProduced, completedOutcomes);
    }

    [Fact]
    public void For_returns_exactly_review_approved_and_review_changes_requested_as_completed_for_code_reviewer()
    {
        Assert.Equal(
            new HashSet<AgentOutcome> { AgentOutcome.ReviewApproved, AgentOutcome.ReviewChangesRequested },
            AgentAttemptContract.For(AgentRole.CodeReviewer).CompletedOutcomes);
    }

    [Fact]
    public void For_throws_for_an_undefined_role()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentAttemptContract.For((AgentRole)999));
    }

    [Fact]
    public void For_never_throws_for_any_currently_defined_role()
    {
        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var contract = AgentAttemptContract.For(role);
            Assert.Equal(role, contract.Role);
        }
    }

    [Fact]
    public void CompletedOutcomes_cannot_be_mutated_through_the_exposed_interface()
    {
        var completedOutcomes = AgentAttemptContract.For(AgentRole.Planner).CompletedOutcomes;

        Assert.IsNotType<HashSet<AgentOutcome>>(completedOutcomes);
        Assert.Throws<NotSupportedException>(() => ((ICollection<AgentOutcome>)completedOutcomes).Add(AgentOutcome.Resolved));
    }

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

    [Fact]
    public void CompletedOutcomesForEffect_throws_for_an_undefined_effect()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentAttemptContract.CompletedOutcomesForEffect((AgentEffectKind)999));
    }
}
