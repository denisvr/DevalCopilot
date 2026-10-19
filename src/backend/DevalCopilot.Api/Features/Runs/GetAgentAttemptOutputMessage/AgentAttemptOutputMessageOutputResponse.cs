namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputMessage;

/// <summary>The selected output of the proved set, with the meanings of the reference inspection (ADR-0054).</summary>
/// <param name="Position">The zero-based ordinal in the proved set's sequence order; it is not a persisted sequence.</param>
/// <param name="CollaborationMessageId">The associated message of the same run.</param>
/// <param name="Type">The message's recognized type name.</param>
/// <param name="CollaborationSequence">The message's exact, positive timeline sequence as a canonical decimal string.</param>
/// <param name="OccurredAtUtc">The message's recorded time as an invariant UTC round-trip string
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>).</param>
public sealed record AgentAttemptOutputMessageOutputResponse(
    int Position, Guid CollaborationMessageId, string Type, string CollaborationSequence, string OccurredAtUtc);
