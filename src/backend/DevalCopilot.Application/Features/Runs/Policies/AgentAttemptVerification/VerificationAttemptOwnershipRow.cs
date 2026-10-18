namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The scalar ownership facts of the selected attempt: its run's project and the workspace, checkpoint and fingerprint the
/// attempt recorded at claim. The fingerprint is read only within its byte guard and carries a flag, so a rejected oversized storage is
/// never mistaken for a legitimate null.</summary>
internal sealed record VerificationAttemptOwnershipRow(
    Guid ProjectId, Guid? WorkspaceId, Guid? CheckpointId, string? Fingerprint, bool FingerprintOversized);
