using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class CollaborationMessageAuthorPolicyTests
{
    [Fact]
    public void AllowedMessageTypes_returns_exactly_proposal_question_and_escalation_for_planner()
    {
        Assert.Equal(
            new HashSet<CollaborationMessageType>
            {
                CollaborationMessageType.Proposal, CollaborationMessageType.Question, CollaborationMessageType.Escalation,
            },
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.Planner));
    }

    [Fact]
    public void AllowedMessageTypes_returns_exactly_acceptance_challenge_question_and_escalation_for_critical_reviewer()
    {
        Assert.Equal(
            new HashSet<CollaborationMessageType>
            {
                CollaborationMessageType.Acceptance, CollaborationMessageType.Challenge,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation,
            },
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.CriticalReviewer));
    }

    [Fact]
    public void AllowedMessageTypes_returns_exactly_decision_proposal_question_and_escalation_for_resolver()
    {
        Assert.Equal(
            new HashSet<CollaborationMessageType>
            {
                CollaborationMessageType.Decision, CollaborationMessageType.Proposal,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation,
            },
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.Resolver));
    }

    [Fact]
    public void AllowedMessageTypes_returns_exactly_execution_report_question_and_escalation_for_implementer()
    {
        Assert.Equal(
            new HashSet<CollaborationMessageType>
            {
                CollaborationMessageType.ExecutionReport, CollaborationMessageType.Question, CollaborationMessageType.Escalation,
            },
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.Implementer));
    }

    [Fact]
    public void AllowedMessageTypes_returns_exactly_review_approval_review_finding_question_and_escalation_for_code_reviewer()
    {
        Assert.Equal(
            new HashSet<CollaborationMessageType>
            {
                CollaborationMessageType.ReviewApproval, CollaborationMessageType.ReviewFinding,
                CollaborationMessageType.Question, CollaborationMessageType.Escalation,
            },
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.CodeReviewer));
    }

    /// <summary>No AgentRole/AgentResponseContract pair authorizes RevisionResponse today — no
    /// Reviser role or contract exists yet.</summary>
    [Theory]
    [InlineData(AgentRole.Planner)]
    [InlineData(AgentRole.CriticalReviewer)]
    [InlineData(AgentRole.Resolver)]
    [InlineData(AgentRole.Implementer)]
    [InlineData(AgentRole.CodeReviewer)]
    public void AllowedMessageTypes_never_authorizes_revision_response_for_any_current_role(AgentRole role)
    {
        Assert.DoesNotContain(CollaborationMessageType.RevisionResponse, CollaborationMessageAuthorPolicy.AllowedMessageTypes(role));
    }

    [Fact]
    public void AllowedMessageTypes_throws_for_an_undefined_role()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CollaborationMessageAuthorPolicy.AllowedMessageTypes((AgentRole)999));
    }

    [Fact]
    public void AllowedMessageTypes_never_throws_for_any_currently_defined_role()
    {
        foreach (var role in Enum.GetValues<AgentRole>())
        {
            Assert.NotEmpty(CollaborationMessageAuthorPolicy.AllowedMessageTypes(role));
        }
    }

    [Fact]
    public void AllowedMessageTypes_result_cannot_be_mutated_through_the_exposed_interface()
    {
        var allowed = CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.Planner);

        Assert.Throws<NotSupportedException>(() => ((ICollection<CollaborationMessageType>)allowed).Add(CollaborationMessageType.Decision));
    }

    /// <summary>Direct proof this policy has no provider dimension at all: its only parameter is
    /// AgentRole, so a message's authorization can never depend on which provider produced the
    /// attempt — verified here by reflection over the public API surface rather than by behavior,
    /// since the type signature itself is the guarantee.</summary>
    [Fact]
    public void AllowedMessageTypes_has_no_provider_or_participant_parameter()
    {
        var method = typeof(CollaborationMessageAuthorPolicy).GetMethod(nameof(CollaborationMessageAuthorPolicy.AllowedMessageTypes));

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(AgentRole), parameters[0].ParameterType);
    }
}
