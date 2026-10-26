namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationInspection;

/// <summary>
/// The recorded facts of one project verification execution as read now (ADR-0069). <c>Status</c> and <c>Outcome</c> are recorded
/// bookkeeping, not a physical exit or a successful verification; <c>Outcome</c> and <c>ExitCode</c> are null when none was recorded and
/// <c>CompletionFingerprintSha256</c> is null until completion and may legitimately differ from the claim fingerprint for a
/// <c>SourceChanged</c> execution. Times are UTC round-trip strings, null when the fact does not exist.
/// </summary>
public sealed record ProjectVerificationInspectionExecutionResponse(
    int ExecutionNumber,
    Guid CommandId,
    string CommandName,
    Guid WorkspaceId,
    Guid CheckpointId,
    int CheckpointNumber,
    string ClaimFingerprintSha256,
    string? CompletionFingerprintSha256,
    string Status,
    string? Outcome,
    int? ExitCode,
    string ClaimedAtUtc,
    string? DispatchedAtUtc,
    string? CompletedAtUtc,
    ProjectVerificationInspectionStreamResponse StandardOutput,
    ProjectVerificationInspectionStreamResponse StandardError);
