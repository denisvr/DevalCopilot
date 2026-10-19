using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>One proved output of the set, as a reference only: its zero-based ordinal in the read's sequence order, the message of the
/// same run, that message's recognized type, exact positive sequence and recorded time.</summary>
internal sealed record RecordedAgentAttemptOutput(
    int Position, Guid CollaborationMessageId, CollaborationMessageType Type, long CollaborationSequence, DateTimeOffset OccurredAtUtc);
