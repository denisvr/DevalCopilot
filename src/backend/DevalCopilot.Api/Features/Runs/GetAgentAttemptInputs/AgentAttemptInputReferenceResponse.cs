namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputs;

/// <summary>One recorded collaboration input of the attempt, as a reference only: never the referenced message's text.</summary>
/// <param name="Sequence">The zero-based recorded input position.</param>
/// <param name="CollaborationMessageId">The referenced message of the same run.</param>
/// <param name="Type">The referenced message's recognized type name.</param>
/// <param name="CollaborationSequence">The referenced message's exact, positive timeline sequence as a canonical decimal string, so a
/// client never rounds it to a floating-point number.</param>
/// <param name="OccurredAtUtc">The referenced message's recorded time as an invariant UTC round-trip string
/// (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>), so a client validates the text itself before any date conversion.</param>
public sealed record AgentAttemptInputReferenceResponse(
    int Sequence, Guid CollaborationMessageId, string Type, string CollaborationSequence, string OccurredAtUtc);
