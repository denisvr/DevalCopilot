using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The recorded facts of one proved verification execution of a step: its identity and positive number, its immutable snapshot
/// command name, its recorded status, process outcome and exit code, and its claim, dispatch and completion times. A status of Running,
/// Failed, a dispatch refusal or an interruption is bookkeeping; none is promoted to a clean exit. No path, executable, argument or hash.</summary>
public sealed record LocalExecutionVerificationFacts(
    Guid ExecutionId,
    int ExecutionNumber,
    string CommandName,
    VerificationExecutionStatus Status,
    VerificationExecutionOutcome? Outcome,
    int? ExitCode,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? CompletedAtUtc);
