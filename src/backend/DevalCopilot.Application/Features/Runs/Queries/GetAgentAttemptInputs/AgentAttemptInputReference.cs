using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>One recorded collaboration input of the attempt, as a reference only: never the referenced message's text.</summary>
/// <param name="Sequence">The zero-based recorded input position, exactly as stored.</param>
/// <param name="CollaborationMessageId">The referenced message of the same run.</param>
/// <param name="Type">The referenced message's recognized type.</param>
/// <param name="CollaborationSequence">The referenced message's exact, positive timeline sequence.</param>
/// <param name="OccurredAtUtc">The referenced message's recorded time.</param>
public sealed record AgentAttemptInputReference(
    int Sequence, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
