namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;

/// <summary>Metadata only — never a storage path, content hash, or any raw content. Truncated is
/// null exactly when genuinely unknown (an artifact recovered from a host interruption).</summary>
public sealed record AgentAttemptArtifactMetadataResponse(string Purpose, long ByteLength, bool? Truncated, string CaptureOutcome);
