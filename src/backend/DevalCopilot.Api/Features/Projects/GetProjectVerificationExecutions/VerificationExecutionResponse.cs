namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationExecutions;

public sealed record VerificationExecutionResponse(
    Guid VerificationExecutionId,
    Guid VerificationCommandId,
    Guid GitCheckpointId,
    string CheckpointFingerprintSha256,
    int ExecutionNumber,
    string Status,
    bool IsDispatched,
    string? Outcome,
    int? ExitCode,
    bool HasStandardOutput,
    bool HasStandardError,
    bool? StandardOutputTruncated,
    bool? StandardErrorTruncated,
    string? StandardOutputCaptureOutcome,
    string? StandardErrorCaptureOutcome,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc);
