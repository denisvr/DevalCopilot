namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// The scalar ownership and result facts of one selected execution, its workspace and its named checkpoint, read by one bounded relational
/// statement (ADR-0069). Identifiers and times are raw stored bytes within their bound, a <c>Present</c> flag tells a rejected value from an
/// absent one, <see cref="StatusCode"/> is 1 to 8 in declaration order (0 unknown) and <see cref="OutcomeCode"/> 0 for none, 1 to 4 in
/// declaration order and 99 for an unknown one, and a number is null unless the stored value is an exact integer in its range. Every string
/// is read only within its byte guard and flagged when oversized. No path, executable, argument or timeout is part of it.
/// </summary>
internal sealed record ProjectVerificationExecutionRow(
    byte[]? Id,
    byte[]? ProjectId,
    byte[]? WorkspaceId,
    byte[]? CheckpointId,
    byte[]? CommandId,
    int? Number,
    string? CommandName,
    bool CommandNameOversized,
    string? ClaimFingerprint,
    bool ClaimFingerprintOversized,
    string? CompletionFingerprint,
    bool CompletionFingerprintOversized,
    bool CompletionFingerprintPresent,
    int StatusCode,
    int OutcomeCode,
    int? ExitCode,
    bool ExitCodePresent,
    byte[]? ClaimedAtUtc,
    byte[]? DispatchedAtUtc,
    bool DispatchedPresent,
    byte[]? CompletedAtUtc,
    bool CompletedPresent,
    byte[]? WorkspaceRowId,
    byte[]? WorkspaceProjectId,
    byte[]? CheckpointRowId,
    byte[]? CheckpointWorkspaceId,
    int? CheckpointNumber,
    string? CheckpointFingerprint,
    bool CheckpointFingerprintOversized);
