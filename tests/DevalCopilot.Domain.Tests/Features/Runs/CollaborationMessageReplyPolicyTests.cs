using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class CollaborationMessageReplyPolicyTests
{
    [Theory]
    [InlineData(CollaborationMessageType.Proposal, null)]
    [InlineData(CollaborationMessageType.Proposal, CollaborationMessageType.Proposal)]
    [InlineData(CollaborationMessageType.Acceptance, CollaborationMessageType.Proposal)]
    [InlineData(CollaborationMessageType.Challenge, CollaborationMessageType.Proposal)]
    [InlineData(CollaborationMessageType.Decision, CollaborationMessageType.Proposal)]
    [InlineData(CollaborationMessageType.Decision, CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.ExecutionReport, CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.ExecutionReport, CollaborationMessageType.Proposal)]
    [InlineData(CollaborationMessageType.ReviewFinding, CollaborationMessageType.ExecutionReport)]
    [InlineData(CollaborationMessageType.RevisionResponse, CollaborationMessageType.ReviewFinding)]
    [InlineData(CollaborationMessageType.Question, CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.Escalation, CollaborationMessageType.RevisionResponse)]
    public void Evaluate_accepts_each_version_one_parent_relationship(
        CollaborationMessageType type,
        CollaborationMessageType? parentType)
    {
        var result = CollaborationMessageReplyPolicy.Evaluate(type, parentType);

        Assert.Equal(CollaborationMessageReplyViolation.None, result);
    }

    [Theory]
    [InlineData(CollaborationMessageType.Proposal, CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.Acceptance, CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.Challenge, CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.Decision, CollaborationMessageType.ExecutionReport)]
    [InlineData(CollaborationMessageType.ExecutionReport, CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.ReviewFinding, CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.RevisionResponse, CollaborationMessageType.ExecutionReport)]
    [InlineData(CollaborationMessageType.Question, CollaborationMessageType.RevisionResponse)]
    [InlineData(CollaborationMessageType.Escalation, CollaborationMessageType.Escalation)]
    public void Evaluate_rejects_each_invalid_parent_relationship(
        CollaborationMessageType type,
        CollaborationMessageType parentType)
    {
        var result = CollaborationMessageReplyPolicy.Evaluate(type, parentType);

        Assert.Equal(CollaborationMessageReplyViolation.InvalidParentType, result);
    }

    [Theory]
    [InlineData(CollaborationMessageType.Acceptance)]
    [InlineData(CollaborationMessageType.Challenge)]
    [InlineData(CollaborationMessageType.Decision)]
    [InlineData(CollaborationMessageType.ExecutionReport)]
    [InlineData(CollaborationMessageType.ReviewFinding)]
    [InlineData(CollaborationMessageType.RevisionResponse)]
    [InlineData(CollaborationMessageType.Question)]
    [InlineData(CollaborationMessageType.Escalation)]
    public void Evaluate_requires_a_parent_for_each_non_proposal_message(CollaborationMessageType type)
    {
        var result = CollaborationMessageReplyPolicy.Evaluate(type, null);

        Assert.Equal(CollaborationMessageReplyViolation.ReplyRequired, result);
    }

    [Fact]
    public void EvaluateReference_never_requires_or_forbids_a_reply_for_a_proposal()
    {
        // A root Proposal has no reply; a revised Proposal replies to the prior Proposal it
        // supersedes — both are valid at the reference-only level. Application-layer validation
        // is what actually requires the parent to be an older Proposal from the same run.
        Assert.Equal(CollaborationMessageReplyViolation.None, CollaborationMessageReplyPolicy.EvaluateReference(CollaborationMessageType.Proposal, hasReply: false));
        Assert.Equal(CollaborationMessageReplyViolation.None, CollaborationMessageReplyPolicy.EvaluateReference(CollaborationMessageType.Proposal, hasReply: true));
    }
}
