namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The identity, workspace and guarded fingerprint of one recorded source checkpoint, read by its identity.</summary>
internal sealed record LocalExecutionInspectionCheckpointRow(Guid Id, Guid WorkspaceId, string? Fingerprint, bool FingerprintOversized);
