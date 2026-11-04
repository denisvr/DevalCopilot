using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class StageVerifierTests
{
    private static readonly Guid Attempt = Guid.NewGuid();
    private static readonly Guid Proposal = Guid.NewGuid();
    private static readonly Guid Reply = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static StageReading Planner => QualificationReadings.Planner(Attempt, Proposal);

    private static StageReading Reviewer(bool accepted = false) => QualificationReadings.Reviewer(Attempt, Reply, Proposal, accepted);

    [Fact]
    public void A_coherent_proposal_and_either_kind_of_reply_to_it_are_valid()
    {
        Assert.Null(StageVerifier.Verify(AllowanceRole.Planner, Planner, null, Attempt));
        Assert.Null(StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(accepted: false), Proposal, Attempt));
        Assert.Null(StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(accepted: true), Proposal, Attempt));
    }

    public static TheoryData<string, StageReading, string> PlannerDefects => new()
    {
        { "no attempt", new StageReading(), "NoAttempt" },
        { "wrong provider", Planner with { Provider = "ClaudeCode" }, "WrongRoleOrProvider" },
        { "wrong role", Planner with { Role = "CriticalReviewer" }, "WrongRoleOrProvider" },
        { "wrong contract", Planner with { Contract = "CriticalReview" }, "WrongRoleOrProvider" },
        { "second attempt exists", Planner with { AttemptsInRun = 2 }, "UnexpectedAttemptCount" },
        { "failed", Planner with { Status = "Failed", Outcome = "ProviderInvocationFailed" }, "AttemptFailedProviderInvocationFailed" },
        { "not a proposal outcome", Planner with { Outcome = "SourceChanged" }, "AttemptCompletedSourceChanged" },
        { "never dispatched", Planner with { Dispatched = false }, "ProcessNotCleanExit" },
        { "timed out", Planner with { ProcessOutcome = "TimedOut", ExitCode = null }, "ProcessNotCleanExit" },
        { "non-zero exit", Planner with { ExitCode = 1 }, "ProcessNotCleanExit" },
        { "status route disagrees", Planner with { StatusRouteAgrees = false }, "StatusRouteDisagrees" },
        { "no message", Planner.WithoutMessages(), "MessageNotRecorded" },
        { "two messages", Planner with { MessageCount = 2 }, "MessageNotRecorded" },
        { "message count never enumerated", Planner.WithUnenumeratedMessages(), "MessageNotRecorded" },
        { "message of another attempt", Planner.EditEach(message => message with { AttemptId = Other }), "MessageNotRecorded" },
        { "not a proposal message", Planner.EditEach(message => message with { Type = "Challenge" }), "ProposalNotRecorded" },
        { "proposal replying to something", Planner.EditEach(message => message with { InReplyToMessageId = Other }), "ProposalNotRecorded" },
        { "wrong author", Planner.EditEach(message => message with { Actor = "Claude.Planner" }), "ProposalAuthorMismatch" },
        { "not provider observed", Planner.EditEach(message => message with { Provenance = "HostConstructed" }), "ProposalAuthorMismatch" },
        { "unexpected inputs", Planner with { InputMessageIds = [Other] }, "UnexpectedInputs" },
        {
            "evidence route disagrees",
            Planner.EditEach(message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees }),
            "EvidenceRouteDisagrees"
        },
        {
            "evidence route never compared",
            Planner.EditEach(message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }),
            "EvidenceRouteDisagrees"
        },
        { "set not equal to the stored rows", Planner with { MessageSetComparison = EvidenceRouteComparisonStatus.Disagrees }, "MessageSetMismatch" },
        { "set never compared", Planner with { MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted }, "MessageSetMismatch" },
        {
            "stored row disagrees",
            Planner.EditEach(message => message with { PersistedMetadataComparison = EvidenceRouteComparisonStatus.Disagrees }),
            "MessageSetMismatch"
        },
        { "no manifest", Planner with { ManifestPresent = false }, "SealedInputMismatch" },
        { "manifest bytes differ", Planner with { ManifestBytesAgree = false }, "SealedInputMismatch" },
        { "manifest contract differs", Planner with { ManifestContractAgrees = false }, "SealedInputContent" },
        { "manifest objective differs", Planner with { ManifestObjectiveAgrees = false }, "SealedInputContent" },
        { "an artifact disagrees", Planner with { ArtifactsAgreeing = 3 }, "ArtifactMismatch" },
        { "no artifacts", Planner with { ArtifactCount = 0, ArtifactsAgreeing = 0 }, "ArtifactMismatch" },
    };

    [Theory]
    [MemberData(nameof(PlannerDefects))]
    public void A_planner_reading_with_any_single_defect_is_not_a_recorded_proposal(string defect, StageReading reading, string code)
    {
        Assert.False(string.IsNullOrEmpty(defect));
        Assert.Equal(code, StageVerifier.Verify(AllowanceRole.Planner, reading, null, Attempt));
    }

    public static TheoryData<string, StageReading, string> ReviewerDefects => new()
    {
        { "wrong provider", Reviewer() with { Provider = "Codex" }, "WrongRoleOrProvider" },
        { "planner attempt count", Reviewer() with { AttemptsInRun = 1 }, "UnexpectedAttemptCount" },
        { "third attempt", Reviewer() with { AttemptsInRun = 3 }, "UnexpectedAttemptCount" },
        { "failed", Reviewer() with { Status = "Failed", Outcome = "InvalidStructuredOutput" }, "AttemptFailedInvalidStructuredOutput" },
        { "not a verdict outcome", Reviewer() with { Outcome = "InputAlreadyReviewed" }, "AttemptCompletedInputAlreadyReviewed" },
        { "unclean exit", Reviewer() with { ExitCode = 2 }, "ProcessNotCleanExit" },
        { "status route disagrees", Reviewer() with { StatusRouteAgrees = false }, "StatusRouteDisagrees" },
        { "reviews another proposal", Reviewer() with { ReviewedProposalMessageId = Other }, "ReviewedProposalMismatch" },
        { "reviews no proposal", Reviewer() with { ReviewedProposalMessageId = null }, "ReviewedProposalMismatch" },
        { "reply of the wrong type", Reviewer().EditEach(message => message with { Type = "Acceptance" }), "ReplyNotRecorded" },
        { "acceptance recorded as challenge", Reviewer(accepted: true).EditEach(message => message with { Type = "Challenge" }), "ReplyNotRecorded" },
        { "reply to another message", Reviewer().EditEach(message => message with { InReplyToMessageId = Other }), "ReplyLineageMismatch" },
        { "reply to nothing", Reviewer().EditEach(message => message with { InReplyToMessageId = null }), "ReplyLineageMismatch" },
        { "reply by the wrong actor", Reviewer().EditEach(message => message with { Actor = "Codex.Planner" }), "ReplyAuthorMismatch" },
        { "reply not provider observed", Reviewer().EditEach(message => message with { Provenance = "Simulated" }), "ReplyAuthorMismatch" },
        { "input is another message", Reviewer() with { InputMessageIds = [Other] }, "InputSetMismatch" },
        { "extra input", Reviewer() with { InputMessageIds = [Proposal, Other] }, "InputSetMismatch" },
        { "no inputs", Reviewer() with { InputMessageIds = [] }, "InputSetMismatch" },
        { "manifest names another proposal", Reviewer() with { ManifestProposalMessageId = Other }, "SealedProposalMismatch" },
        { "manifest names no proposal", Reviewer() with { ManifestProposalMessageId = null }, "SealedProposalMismatch" },
        { "manifest bytes differ", Reviewer() with { ManifestBytesAgree = false }, "SealedInputMismatch" },
        {
            "evidence route disagrees",
            Reviewer().EditEach(message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees }),
            "EvidenceRouteDisagrees"
        },
        {
            "evidence route never compared",
            Reviewer().EditEach(message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }),
            "EvidenceRouteDisagrees"
        },
        {
            "no reply observed",
            Reviewer().WithoutMessages(),
            "MessageNotRecorded"
        },
        { "message count never enumerated", Reviewer().WithUnenumeratedMessages(), "MessageNotRecorded" },
    };

    [Theory]
    [MemberData(nameof(ReviewerDefects))]
    public void A_reviewer_reading_with_any_single_defect_is_not_a_reply_to_the_exact_proposal(string defect, StageReading reading, string code)
    {
        Assert.False(string.IsNullOrEmpty(defect));
        Assert.Equal(code, StageVerifier.Verify(AllowanceRole.Reviewer, reading, Proposal, Attempt));
    }

    [Fact]
    public void A_coherent_reading_of_an_attempt_other_than_the_accepted_one_is_foreign_for_both_roles()
    {
        var accepted = Guid.NewGuid();

        Assert.Equal("AttemptIdentityMismatch", StageVerifier.Verify(AllowanceRole.Planner, Planner, null, accepted));
        Assert.Equal("AttemptIdentityMismatch", StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(), Proposal, accepted));
    }

    [Theory]
    [InlineData(EvidenceRouteComparisonStatus.NotAttempted, false)]
    [InlineData(EvidenceRouteComparisonStatus.Agrees, true)]
    [InlineData(EvidenceRouteComparisonStatus.Disagrees, false)]
    public void The_stage_agreement_flag_is_derived_from_its_messages_comparison_state(EvidenceRouteComparisonStatus comparison, bool agrees)
    {
        Assert.Equal(agrees, Planner.EditEach(message => message with { EvidenceRouteComparison = comparison }).EvidenceRouteAgrees);
        Assert.Equal(agrees, Reviewer().EditEach(message => message with { EvidenceRouteComparison = comparison }).EvidenceRouteAgrees);
    }

    [Fact]
    public void An_unread_reading_has_no_message_count_and_no_comparison()
    {
        var unread = new StageReading();

        Assert.Null(unread.MessageCount);
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, unread.EvidenceRouteComparison);
        Assert.False(unread.EvidenceRouteAgrees);
    }

    [Fact]
    public void A_review_without_a_known_proposal_is_never_valid()
    {
        Assert.Equal("ReviewedProposalMismatch", StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(), null, Attempt));
    }

    private static StageReading Challenges(int size) => QualificationReadings.ChallengeSet(Attempt, Proposal, size);

    private static StageReading Verified(Func<StageReading, StageReading> corrupt) => corrupt(Challenges(3));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void A_complete_set_of_one_to_five_challenges_to_the_exact_proposal_is_valid(int size)
    {
        Assert.Null(StageVerifier.Verify(AllowanceRole.Reviewer, Challenges(size), Proposal, Attempt));
    }

    [Theory]
    [InlineData("Planner", "Proposed", 1)]
    [InlineData("Planner", "Failed", 0)]
    [InlineData("CriticalReviewer", "Accepted", 1)]
    [InlineData("CriticalReviewer", "Challenged", 5)]
    [InlineData("CriticalReviewer", "InputAlreadyReviewed", 0)]
    [InlineData("CriticalReviewer", null, 0)]
    [InlineData("Implementer", "Proposed", 0)]
    public void The_bound_on_messages_and_evidence_requests_follows_the_role_and_the_recorded_outcome(string role, string? outcome, int expected)
    {
        Assert.Equal(expected, StageMessageBounds.Maximum(role, outcome));
    }

    public static TheoryData<string, StageReading, string> ChallengeSetDefects => new()
    {
        { "six challenges", Challenges(6), "UnexpectedMessageCount" },
        { "twenty challenges, six listed", Challenges(6) with { MessageCount = 20 }, "UnexpectedMessageCount" },
        {
            "two acceptances",
            Reviewer(accepted: true) with
            {
                MessageCount = 2,
                Messages = [.. Reviewer(accepted: true).Messages, QualificationReadings.Reply(Attempt, Other, Proposal, accepted: true)],
            },
            "UnexpectedMessageCount"
        },
        { "listing shorter than the count", Challenges(3) with { MessageCount = 4 }, "MessageNotRecorded" },
        { "listing longer than the count", Challenges(3) with { MessageCount = 2 }, "MessageNotRecorded" },
        { "count never enumerated", Challenges(3).WithUnenumeratedMessages(), "MessageNotRecorded" },
        { "observed zero", Challenges(3).WithoutMessages(), "MessageNotRecorded" },
        { "a reply of another attempt", Verified(set => set.EditAt(1, message => message with { AttemptId = Other })), "MessageNotRecorded" },
        { "a reply of no attempt", Verified(set => set.EditAt(1, message => message with { AttemptId = null })), "MessageNotRecorded" },
        { "a repeated identity", Verified(set => set.EditAt(2, message => message with { Id = set.Messages[0].Id })), "DuplicateMessage" },
        { "the same identity twice and a second one", Verified(set => set.EditAt(1, message => message with { Id = set.Messages[0].Id })), "DuplicateMessage" },
        { "the timeline set differs from the stored rows", Challenges(3) with { MessageSetComparison = EvidenceRouteComparisonStatus.Disagrees }, "MessageSetMismatch" },
        { "the set was never compared", Challenges(3) with { MessageSetComparison = EvidenceRouteComparisonStatus.NotAttempted }, "MessageSetMismatch" },
        {
            "the stored row of the second reply disagrees",
            Verified(set => set.EditAt(1, message => message with { PersistedMetadataComparison = EvidenceRouteComparisonStatus.Disagrees })),
            "MessageSetMismatch"
        },
        { "an acceptance among the challenges", Verified(set => set.EditAt(1, message => message with { Type = "Acceptance" })), "MixedReplySet" },
        { "a foreign type among the challenges", Verified(set => set.EditAt(2, message => message with { Type = "Proposal" })), "MixedReplySet" },
        { "only acceptances beside a challenged outcome", Verified(set => set.EditEach(message => message with { Type = "Acceptance" })), "ReplyNotRecorded" },
        { "a missing type on the third", Verified(set => set.EditAt(2, message => message with { Type = null })), "MixedReplySet" },
        { "the second reply answers another message", Verified(set => set.EditAt(1, message => message with { InReplyToMessageId = Other })), "ReplyLineageMismatch" },
        { "the third reply answers nothing", Verified(set => set.EditAt(2, message => message with { InReplyToMessageId = null })), "ReplyLineageMismatch" },
        { "the second reply has the wrong author", Verified(set => set.EditAt(1, message => message with { Actor = "Codex.Planner" })), "ReplyAuthorMismatch" },
        { "the third reply is not provider observed", Verified(set => set.EditAt(2, message => message with { Provenance = "HostConstructed" })), "ReplyAuthorMismatch" },
        {
            "the evidence of the second reply disagrees",
            Verified(set => set.EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees })),
            "EvidenceRouteDisagrees"
        },
        {
            "the evidence of the last reply was never requested",
            Verified(set => set.EditAt(2, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted })),
            "EvidenceRouteDisagrees"
        },
        {
            "the evidence request of the second reply failed, the rest were never made",
            Verified(set => set
                .EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Unavailable })
                .EditAt(2, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted })),
            "EvidenceRouteUnavailable"
        },
        {
            "one reply disagrees and a later request failed",
            Verified(set => set
                .EditAt(0, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees })
                .EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Unavailable })),
            "EvidenceRouteDisagrees"
        },
        {
            "an observed empty timeline beside stored replies",
            Challenges(3).WithoutMessages() with { MessageSetComparison = EvidenceRouteComparisonStatus.Disagrees },
            "MessageNotRecorded"
        },
        { "the recorded inputs name another message", Verified(set => set with { InputMessageIds = [Other] }), "InputSetMismatch" },
        { "the sealed input names another proposal", Verified(set => set with { ManifestProposalMessageId = Other }), "SealedProposalMismatch" },
    };

    [Theory]
    [MemberData(nameof(ChallengeSetDefects))]
    public void A_challenge_set_with_any_single_defect_is_not_a_valid_review(string defect, StageReading reading, string code)
    {
        Assert.False(string.IsNullOrEmpty(defect));
        Assert.Equal(code, StageVerifier.Verify(AllowanceRole.Reviewer, reading, Proposal, Attempt));
    }

    [Fact]
    public void A_set_of_several_replies_is_never_valid_for_the_planner_or_an_acceptance()
    {
        var two = Planner with { MessageCount = 2, Messages = [.. Planner.Messages, QualificationReadings.Reply(Attempt, Other, Proposal, accepted: false)] };

        Assert.Equal("MessageNotRecorded", StageVerifier.Verify(AllowanceRole.Planner, two, null, Attempt));
    }

    [Fact]
    public void The_stage_comparison_is_agreement_only_when_every_listed_message_agrees_and_disagreement_when_any_does_not()
    {
        var set = Challenges(3);

        Assert.Equal(EvidenceRouteComparisonStatus.Agrees, set.EvidenceRouteComparison);
        Assert.Equal(
            EvidenceRouteComparisonStatus.Disagrees,
            set.EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees }).EvidenceRouteComparison);
        Assert.Equal(
            EvidenceRouteComparisonStatus.NotAttempted,
            set.EditAt(2, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }).EvidenceRouteComparison);
        Assert.Equal(EvidenceRouteComparisonStatus.NotAttempted, set.WithoutMessages().EvidenceRouteComparison);
        Assert.Equal(
            EvidenceRouteComparisonStatus.Unavailable,
            set.EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Unavailable }).EvidenceRouteComparison);
    }
}
