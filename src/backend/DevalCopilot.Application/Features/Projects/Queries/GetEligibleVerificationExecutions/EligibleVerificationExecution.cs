using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

namespace DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;

/// <summary>A candidate for dispatch, not authority to launch: the supervisor carries <see cref="ToSnapshot"/> into the final
/// dispatch decision, which re-reads the durable execution and its ownership.</summary>
public sealed record EligibleVerificationExecution(
    Guid VerificationExecutionId,
    string CheckpointFingerprintSha256,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkspacePath,
    int TimeoutSeconds,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid VerificationCommandId)
{
    public VerificationDispatchSnapshot ToSnapshot() => new(
        ProjectId, WorkspaceId, CheckpointId, VerificationCommandId, WorkspacePath, CheckpointFingerprintSha256, ExecutablePath, Arguments, TimeoutSeconds);
}
