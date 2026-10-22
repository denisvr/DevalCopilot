using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionStepInspection;

/// <summary>
/// The proved verification a step links: its execution identity and positive number, the immutable snapshot command name, its recorded
/// status (<c>Running</c>, <c>Passed</c>, <c>Failed</c>, <c>TimedOut</c>, <c>Cancelled</c>, <c>Interrupted</c>, <c>SourceChanged</c> or
/// <c>LocalExecutionSequenceDispatchRefused</c>; never promoted to a clean exit), the nullable process outcome and exit code, its claim,
/// dispatch and completion times (UTC round-trip strings) and the admitted metadata of its two sealed streams. It never carries a path,
/// executable, argument, hash or output byte.
/// </summary>
public sealed record LocalExecutionInspectionVerificationResponse(
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    string Status,
    string? Outcome,
    int? ExitCode,
    string ClaimedAtUtc,
    string? DispatchedAtUtc,
    string? CompletedAtUtc,
    AgentAttemptVerificationStreamResponse StandardOutput,
    AgentAttemptVerificationStreamResponse StandardError);
