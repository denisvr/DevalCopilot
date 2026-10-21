using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The in-memory form of the coherent host-recorded process Exited evidence that ADR-0060 requires before a dispatched linked operation
/// is released from ownership: the same tuple <see cref="LocalExecutionOwnership"/> evaluates database-side. A timeout, cancellation,
/// adapter exception, missing evidence or a terminal enum alone proves no exit, and Exited is the invoked process only, never every
/// descendant.
/// </summary>
internal static class LocalExecutionExitProof
{
    public static bool Holds(Attempt attempt) =>
        attempt.AgentDispatchedAtUtc is not null
        && attempt.AgentProcessOutcome == ProcessOutcome.Exited
        && attempt.AgentProcessExitCode is not null
        && attempt.AgentProcessDuration is { } duration
        && duration >= TimeSpan.Zero;

    public static bool Holds(VerificationExecution execution) =>
        execution.DispatchedAtUtc is not null
        && execution.Outcome == VerificationExecutionOutcome.Exited
        && execution.ExitCode is not null;
}
