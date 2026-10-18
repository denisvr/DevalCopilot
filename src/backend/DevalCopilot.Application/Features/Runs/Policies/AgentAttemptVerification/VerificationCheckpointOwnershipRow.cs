namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The scalar facts of the attempt's recorded checkpoint and the workspace it belongs to. <see cref="Number"/> is null unless the
/// database proved the stored number a positive 32-bit integer; the fingerprint is read only within its byte guard.</summary>
internal sealed record VerificationCheckpointOwnershipRow(
    Guid WorkspaceId, Guid WorkspaceProjectId, int? Number, string? Fingerprint, bool FingerprintOversized);
