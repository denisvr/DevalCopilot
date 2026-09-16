using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationExecutions;

public sealed record VerificationExecutionQueryResult(
    Guid VerificationExecutionId,
    Guid VerificationCommandId,
    int ExecutionNumber,
    VerificationExecutionStatus Status,
    bool IsDispatched,
    VerificationExecutionOutcome? Outcome,
    int? ExitCode,
    bool HasStandardOutput,
    bool HasStandardError,
    bool? StandardOutputTruncated,
    bool? StandardErrorTruncated,
    VerificationOutputCaptureOutcome? StandardOutputCaptureOutcome,
    VerificationOutputCaptureOutcome? StandardErrorCaptureOutcome,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc);
