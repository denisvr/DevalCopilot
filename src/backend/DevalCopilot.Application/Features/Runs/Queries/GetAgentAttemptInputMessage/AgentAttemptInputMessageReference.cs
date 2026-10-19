using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;

/// <summary>The selected input of the proved set, exactly as the reference inspection admits it.</summary>
/// <param name="Sequence">The zero-based recorded input position.</param>
/// <param name="CollaborationMessageId">The referenced message of the same run.</param>
/// <param name="Type">The referenced message's recognized type.</param>
/// <param name="CollaborationSequence">The referenced message's exact, positive timeline sequence.</param>
/// <param name="OccurredAtUtc">The referenced message's recorded time.</param>
public sealed record AgentAttemptInputMessageReference(
    int Sequence, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
