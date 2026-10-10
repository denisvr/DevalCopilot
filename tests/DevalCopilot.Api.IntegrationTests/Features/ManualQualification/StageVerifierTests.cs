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
        { "no message", Planner with { MessageCount = 0, MessageId = null }, "MessageNotRecorded" },
        { "two messages", Planner with { MessageCount = 2 }, "MessageNotRecorded" },
        { "message of another attempt", Planner with { MessageAttemptId = Other }, "MessageNotRecorded" },
        { "not a proposal message", Planner with { MessageType = "Challenge" }, "ProposalNotRecorded" },
        { "proposal replying to something", Planner with { InReplyToMessageId = Other }, "ProposalNotRecorded" },
        { "wrong author", Planner with { MessageActor = "Claude.Planner" }, "ProposalAuthorMismatch" },
        { "not provider observed", Planner with { MessageProvenance = "HostConstructed" }, "ProposalAuthorMismatch" },
        { "unexpected inputs", Planner with { InputMessageIds = [Other] }, "UnexpectedInputs" },
        { "evidence route disagrees", Planner with { EvidenceRouteAgrees = false }, "EvidenceRouteDisagrees" },
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
        { "reply of the wrong type", Reviewer() with { MessageType = "Acceptance" }, "ReplyNotRecorded" },
        { "acceptance recorded as challenge", Reviewer(accepted: true) with { MessageType = "Challenge" }, "ReplyNotRecorded" },
        { "reply to another message", Reviewer() with { InReplyToMessageId = Other }, "ReplyLineageMismatch" },
        { "reply to nothing", Reviewer() with { InReplyToMessageId = null }, "ReplyLineageMismatch" },
        { "reply by the wrong actor", Reviewer() with { MessageActor = "Codex.Planner" }, "ReplyAuthorMismatch" },
        { "reply not provider observed", Reviewer() with { MessageProvenance = "Simulated" }, "ReplyAuthorMismatch" },
        { "input is another message", Reviewer() with { InputMessageIds = [Other] }, "InputSetMismatch" },
        { "extra input", Reviewer() with { InputMessageIds = [Proposal, Other] }, "InputSetMismatch" },
        { "no inputs", Reviewer() with { InputMessageIds = [] }, "InputSetMismatch" },
        { "manifest names another proposal", Reviewer() with { ManifestProposalMessageId = Other }, "SealedProposalMismatch" },
        { "manifest names no proposal", Reviewer() with { ManifestProposalMessageId = null }, "SealedProposalMismatch" },
        { "manifest bytes differ", Reviewer() with { ManifestBytesAgree = false }, "SealedInputMismatch" },
        { "evidence route disagrees", Reviewer() with { EvidenceRouteAgrees = false }, "EvidenceRouteDisagrees" },
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

    [Fact]
    public void A_review_without_a_known_proposal_is_never_valid()
    {
        Assert.Equal("ReviewedProposalMismatch", StageVerifier.Verify(AllowanceRole.Reviewer, Reviewer(), null, Attempt));
    }
}
