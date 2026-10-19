using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

/// <summary>The selected output of the proved set, exactly as the reference inspection admits it.</summary>
/// <param name="Position">The zero-based ordinal in the proved set's sequence order.</param>
/// <param name="CollaborationMessageId">The associated message of the same run.</param>
/// <param name="Type">The message's recognized type.</param>
/// <param name="CollaborationSequence">The message's exact, positive timeline sequence.</param>
/// <param name="OccurredAtUtc">The message's recorded time.</param>
public sealed record AgentAttemptOutputMessageReference(
    int Position, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
