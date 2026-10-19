namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputMessage;

/// <summary>The selected input of the proved set, with the meanings of the reference inspection (ADR-0051).</summary>
/// <param name="Sequence">The zero-based recorded input position.</param>
/// <param name="CollaborationMessageId">The referenced message of the same run.</param>
/// <param name="Type">The referenced message's recognized type name.</param>
/// <param name="CollaborationSequence">The referenced message's exact, positive timeline sequence as a canonical decimal string.</param>
/// <param name="OccurredAtUtc">The referenced message's recorded time as an invariant UTC round-trip string
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>).</param>
public sealed record AgentAttemptInputMessageInputResponse(
    int Sequence, Guid CollaborationMessageId, string Type, string CollaborationSequence, string OccurredAtUtc);
