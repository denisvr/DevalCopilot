using System.Reflection;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class CollaborationMessageRecordAgentTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";
    private const string AcceptanceContent = "{\"rationale\":\"The plan is feasible and consistent.\"}";
    private const string ChallengeContent =
        "{\"disputedItem\":\"Claim\",\"materialImpact\":\"Impact\",\"reasoning\":\"Reasoning\",\"alternativeOrQuestion\":\"Alternative\"}";
    private const string DecisionContent =
        "{\"resolution\":\"Accepted\",\"rationale\":\"Sound\",\"resultingPlanChanges\":\"None\",\"nextAction\":\"Proceed\"}";
    private const string ExecutionReportContent = "{\"completedWork\":\"Added the table.\",\"verification\":\"Tests pass.\"}";
    private const string ReviewFindingContent =
        "{\"severity\":\"Major\",\"category\":\"Correctness\",\"evidence\":\"Observed mismatch\",\"requiredChange\":\"Fix the check\"}";
    private const string ReviewApprovalContent = "{\"rationale\":\"Matches the plan.\",\"residualRisks\":\"None material.\"}";
    private const string QuestionContent = "{\"question\":\"Which target platform?\",\"why\":\"Changes the verification plan.\"}";

    private static Attempt Dispatch(Attempt attempt)
    {
        attempt.MarkAgentDispatched(BaseTime.AddSeconds(1));
        return attempt;
    }

    private static Attempt ClaimPlannerAttempt() => Dispatch(Attempt.ClaimAgent(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));

    private static Attempt ClaimCriticalReviewerAttempt() => Dispatch(Attempt.ClaimAgentCriticalReview(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));

    private static Attempt ClaimResolverAttempt() => Dispatch(Attempt.ClaimAgentChallengeResolution(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));

    private static Attempt ClaimImplementerAttempt() => Dispatch(Attempt.ClaimAgentImplementation(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));

    private static Attempt ClaimCodeReviewerAttempt() => Dispatch(Attempt.ClaimAgentCodeReview(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, BaseTime));

    /// <summary>
    /// Overwrites a private-setter property on an already-claimed Attempt purely to construct a
    /// role/provider/field combination no production factory can produce today — every ClaimAgent*
    /// factory intentionally fixes exactly one provider per role (Slice A), and every field it sets
    /// is always internally coherent. This helper exists solely to prove RecordAgent's authorization
    /// decision is structurally independent of provider, and to exercise its fail-closed
    /// preconditions directly; it is never a substitute for a real factory and is never used outside
    /// this test file.
    /// </summary>
    private static void SetPrivateProperty(Attempt attempt, string propertyName, object? value)
    {
        var property = typeof(Attempt).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Attempt has no property named {propertyName}.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException($"Attempt.{propertyName} has no setter.");
        setter.Invoke(attempt, [value]);
    }

    [Fact]
    public void RecordAgent_throws_for_a_null_attempt()
    {
        Assert.Throws<ArgumentNullException>(() => CollaborationMessage.RecordAgent(
            null!, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Theory]
    [MemberData(nameof(RealRoleOutputPairs))]
    public void RecordAgent_accepts_every_current_real_role_output_pair(
        Attempt attempt, ParticipantIdentity recipient, CollaborationMessageType type, bool requiresReply, string summary, string content,
        ParticipantIdentity expectedActor)
    {
        var inReplyToMessageId = requiresReply ? Guid.NewGuid() : (Guid?)null;

        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), recipient, type, inReplyToMessageId, summary, content, BaseTime.AddSeconds(2));

        Assert.Equal(type, message.Type);
        Assert.Equal(expectedActor.Kind, message.Actor.Kind);
        Assert.Equal(expectedActor.Provider, message.Actor.Provider);
        Assert.Equal(attempt.AgentRole, message.Actor.Role);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, message.Provenance);
        Assert.Equal(attempt.RunId, message.RunId);
        Assert.Equal(attempt.Id, message.AttemptId);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, message.ProtocolVersion);
    }

    // Every CollaborationMessageType except Proposal requires a reply per CollaborationMessageReplyPolicy.
    public static IEnumerable<object[]> RealRoleOutputPairs()
    {
        yield return
        [
            ClaimPlannerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, false, "A proposal", ProposalContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
        yield return
        [
            ClaimPlannerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Question, true, "A question", QuestionContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
        yield return
        [
            ClaimCriticalReviewerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, true, "An acceptance", AcceptanceContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        ];
        yield return
        [
            ClaimCriticalReviewerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge, true, "A challenge", ChallengeContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        ];
        yield return
        [
            ClaimResolverAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Decision, true, "A decision", DecisionContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
        yield return
        [
            ClaimResolverAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, false, "A revised proposal", ProposalContent,
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
        yield return
        [
            ClaimImplementerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, true, "An execution report",
            ExecutionReportContent, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
        ];
        yield return
        [
            ClaimCodeReviewerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewApproval, true, "A review approval",
            ReviewApprovalContent, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
        yield return
        [
            ClaimCodeReviewerAttempt(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ReviewFinding, true, "A review finding",
            ReviewFindingContent, ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
        ];
    }

    [Fact]
    public void RecordAgent_throws_for_a_forbidden_role_type_combination()
    {
        var attempt = ClaimPlannerAttempt();

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.ExecutionReport,
            Guid.NewGuid(), "An execution report", ExecutionReportContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_a_non_agent_attempt()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_role()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentRole), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_provider()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_response_contract()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentResponseContract), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_when_the_role_and_response_contract_are_not_coherent()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentResponseContract), AgentResponseContract.CriticalReview);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_a_malformed_protocol_version()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProtocolVersion), "2.0");

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_undispatched_attempt()
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    // Provider-independence evidence: production code can never construct these role/provider
    // combinations today (every ClaimAgent* factory fixes exactly one provider per role), so the
    // attempt's provider is overwritten via SetPrivateProperty purely to prove RecordAgent's
    // authorization decision — and the truthfulness of its derived Actor — cannot depend on which
    // provider actually produced the attempt.
    [Fact]
    public void RecordAgent_succeeds_when_resolver_is_assigned_to_claude_code_authoring_decision()
    {
        var attempt = ClaimResolverAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), AgentProvider.ClaudeCode);

        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Decision,
            Guid.NewGuid(), "A decision", DecisionContent, BaseTime.AddSeconds(2));

        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.Resolver, AgentProvider.ClaudeCode), message.Actor);
        Assert.Equal(CollaborationMessageType.Decision, message.Type);
    }

    [Fact]
    public void RecordAgent_succeeds_when_critical_reviewer_is_assigned_to_codex_authoring_acceptance()
    {
        var attempt = ClaimCriticalReviewerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), AgentProvider.Codex);

        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Acceptance,
            Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2));

        Assert.Equal(ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.Codex), message.Actor);
        Assert.Equal(CollaborationMessageType.Acceptance, message.Type);
    }

    [Fact]
    public void RecordAgent_rejects_a_forbidden_role_type_combination_identically_regardless_of_provider()
    {
        var realProviderAttempt = ClaimResolverAttempt();
        var substitutedProviderAttempt = ClaimResolverAttempt();
        SetPrivateProperty(substitutedProviderAttempt, nameof(Attempt.AgentProvider), AgentProvider.ClaudeCode);

        // Resolver may never author Acceptance, regardless of which provider produced the attempt.
        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            realProviderAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Acceptance,
            Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2)));
        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            substitutedProviderAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance,
            Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2)));
    }

    /// <summary>Complements CollaborationMessageAuthorPolicyTests' structural proof (the policy
    /// method's only parameter is AgentRole) with an end-to-end proof at the RecordAgent boundary:
    /// no ParticipantKind/provider value passed to or derived within RecordAgent ever participates
    /// in the authorization decision.</summary>
    [Fact]
    public void RecordAgent_authorization_outcome_is_identical_across_every_defined_provider_for_the_same_role()
    {
        foreach (var provider in Enum.GetValues<AgentProvider>())
        {
            var allowedAttempt = ClaimResolverAttempt();
            SetPrivateProperty(allowedAttempt, nameof(Attempt.AgentProvider), provider);
            var allowedActor = ParticipantIdentity.ForAgent(AgentRole.Resolver, provider);
            var recipient = provider == AgentProvider.Codex
                ? ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode)
                : ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex);

            var message = CollaborationMessage.RecordAgent(
                allowedAttempt, Guid.NewGuid(), recipient, CollaborationMessageType.Decision,
                Guid.NewGuid(), "A decision", DecisionContent, BaseTime.AddSeconds(2));
            Assert.Equal(allowedActor, message.Actor);

            var forbiddenAttempt = ClaimResolverAttempt();
            SetPrivateProperty(forbiddenAttempt, nameof(Attempt.AgentProvider), provider);
            Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
                forbiddenAttempt, Guid.NewGuid(), recipient, CollaborationMessageType.Acceptance,
                Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2)));
        }
    }
}
