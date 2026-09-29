using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The single validation of the persisted authorization chain, used both by an idempotent
/// authorization retry and by a correction claim before it seals anything. The authorization, its
/// escalation, the escalation's own message, and the HumanInstruction must all belong to the same run
/// and escalation with their expected envelopes:
/// <list type="bullet">
/// <item>the escalation message is a host-constructed, attemptless protocol-1.0 <c>Escalation</c> from the
/// Orchestrator to the Human;</item>
/// <item>the HumanInstruction is a human-submitted, attemptless protocol-1.0 message from the Human to the
/// Orchestrator replying to exactly that escalation message;</item>
/// <item>its content is the canonical form <see cref="ReviewCorrectionGuidance.TryReadRationale"/> accepts.</item>
/// </list>
/// Any deviation fails closed with a fixed error that never echoes persisted text.
/// </summary>
internal static class ReviewCorrectionAuthorizationInstruction
{
    public const string InvalidCode = "review_correction_authorizations.instruction_invalid";

    /// <summary>Exact accepted guidance and the HumanInstruction message it came from.</summary>
    internal sealed record HumanGuidance(Guid MessageId, string Text);

    /// <summary>On success, <see cref="Rationale"/> is the validated stored rationale (the reserved
    /// default for a bodyless authorization) and <see cref="Guidance"/> is <see langword="null"/> for a
    /// bodyless authorization. On failure, only <see cref="Error"/> is set.</summary>
    internal sealed record Resolution(Error? Error, HumanGuidance? Guidance, string? Rationale);

    public static async Task<Resolution> ResolveAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        ReviewCorrectionEscalation escalation,
        ReviewCorrectionAuthorization authorization,
        CancellationToken cancellationToken)
    {
        if (authorization.RunId != runId
            || escalation.RunId != runId
            || authorization.EscalationId != escalation.Id)
        {
            return Invalid();
        }

        var escalationMessage = await dbContext.CollaborationMessages.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == escalation.CollaborationMessageId && candidate.RunId == runId,
            cancellationToken);
        if (escalationMessage is null
            || escalationMessage.Type != CollaborationMessageType.Escalation
            || escalationMessage.Provenance != CollaborationMessageProvenance.HostConstructed
            || escalationMessage.AttemptId is not null
            || escalationMessage.Actor != ParticipantIdentity.ForOrchestrator()
            || escalationMessage.Recipient != ParticipantIdentity.ForHuman()
            || !string.Equals(escalationMessage.ProtocolVersion, CollaborationMessage.ProtocolVersionOne, StringComparison.Ordinal))
        {
            return Invalid();
        }

        var message = await dbContext.CollaborationMessages.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == authorization.HumanInstructionMessageId && candidate.RunId == runId,
            cancellationToken);
        if (message is null
            || message.Type != CollaborationMessageType.HumanInstruction
            || message.Provenance != CollaborationMessageProvenance.HumanSubmitted
            || message.AttemptId is not null
            || message.Actor != ParticipantIdentity.ForHuman()
            || message.Recipient != ParticipantIdentity.ForOrchestrator()
            || message.InReplyToMessageId != escalation.CollaborationMessageId
            || !string.Equals(message.ProtocolVersion, CollaborationMessage.ProtocolVersionOne, StringComparison.Ordinal))
        {
            return Invalid();
        }

        var rationale = ReviewCorrectionGuidance.TryReadRationale(message.StructuredContentJson);
        if (rationale is null)
        {
            return Invalid();
        }

        return new Resolution(
            null,
            ReviewCorrectionGuidance.IsGuidance(rationale) ? new HumanGuidance(message.Id, rationale) : null,
            rationale);
    }

    private static Resolution Invalid() =>
        new(Error.Conflict(InvalidCode, "The persisted authorization instruction could not be validated."), null, null);
}
