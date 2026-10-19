using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;

/// <summary>One stored collaboration message associated with the attempt, as a reference only: never the message's text.</summary>
/// <param name="Position">The zero-based ordinal in this read's sequence order; it is not a persisted sequence.</param>
/// <param name="CollaborationMessageId">The associated message of the same run.</param>
/// <param name="Type">The message's recognized type.</param>
/// <param name="CollaborationSequence">The message's exact, positive timeline sequence.</param>
/// <param name="OccurredAtUtc">The message's recorded time.</param>
public sealed record AgentAttemptOutputReference(
    int Position, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
