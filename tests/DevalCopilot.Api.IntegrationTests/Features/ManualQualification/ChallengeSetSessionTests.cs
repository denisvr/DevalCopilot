using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// One CriticalReviewer attempt may record one Acceptance or a complete set of one to five Challenges, and the session accepts or
/// rejects exactly that set through the whole launcher sequence: always one invocation per role, never a repeat, and a summary that
/// carries each message's own comparison. The observed v3 run completed as Challenged with two timeline messages; these tests pin
/// that the set, and not a single message, is what is verified.
/// </summary>
public sealed class ChallengeSetSessionTests : QualificationTestBase
{
    private static ScriptedEnvironment Reviewing(Func<ScriptedEnvironment, StageReading> reviewer)
    {
        var environment = new ScriptedEnvironment();
        environment.OnRead = (role, _) => role == AllowanceRole.Planner
            ? QualificationReadings.Planner(environment.PlannerAttemptId, environment.ProposalMessageId)
            : reviewer(environment);
        return environment;
    }

    private static StageReading Set(ScriptedEnvironment environment, int size) =>
        QualificationReadings.ChallengeSet(environment.ReviewerAttemptId, environment.ProposalMessageId, size);

    private static void AssertOneInvocationPerRole(ScriptedEnvironment environment)
    {
        Assert.Equal(1, environment.Count("submit.Planner"));
        Assert.Equal(1, environment.Count("submit.Reviewer"));
    }

    [Fact]
    public async Task A_single_acceptance_qualifies_with_the_acceptance_verdict()
    {
        var environment = Reviewing(scripted => QualificationReadings.Reviewer(
            scripted.ReviewerAttemptId, scripted.ReplyMessageId, scripted.ProposalMessageId, accepted: true));

        var report = await RunAsync(environment);

        Assert.True(report.Qualified, report.Code);
        Assert.Equal("Acceptance", report.Verdict);
        AssertOneInvocationPerRole(environment);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task A_complete_set_of_challenges_qualifies_with_the_challenge_verdict_and_one_invocation_per_role(int size)
    {
        var environment = Reviewing(scripted => Set(scripted, size));

        var report = await RunAsync(environment);

        Assert.True(report.Qualified, report.Code);
        Assert.Equal("Challenge", report.Verdict);
        Assert.Equal(size, report.Reviewer!.MessageCount);
        Assert.Equal(size, report.Reviewer.Messages.Count);
        AssertOneInvocationPerRole(environment);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.Equal(size, reviewer.GetProperty("messageCount").GetInt32());
        var messages = reviewer.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(size, messages.Length);
        Assert.All(messages, message =>
        {
            Assert.Equal("Challenge", message.GetProperty("type").GetString());
            Assert.Equal(environment.ProposalMessageId, message.GetProperty("inReplyTo").GetGuid());
            Assert.Equal("ClaudeCode.CriticalReviewer", message.GetProperty("actor").GetString());
            Assert.Equal("Agrees", message.GetProperty("evidenceRouteComparison").GetString());
            Assert.Equal("Agrees", message.GetProperty("persistedMetadataComparison").GetString());
            Assert.True(message.GetProperty("belongsToAcceptedAttempt").GetBoolean());
        });
        Assert.Equal(size, messages.Select(message => message.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.True(reviewer.GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());
        Assert.Equal("Agrees", reviewer.GetProperty("messageSetComparison").GetString());
    }

    public static TheoryData<string, Func<StageReading, StageReading>, string> InvalidSets => new()
    {
        { "six challenges", set => set with { MessageCount = 6, Messages = [.. set.Messages, set.Messages[0] with { Id = Guid.NewGuid() }] }, "ReviewerUnexpectedMessageCount" },
        {
            "twenty challenges, only the first five compared",
            set => set with
            {
                MessageCount = 20,
                Messages = [.. set.Messages, set.Messages[0] with { Id = Guid.NewGuid(), EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }],
            },
            "ReviewerUnexpectedMessageCount"
        },
        { "no message", set => set.WithoutMessages(), "ReviewerMessageNotRecorded" },
        { "an unobserved count", set => set.WithUnenumeratedMessages(), "ReviewerMessageNotRecorded" },
        { "a repeated identity", set => set.EditAt(2, message => message with { Id = set.Messages[0].Id }), "ReviewerDuplicateMessage" },
        { "the stored rows differ", set => set with { MessageSetComparison = EvidenceRouteComparisonStatus.Disagrees }, "ReviewerMessageSetMismatch" },
        { "an acceptance among the challenges", set => set.EditAt(2, message => message with { Type = "Acceptance" }), "ReviewerMixedReplySet" },
        { "a foreign reply", set => set.EditAt(1, message => message with { AttemptId = Guid.NewGuid() }), "ReviewerMessageNotRecorded" },
        { "the second reply answers another message", set => set.EditAt(1, message => message with { InReplyToMessageId = Guid.NewGuid() }), "ReviewerReplyLineageMismatch" },
        { "the third reply has another author", set => set.EditAt(2, message => message with { Actor = "Codex.Planner" }), "ReviewerReplyAuthorMismatch" },
        {
            "the evidence of the second reply disagrees",
            set => set.EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees }),
            "ReviewerEvidenceRouteDisagrees"
        },
        {
            "the evidence of the last reply was never requested",
            set => set.EditAt(2, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }),
            "ReviewerEvidenceRouteDisagrees"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidSets))]
    public async Task An_invalid_set_fails_the_review_and_never_starts_a_second_invocation(
        string defect, Func<StageReading, StageReading> corrupt, string code)
    {
        Assert.False(string.IsNullOrEmpty(defect));
        var environment = Reviewing(scripted => corrupt(Set(scripted, 3)));

        var report = await RunAsync(environment);

        Assert.Equal(QualificationResult.Failed, report.Result);
        Assert.Equal(code, report.Code);
        Assert.Null(report.Verdict);
        AssertOneInvocationPerRole(environment);
    }

    [Fact]
    public async Task A_disagreeing_comparison_on_a_reply_after_the_first_is_reported_with_that_reply_and_never_hidden_by_the_first()
    {
        var environment = Reviewing(scripted => Set(scripted, 2).EditAt(
            1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Disagrees }));

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerEvidenceRouteDisagrees", report.Code);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        var messages = reviewer.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("Agrees", messages[0].GetProperty("evidenceRouteComparison").GetString());
        Assert.Equal("Disagrees", messages[1].GetProperty("evidenceRouteComparison").GetString());
        Assert.Equal("Disagrees", reviewer.GetProperty("evidenceRouteComparison").GetString());
        Assert.False(reviewer.GetProperty("evidenceRouteAgrees").GetBoolean());
    }

    [Fact]
    public async Task An_excess_count_is_reported_in_full_with_a_bounded_list_and_an_explicit_truncation()
    {
        var environment = Reviewing(scripted => Set(scripted, 5) with
        {
            MessageCount = 20,
            Messages = [.. Set(scripted, 5).Messages, Set(scripted, 1).Messages[0] with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }],
        });

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerUnexpectedMessageCount", report.Code);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.Equal(20, reviewer.GetProperty("messageCount").GetInt32());
        Assert.Equal(StageMessageBounds.MaximumListed, reviewer.GetProperty("messages").GetArrayLength());
        Assert.True(reviewer.GetProperty("messagesTruncated").GetBoolean());
        // Every listed reply answers the Proposal, yet the listing is a truncated prefix, so no claim is made about the whole set.
        Assert.False(reviewer.GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());
        Assert.Equal(
            "NotAttempted",
            reviewer.GetProperty("messages")[StageMessageBounds.MaximumListed - 1].GetProperty("evidenceRouteComparison").GetString());
    }

    [Fact]
    public async Task A_truncated_listing_whose_every_reply_answers_the_proposal_makes_no_whole_set_lineage_claim()
    {
        var environment = Reviewing(scripted => Set(scripted, 5) with
        {
            MessageCount = 7,
            Messages = [.. Set(scripted, 5).Messages, Set(scripted, 1).Messages[0] with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }],
        });

        var report = await RunAsync(environment);

        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.True(reviewer.GetProperty("messagesTruncated").GetBoolean());
        Assert.All(
            reviewer.GetProperty("messages").EnumerateArray(),
            message => Assert.Equal(environment.ProposalMessageId, message.GetProperty("inReplyTo").GetGuid()));
        Assert.False(reviewer.GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());

        // The same listing without truncation does support the claim.
        var complete = Reviewing(scripted => Set(scripted, 5));
        var qualified = await new QualificationSession(
            new InvocationLedger(Path.Combine(LedgerDirectory, "complete"), TimeProvider.System), complete, Tiny, "complete-listing")
            .RunAsync(CancellationToken.None);
        using var qualifiedSummary = JsonDocument.Parse(SafeSummary.Render(qualified, []));
        Assert.True(qualifiedSummary.RootElement.GetProperty("reviewer").GetProperty("lineage")
            .GetProperty("repliesToThePersistedProposal").GetBoolean());
    }

    [Fact]
    public async Task A_foreign_observed_attempt_is_never_reported_as_belonging_to_the_accepted_one()
    {
        var foreign = Guid.NewGuid();
        var environment = Reviewing(scripted => QualificationReadings.ChallengeSet(foreign, scripted.ProposalMessageId, 2));

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerAttemptIdentityMismatch", report.Code);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.False(reviewer.GetProperty("attemptIdMatchesAccepted").GetBoolean());
        Assert.Equal(foreign, reviewer.GetProperty("attemptId").GetGuid());
        // Each message names the attempt the stage observed, and none of them names the accepted one.
        Assert.All(
            reviewer.GetProperty("messages").EnumerateArray(),
            message => Assert.False(message.GetProperty("belongsToAcceptedAttempt").GetBoolean()));
    }

    [Fact]
    public async Task An_observed_empty_timeline_beside_stored_replies_is_reported_as_a_disagreement_and_never_as_not_attempted()
    {
        var disagreeing = Reviewing(scripted => Set(scripted, 2).WithoutMessages() with
        {
            MessageSetComparison = EvidenceRouteComparisonStatus.Disagrees,
        });
        var unobserved = Reviewing(scripted => Set(scripted, 2).WithUnenumeratedMessages());

        var first = await RunAsync(disagreeing, "empty-beside-stored");
        using var disagreement = JsonDocument.Parse(SafeSummary.Render(first, []));
        var second = await new QualificationSession(
            new InvocationLedger(Path.Combine(LedgerDirectory, "unobserved"), TimeProvider.System), unobserved, Tiny, "unobserved")
            .RunAsync(CancellationToken.None);
        using var never = JsonDocument.Parse(SafeSummary.Render(second, []));

        Assert.Equal("ReviewerMessageNotRecorded", first.Code);
        var stage = disagreement.RootElement.GetProperty("reviewer");
        Assert.Equal(0, stage.GetProperty("messageCount").GetInt32());
        Assert.Equal("Disagrees", stage.GetProperty("messageSetComparison").GetString());
        var neverStage = never.RootElement.GetProperty("reviewer");
        Assert.Equal(JsonValueKind.Null, neverStage.GetProperty("messageCount").ValueKind);
        Assert.Equal("NotAttempted", neverStage.GetProperty("messageSetComparison").GetString());
    }

    [Fact]
    public async Task A_failed_evidence_request_is_reported_as_unavailable_with_the_later_replies_never_attempted()
    {
        var environment = Reviewing(scripted => Set(scripted, 3)
            .EditAt(1, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.Unavailable })
            .EditAt(2, message => message with { EvidenceRouteComparison = EvidenceRouteComparisonStatus.NotAttempted }));

        var report = await RunAsync(environment);

        Assert.Equal("ReviewerEvidenceRouteUnavailable", report.Code);
        using var summary = JsonDocument.Parse(SafeSummary.Render(report, []));
        var reviewer = summary.RootElement.GetProperty("reviewer");
        Assert.Equal(
            ["Agrees", "Unavailable", "NotAttempted"],
            reviewer.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("evidenceRouteComparison").GetString()));
        Assert.Equal("Unavailable", reviewer.GetProperty("evidenceRouteComparison").GetString());
        AssertOneInvocationPerRole(environment);
    }

    [Fact]
    public async Task A_count_never_observed_is_null_and_an_observed_zero_is_an_empty_list_in_the_closed_summary()
    {
        var unobserved = Reviewing(scripted => Set(scripted, 2).WithUnenumeratedMessages());
        var observedZero = Reviewing(scripted => Set(scripted, 2).WithoutMessages());

        var first = await RunAsync(unobserved, "unobserved");
        using var firstSummary = JsonDocument.Parse(SafeSummary.Render(first, []));
        var neverEnumerated = firstSummary.RootElement.GetProperty("reviewer");

        Assert.Equal(JsonValueKind.Null, neverEnumerated.GetProperty("messageCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, neverEnumerated.GetProperty("messages").ValueKind);
        Assert.Equal(JsonValueKind.Null, neverEnumerated.GetProperty("messagesTruncated").ValueKind);

        var second = await new QualificationSession(
            new InvocationLedger(Path.Combine(LedgerDirectory, "second"), TimeProvider.System), observedZero, Tiny, "observed-zero")
            .RunAsync(CancellationToken.None);
        using var secondSummary = JsonDocument.Parse(SafeSummary.Render(second, []));
        var zero = secondSummary.RootElement.GetProperty("reviewer");

        Assert.Equal(0, zero.GetProperty("messageCount").GetInt32());
        Assert.Equal(0, zero.GetProperty("messages").GetArrayLength());
        Assert.False(zero.GetProperty("messagesTruncated").GetBoolean());
    }
}
