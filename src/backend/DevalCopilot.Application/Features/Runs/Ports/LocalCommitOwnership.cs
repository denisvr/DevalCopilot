namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>The durable ownership identity the host must find unchanged in the worktree's marker (ADR-0008).</summary>
public sealed record LocalCommitOwnership(
    Guid WorkspaceId, Guid ProjectId, Guid LeaseId, ulong PhysicalVolumeSerialNumber, string PhysicalFileIdHex);
