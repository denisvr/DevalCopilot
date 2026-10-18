namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>The checkpoint the attempt recorded at claim, by identity and positive number; never the workspace's latest checkpoint.</summary>
public sealed record AgentAttemptVerificationEvidenceCheckpoint(Guid Id, int CheckpointNumber);
