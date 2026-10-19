namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptOutputs;

/// <summary>The whole proved output set of one Agent attempt: its immutable recorded identity and every message associated with it by the
/// message-level attempt reference, in sequence order (0 to 64). It states stored associations as read now, never the complete provider
/// response, what was generated or seen, a result outcome or a claim-time snapshot.</summary>
internal sealed record RecordedAgentAttemptOutputSet(
    Guid AttemptId, int AttemptNumber, CoherentAgentAttemptIdentity Identity, IReadOnlyList<RecordedAgentAttemptOutput> Outputs);
