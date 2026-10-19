namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptInputs;

/// <summary>The whole proved input set of one Agent attempt: its immutable recorded identity and every input in recorded order (0 to 64;
/// none only for a Planner). It states recorded references as read now, never the complete prompt, the context an Agent saw or a
/// claim-time snapshot.</summary>
internal sealed record RecordedAgentAttemptInputSet(
    Guid AttemptId, int AttemptNumber, CoherentAgentAttemptIdentity Identity, IReadOnlyList<RecordedAgentAttemptInput> Inputs);
