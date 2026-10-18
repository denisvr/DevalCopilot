using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>One recorded verification member in recorded order, with its execution's metadata as read now.</summary>
/// <param name="Sequence">The zero-based recorded position.</param>
/// <param name="CommandId">The recorded command identity, equal to the execution's own.</param>
/// <param name="ExecutionId">The recorded execution identity.</param>
/// <param name="ExecutionNumber">The execution's positive project-scoped number.</param>
/// <param name="CommandName">The command name from the execution's immutable snapshot, never the current recipe.</param>
/// <param name="Status">Passed or Failed; the process outcome is Exited for both.</param>
/// <param name="ExitCode">The recorded exit code: 0 for Passed, nonzero for Failed.</param>
/// <param name="StandardOutput">The admitted metadata of the standard output stream.</param>
/// <param name="StandardError">The admitted metadata of the standard error stream.</param>
public sealed record AgentAttemptVerificationMember(
    int Sequence,
    Guid CommandId,
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    AgentAttemptVerificationMemberStatus Status,
    int ExitCode,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset DispatchedAtUtc,
    DateTimeOffset CompletedAtUtc,
    RecordedVerificationOutputDescriptor StandardOutput,
    RecordedVerificationOutputDescriptor StandardError);
