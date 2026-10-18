namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

/// <summary>The checkpoint the attempt recorded at claim, by identity and positive number; never the workspace latest checkpoint.</summary>
public sealed record AgentAttemptVerificationCheckpointResponse(Guid Id, int CheckpointNumber);
