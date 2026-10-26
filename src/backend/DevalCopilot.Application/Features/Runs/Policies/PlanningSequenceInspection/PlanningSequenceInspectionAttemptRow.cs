namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The workspace, checkpoint and fingerprint the linked attempt recorded at claim, and the three time columns the shared identity
/// projection would otherwise convert, all as raw stored bytes (the fingerprint within its byte guard, with an oversized flag), so a
/// corrupt storage is a refused association and never a conversion fault inside a shared projection.</summary>
internal sealed record PlanningSequenceInspectionAttemptRow(
    byte[]? WorkspaceId,
    bool WorkspacePresent,
    byte[]? CheckpointId,
    bool CheckpointPresent,
    string? Fingerprint,
    bool FingerprintOversized,
    byte[]? ClaimedAtUtc,
    byte[]? CompletedAtUtc,
    bool CompletedPresent,
    byte[]? DispatchedAtUtc,
    bool DispatchedPresent);
