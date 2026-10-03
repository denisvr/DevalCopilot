using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

/// <summary>
/// The bounded, operation-owned expectation a supervisor carries from its candidate read into the final dispatch decision:
/// the ownership identities and the exact source and process facts it used for the Git capture and will use to launch. The
/// eligibility feed only proposes candidates; the decision re-reads the durable execution and requires it to agree with this
/// snapshot, so a stale tuple can never authorize a launch. Agreement is with the durable execution, never with the live recipe,
/// and is not protection against out-of-band tampering with the database.
/// </summary>
public sealed record VerificationDispatchSnapshot(
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    Guid VerificationCommandId,
    string WorkspacePath,
    string CheckpointFingerprintSha256,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds) : IEquatable<VerificationDispatchSnapshot>
{
    public static VerificationDispatchSnapshot Of(VerificationExecution execution) => new(
        execution.ProjectId,
        execution.GitWorkspaceId,
        execution.GitCheckpointId,
        execution.VerificationCommandId,
        execution.WorkspacePath,
        execution.CheckpointFingerprintSha256,
        execution.ExecutablePath,
        execution.Arguments,
        execution.TimeoutSeconds);

    public bool Equals(VerificationDispatchSnapshot? other) =>
        other is not null
        && ProjectId == other.ProjectId
        && WorkspaceId == other.WorkspaceId
        && CheckpointId == other.CheckpointId
        && VerificationCommandId == other.VerificationCommandId
        && string.Equals(WorkspacePath, other.WorkspacePath, StringComparison.Ordinal)
        && string.Equals(CheckpointFingerprintSha256, other.CheckpointFingerprintSha256, StringComparison.Ordinal)
        && string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.Ordinal)
        && TimeoutSeconds == other.TimeoutSeconds
        && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(ProjectId, WorkspaceId, CheckpointId, VerificationCommandId, TimeoutSeconds);
}
