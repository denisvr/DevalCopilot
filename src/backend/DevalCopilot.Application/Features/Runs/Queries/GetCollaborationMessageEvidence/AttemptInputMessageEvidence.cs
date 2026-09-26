using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

/// <param name="Sequence">The attempt's own zero-based ordered position for this input — matches
/// <see cref="AttemptInputMessage.Sequence"/> exactly, never re-sorted or re-numbered.</param>
/// <param name="CollaborationMessageId">The durable id of the referenced, same-run collaboration
/// message. Carries no content of its own; a caller cross-references it against its own already-
/// loaded timeline to show that message's summary, and must never assume presence there.</param>
/// <param name="Type">The referenced message's own <see cref="CollaborationMessageType"/> — safe,
/// already-public metadata, resolved from this run's own durable ledger independent of whether the
/// message happens to be in a caller's currently loaded timeline window.</param>
/// <param name="CollaborationMessageSequence">The referenced message's own monotonic timeline
/// <see cref="CollaborationMessage.Sequence"/>, so a caller can reason about its position without
/// re-deriving it.</param>
/// <param name="OccurredAtUtc">The referenced message's own recorded time.</param>
public sealed record AttemptInputMessageEvidence(
    int Sequence,
    Guid CollaborationMessageId,
    CollaborationMessageType Type,
    long CollaborationMessageSequence,
    DateTimeOffset OccurredAtUtc);
