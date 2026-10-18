namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

/// <summary>One recorded verification member in recorded order, as read now.</summary>
/// <param name="Sequence">The zero-based recorded position.</param>
/// <param name="CommandId">The recorded command identity, equal to the execution's own.</param>
/// <param name="ExecutionId">The recorded execution identity.</param>
/// <param name="ExecutionNumber">The execution's positive project-scoped number.</param>
/// <param name="CommandName">The command name from the execution's immutable snapshot, never the current recipe.</param>
/// <param name="Status">Passed (exit code 0) or Failed (nonzero exit code); the process always Exited.</param>
/// <param name="ExitCode">The recorded 32-bit exit code.</param>
/// <param name="ClaimedAtUtc">Invariant UTC round-trip text (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>).</param>
/// <param name="DispatchedAtUtc">Invariant UTC round-trip text.</param>
/// <param name="CompletedAtUtc">Invariant UTC round-trip text.</param>
/// <param name="StandardOutput">The standard output stream metadata.</param>
/// <param name="StandardError">The standard error stream metadata.</param>
public sealed record AgentAttemptVerificationMemberResponse(
    int Sequence,
    Guid CommandId,
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    string Status,
    int ExitCode,
    string ClaimedAtUtc,
    string DispatchedAtUtc,
    string CompletedAtUtc,
    AgentAttemptVerificationStreamResponse StandardOutput,
    AgentAttemptVerificationStreamResponse StandardError);
