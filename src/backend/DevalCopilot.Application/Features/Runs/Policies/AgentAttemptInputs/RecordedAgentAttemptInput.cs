using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>One proved input of the set, as a reference only: its gapless position, the referenced message of the same run, that
/// message's recognized type, exact positive sequence and recorded time.</summary>
internal sealed record RecordedAgentAttemptInput(
    int Position, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
