namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputs;

/// <summary>One stored collaboration message associated with the attempt, as a reference only: never the message's text.</summary>
/// <param name="Position">The zero-based ordinal in this read's sequence order; it is not a persisted sequence.</param>
/// <param name="CollaborationMessageId">The associated message of the same run.</param>
/// <param name="Type">The message's recognized type name.</param>
/// <param name="CollaborationSequence">The message's exact, positive timeline sequence as a canonical decimal string, so a client never
/// rounds it to a floating-point number.</param>
/// <param name="OccurredAtUtc">The message's recorded time as an invariant UTC round-trip string (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>),
/// so a client validates the text itself before any date conversion.</param>
public sealed record AgentAttemptOutputReferenceResponse(
    int Position, Guid CollaborationMessageId, string Type, string CollaborationSequence, string OccurredAtUtc);
