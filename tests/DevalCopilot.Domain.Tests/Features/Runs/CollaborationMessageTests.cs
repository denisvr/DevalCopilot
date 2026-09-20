using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class CollaborationMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_creates_an_immutable_bounded_proposal_envelope()
    {
        var message = CollaborationMessage.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal,
            null,
            "Propose a bounded ledger.",
            ProposalContent,
            CollaborationMessageProvenance.Simulated,
            Now);

        Assert.Equal(CollaborationMessage.ProtocolVersionOne, message.ProtocolVersion);
        Assert.Equal(CollaborationMessageType.Proposal, message.Type);
        Assert.Equal(CollaborationMessageProvenance.Simulated, message.Provenance);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("")]
    public void Record_rejects_unknown_protocol_versions(string protocolVersion)
    {
        Assert.Throws<ArgumentException>(() => RecordProposal(protocolVersion: protocolVersion));
    }

    [Fact]
    public void Record_rejects_an_invalid_actor_type_combination()
    {
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, CollaborationMessageProvenance.Simulated, Now));
    }

    [Fact]
    public void Record_rejects_missing_material_challenge_fields()
    {
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge,
            null, "A challenge", "{\"disputedItem\":\"Claim timing\"}", CollaborationMessageProvenance.Simulated, Now));
    }

    [Fact]
    public void Record_rejects_an_incomplete_decision()
    {
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Decision,
            null, "A decision", "{\"resolution\":\"Accepted\"}", CollaborationMessageProvenance.Simulated, Now));
    }

    [Fact]
    public void Record_rejects_self_reply_and_oversized_summary()
    {
        var messageId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            messageId, Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            messageId, "A summary", ProposalContent, CollaborationMessageProvenance.Simulated, Now));
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, new string('a', CollaborationMessageContentPolicy.MaximumSummaryLength + 1), ProposalContent,
            CollaborationMessageProvenance.Simulated, Now));
    }

    [Fact]
    public void Record_rejects_reply_reference_requirements_before_content_validation()
    {
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge,
            null, "A challenge", ChallengeContent, CollaborationMessageProvenance.Simulated, Now));
    }

    /// <summary>
    /// A reply is optional for a Proposal, not forbidden: a revised Proposal replies to the prior
    /// Proposal it supersedes. This Domain-level factory only owns the type-level shape (a reply
    /// reference may or may not be present); verifying that the referenced message is actually an
    /// older Proposal from the same run is the Application layer's own responsibility, exercised
    /// separately via <c>CollaborationMessageReplyPolicy.Evaluate</c>.
    /// </summary>
    [Fact]
    public void Record_accepts_a_proposal_that_replies_to_a_prior_message()
    {
        var message = CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            Guid.NewGuid(), "A revised proposal", ProposalContent, CollaborationMessageProvenance.ProviderObserved, Now);

        Assert.NotNull(message.InReplyToMessageId);
    }

    [Fact]
    public void Record_rejects_unsafe_path_or_credential_like_content()
    {
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "Use C:\\tools\\agent.exe", ProposalContent, CollaborationMessageProvenance.Simulated, Now));
    }

    [Fact]
    public void Record_rejects_undefined_closed_enum_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParticipantIdentity.FromParts((ParticipantKind)999, role: null, provider: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), (CollaborationMessageType)999,
            null, "A summary", ProposalContent, CollaborationMessageProvenance.Simulated, Now));
    }

    private static CollaborationMessage RecordProposal(string protocolVersion = CollaborationMessage.ProtocolVersionOne)
    {
        return CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, protocolVersion, ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "A summary", ProposalContent, CollaborationMessageProvenance.Simulated, Now);
    }

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";
    private const string ChallengeContent =
        "{\"disputedItem\":\"Claim\",\"materialImpact\":\"Impact\",\"reasoning\":\"Reasoning\",\"alternativeOrQuestion\":\"Alternative\"}";
}
