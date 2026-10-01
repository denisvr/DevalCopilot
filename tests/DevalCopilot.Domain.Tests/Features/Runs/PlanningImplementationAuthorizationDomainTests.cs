using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class PlanningImplementationAuthorizationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static PlanningImplementationAuthorization Grant() => PlanningImplementationAuthorization.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), Now);

    [Fact]
    public void A_grant_binds_run_escalation_proposal_workspace_checkpoint_fingerprint_and_instruction_and_starts_available()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var escalationId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var instructionId = Guid.NewGuid();

        var grant = PlanningImplementationAuthorization.Create(
            id, runId, escalationId, proposalId, workspaceId, checkpointId, Fingerprint, instructionId, Now);

        Assert.Equal(
            (id, runId, escalationId, proposalId, workspaceId, checkpointId, Fingerprint, instructionId, Now),
            (grant.Id, grant.RunId, grant.EscalationMessageId, grant.FinalProposalMessageId, grant.WorkspaceId, grant.CheckpointId,
                grant.FingerprintSha256, grant.HumanInstructionMessageId, grant.CreatedAtUtc));
        Assert.True(grant.IsAvailable);
        Assert.Null(grant.ConsumedByAttemptId);
        Assert.Null(grant.ConsumedAtUtc);
    }

    [Fact]
    public void Every_identifier_and_the_fingerprint_and_time_are_required()
    {
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++)
        {
            var broken = ids.ToArray();
            broken[index] = Guid.Empty;
            Assert.Throws<ArgumentException>(() => PlanningImplementationAuthorization.Create(
                broken[0], broken[1], broken[2], broken[3], broken[4], broken[5], Fingerprint, broken[6], Now));
        }

        Assert.Throws<ArgumentException>(() => PlanningImplementationAuthorization.Create(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], " ", ids[6], Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanningImplementationAuthorization.Create(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], Fingerprint, ids[6], default));
    }

    [Fact]
    public void A_grant_is_consumed_exactly_once_and_never_before_it_was_created()
    {
        var grant = Grant();
        var attemptId = Guid.NewGuid();

        Assert.Throws<ArgumentOutOfRangeException>(() => grant.Consume(attemptId, Now.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => grant.Consume(Guid.Empty, Now));
        grant.Consume(attemptId, Now.AddSeconds(5));

        Assert.False(grant.IsAvailable);
        Assert.Equal(attemptId, grant.ConsumedByAttemptId);
        Assert.Equal(Now.AddSeconds(5), grant.ConsumedAtUtc);
        Assert.Throws<InvalidOperationException>(() => grant.Consume(Guid.NewGuid(), Now.AddSeconds(6)));
        Assert.Equal(attemptId, grant.ConsumedByAttemptId);
    }

    [Fact]
    public void The_instruction_content_is_the_fixed_instruction_and_the_normalized_rationale_only()
    {
        var json = PlanningImplementationInstruction.BuildStructuredContentJson("Reviewed and accepted.");

        Assert.Equal(
            "{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\",\"rationale\":\"Reviewed and accepted.\"}",
            json);
        Assert.Equal("Reviewed and accepted.", PlanningImplementationInstruction.TryReadRationale(json));
        Assert.NotEqual(ReviewCorrectionGuidance.FixedInstruction, PlanningImplementationInstruction.FixedInstruction);
    }

    [Theory]
    [InlineData("{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"Fine.\"}")]
    [InlineData("{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\",\"rationale\":\"  padded \"}")]
    [InlineData("{\"rationale\":\"Fine.\",\"instruction\":\"Authorize one implementation claim for the final escalated plan.\"}")]
    [InlineData("{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\",\"rationale\":\"Fine.\",\"extra\":1}")]
    [InlineData("{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\",\"rationale\":\"\"}")]
    [InlineData("{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\",\"rationale\":5}")]
    [InlineData("{\"instruction\":\"Authorize one implementation claim for the final escalated plan.\", \"rationale\":\"Fine.\"}")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Anything_but_the_canonical_form_is_never_read(string json)
    {
        Assert.Null(PlanningImplementationInstruction.TryReadRationale(json));
    }

    [Fact]
    public void The_review_correction_authorization_content_is_never_accepted_as_a_planning_authorization_and_vice_versa()
    {
        var review = ReviewCorrectionGuidance.BuildStructuredContentJson("Fine.");
        var planning = PlanningImplementationInstruction.BuildStructuredContentJson("Fine.");

        Assert.Null(PlanningImplementationInstruction.TryReadRationale(review));
        Assert.Null(ReviewCorrectionGuidance.TryReadRationale(planning));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001control")]
    [InlineData("Contains a password here")]
    public void Unsafe_or_blank_rationales_are_not_normalized(string? raw)
    {
        Assert.Null(PlanningImplementationInstruction.Normalize(raw));
    }

    [Fact]
    public void The_rationale_policy_matches_the_other_guidance_bound_of_six_hundred_code_units()
    {
        Assert.Equal(600, PlanningImplementationInstruction.MaximumLength);
        Assert.NotNull(PlanningImplementationInstruction.Normalize(new string('x', 600)));
        Assert.Null(PlanningImplementationInstruction.Normalize(new string('x', 601)));
        Assert.Equal("Café", PlanningImplementationInstruction.Normalize("  Café  "));
        Assert.Null(PlanningImplementationInstruction.Normalize("lone\ud800surrogate"));
    }

    [Fact]
    public void The_authorization_message_is_a_human_submitted_attemptless_instruction_replying_to_the_escalation()
    {
        var escalationId = Guid.NewGuid();
        var message = CollaborationMessage.RecordPlanningImplementationAuthorization(
            Guid.NewGuid(), Guid.NewGuid(), escalationId, PlanningImplementationInstruction.BuildStructuredContentJson("Fine."), Now);

        Assert.Equal(CollaborationMessageType.HumanInstruction, message.Type);
        Assert.Equal(CollaborationMessageProvenance.HumanSubmitted, message.Provenance);
        Assert.Null(message.AttemptId);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Actor);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), message.Recipient);
        Assert.Equal(escalationId, message.InReplyToMessageId);
        Assert.Equal(PlanningImplementationInstruction.Summary, message.Summary);
    }

    [Fact]
    public void The_existing_review_correction_instruction_factory_is_unchanged()
    {
        var message = CollaborationMessage.RecordHumanInstruction(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReviewCorrectionGuidance.BuildStructuredContentJson("Fine."), Now);

        Assert.Equal("Human authorized one additional review-correction attempt.", message.Summary);
    }
}
