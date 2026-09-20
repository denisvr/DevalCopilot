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
            null!, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Theory]
    [MemberData(nameof(RealRoleOutputPairs))]
    public void RecordAgent_accepts_every_current_real_role_output_pair(
        Attempt attempt, ParticipantKind recipient, CollaborationMessageType type, bool requiresReply, string summary, string content,
        ParticipantKind expectedActor)
    {
        var inReplyToMessageId = requiresReply ? Guid.NewGuid() : (Guid?)null;

        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), recipient, type, inReplyToMessageId, summary, content, BaseTime.AddSeconds(2));

        Assert.Equal(type, message.Type);
        Assert.Equal(expectedActor, message.Actor);
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
            ClaimPlannerAttempt(), ParticipantKind.Claude, CollaborationMessageType.Proposal, false, "A proposal", ProposalContent,
            ParticipantKind.Codex,
        ];
        yield return
        [
            ClaimPlannerAttempt(), ParticipantKind.Claude, CollaborationMessageType.Question, true, "A question", QuestionContent,
            ParticipantKind.Codex,
        ];
        yield return
        [
            ClaimCriticalReviewerAttempt(), ParticipantKind.Codex, CollaborationMessageType.Acceptance, true, "An acceptance", AcceptanceContent,
            ParticipantKind.Claude,
        ];
        yield return
        [
            ClaimCriticalReviewerAttempt(), ParticipantKind.Codex, CollaborationMessageType.Challenge, true, "A challenge", ChallengeContent,
            ParticipantKind.Claude,
        ];
        yield return
        [
            ClaimResolverAttempt(), ParticipantKind.Claude, CollaborationMessageType.Decision, true, "A decision", DecisionContent,
            ParticipantKind.Codex,
        ];
        yield return
        [
            ClaimResolverAttempt(), ParticipantKind.Claude, CollaborationMessageType.Proposal, false, "A revised proposal", ProposalContent,
            ParticipantKind.Codex,
        ];
        yield return
        [
            ClaimImplementerAttempt(), ParticipantKind.Codex, CollaborationMessageType.ExecutionReport, true, "An execution report",
            ExecutionReportContent, ParticipantKind.Claude,
        ];
        yield return
        [
            ClaimCodeReviewerAttempt(), ParticipantKind.Claude, CollaborationMessageType.ReviewApproval, true, "A review approval",
            ReviewApprovalContent, ParticipantKind.Codex,
        ];
        yield return
        [
            ClaimCodeReviewerAttempt(), ParticipantKind.Claude, CollaborationMessageType.ReviewFinding, true, "A review finding",
            ReviewFindingContent, ParticipantKind.Codex,
        ];
    }

    [Fact]
    public void RecordAgent_throws_for_a_forbidden_role_type_combination()
    {
        var attempt = ClaimPlannerAttempt();

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.ExecutionReport,
            Guid.NewGuid(), "An execution report", ExecutionReportContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_a_non_agent_attempt()
    {
        var attempt = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, BaseTime);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_role()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentRole), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_provider()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_absent_response_contract()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentResponseContract), null);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_when_the_role_and_response_contract_are_not_coherent()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentResponseContract), AgentResponseContract.CriticalReview);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_a_malformed_protocol_version()
    {
        var attempt = ClaimPlannerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProtocolVersion), "2.0");

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
            null, "A summary", ProposalContent, BaseTime.AddSeconds(2)));
    }

    [Fact]
    public void RecordAgent_throws_for_an_undispatched_attempt()
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint-1", Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, BaseTime);

        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Proposal,
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
            attempt, Guid.NewGuid(), ParticipantKind.Codex, CollaborationMessageType.Decision,
            Guid.NewGuid(), "A decision", DecisionContent, BaseTime.AddSeconds(2));

        Assert.Equal(ParticipantKind.Claude, message.Actor);
        Assert.Equal(CollaborationMessageType.Decision, message.Type);
    }

    [Fact]
    public void RecordAgent_succeeds_when_critical_reviewer_is_assigned_to_codex_authoring_acceptance()
    {
        var attempt = ClaimCriticalReviewerAttempt();
        SetPrivateProperty(attempt, nameof(Attempt.AgentProvider), AgentProvider.Codex);

        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Acceptance,
            Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2));

        Assert.Equal(ParticipantKind.Codex, message.Actor);
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
            realProviderAttempt, Guid.NewGuid(), ParticipantKind.Claude, CollaborationMessageType.Acceptance,
            Guid.NewGuid(), "An acceptance", AcceptanceContent, BaseTime.AddSeconds(2)));
        Assert.Throws<ArgumentException>(() => CollaborationMessage.RecordAgent(
            substitutedProviderAttempt, Guid.NewGuid(), ParticipantKind.Codex, CollaborationMessageType.Acceptance,
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
            var allowedActor = AgentProviderParticipant.For(provider);
            var recipient = allowedActor == ParticipantKind.Codex ? ParticipantKind.Claude : ParticipantKind.Codex;

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
