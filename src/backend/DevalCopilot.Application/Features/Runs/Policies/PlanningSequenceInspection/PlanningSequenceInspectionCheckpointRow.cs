namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The scalar facts of the pinned checkpoint: its workspace as raw stored bytes and its fingerprint, read only within its byte
/// guard and flagged when oversized so a rejected storage is never read as a legitimate null.</summary>
internal sealed record PlanningSequenceInspectionCheckpointRow(byte[]? WorkspaceId, string? Fingerprint, bool FingerprintOversized);
