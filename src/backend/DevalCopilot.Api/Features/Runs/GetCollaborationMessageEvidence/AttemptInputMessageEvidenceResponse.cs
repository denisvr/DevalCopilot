namespace DevalCopilot.Api.Features.Runs.GetCollaborationMessageEvidence;

/// <summary>
/// One recorded collaboration input the producing Agent attempt was launched against — never the
/// complete prompt, complete context manifest, or a resumable provider session, only a durable
/// reference. <c>Type</c> crosses the wire as a plain string (this repo's established convention —
/// zero generated TypeScript <c>enum</c>). Carries no message content of its own; a caller
/// cross-references <c>CollaborationMessageId</c> against its own already-loaded timeline to show
/// that message's summary, and must never assume presence there merely because this reference
/// exists.
/// </summary>
public sealed record AttemptInputMessageEvidenceResponse(
    int Sequence,
    Guid CollaborationMessageId,
    string Type,
    long CollaborationMessageSequence,
    DateTimeOffset OccurredAtUtc);
