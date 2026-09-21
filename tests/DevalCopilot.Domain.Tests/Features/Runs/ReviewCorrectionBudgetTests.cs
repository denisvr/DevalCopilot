using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ReviewCorrectionBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Run_defaults_to_two_positive_review_correction_attempts()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Bound correction", Now);

        Assert.Equal(2, run.MaximumReviewCorrectionAttempts);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Invalid", Now, 0));
    }

    [Fact]
    public void Authorization_is_one_way_and_rejects_invalid_consumption()
    {
        var authorization = ReviewCorrectionAuthorization.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var attemptId = Guid.NewGuid();

        authorization.Consume(attemptId, Now.AddMinutes(1));

        Assert.False(authorization.IsAvailable);
        Assert.Equal(attemptId, authorization.ConsumedByAttemptId);
        Assert.Throws<InvalidOperationException>(() => authorization.Consume(Guid.NewGuid(), Now.AddMinutes(2)));
        var earlierAuthorization = ReviewCorrectionAuthorization.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => earlierAuthorization.Consume(Guid.NewGuid(), Now.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() =>
            ReviewCorrectionAuthorization.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now));
    }

    [Fact]
    public void Human_instruction_is_fixed_human_submitted_and_replies_to_escalation()
    {
        var escalationId = Guid.NewGuid();
        var instruction = CollaborationMessage.RecordHumanInstruction(
            Guid.NewGuid(), Guid.NewGuid(), escalationId,
            JsonSerializer.Serialize(new { instruction = "Authorize one additional review-correction attempt.", rationale = "Continue only after explicit human authorization." }),
            Now);

        Assert.Equal(CollaborationMessageType.HumanInstruction, instruction.Type);
        Assert.Equal(CollaborationMessageProvenance.HumanSubmitted, instruction.Provenance);
        Assert.Equal(ParticipantKind.Human, instruction.Actor.Kind);
        Assert.Equal(ParticipantKind.Orchestrator, instruction.Recipient.Kind);
        Assert.Equal(escalationId, instruction.InReplyToMessageId);
    }

    [Fact]
    public void Agent_role_policy_does_not_authorize_human_instruction()
    {
        Assert.DoesNotContain(
            CollaborationMessageType.HumanInstruction,
            CollaborationMessageAuthorPolicy.AllowedMessageTypes(AgentRole.Implementer));
        Assert.Throws<ArgumentException>(() => CollaborationMessage.Record(
            Guid.NewGuid(), Guid.NewGuid(), null, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForHuman(), CollaborationMessageType.HumanInstruction,
            Guid.NewGuid(), "Human instruction.",
            "{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"Continue only after explicit human authorization.\"}",
            CollaborationMessageProvenance.ProviderObserved, Now));
    }
}
