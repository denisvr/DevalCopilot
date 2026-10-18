using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>
/// The bounded, untracked scalar projections of the recorded-verification read (ADR-0052). Nothing outside the listed columns is ever
/// selected: no membership digest, workspace path, executable, argument, timeout or stored enum text, and a path or hash only in the
/// window's storage projection. Every stored enum is classified by the database and every number and string is guarded before a value
/// is converted, so malformed SQLite storage reads as an explicit refusal rather than as a plausible value.
/// </summary>
internal static class RecordedVerificationProjection
{
    /// <summary>The most memberships this inspection judges, and so the number of distinct positions a coherent set can hold.</summary>
    public const int PositionBound = 32;

    private const int Unrecognized = 99;

    // length() of TEXT stops at an embedded NUL, so each guard bounds the stored bytes (CAST AS BLOB). A UTF-16 unit is at most three
    // UTF-8 bytes, so every admissible value fits its guard and the exact character bound is applied again in memory: a fingerprint is 64
    // ASCII characters, a command name at most 200 units, a relative path at most 1024 units and a content hash exactly 71 ASCII
    // characters (sha256: and 64 lowercase hexadecimal digits).
    private const int FingerprintByteBound = 64;
    private const int CommandNameByteBound = 600;
    private const int RelativeStoragePathByteBound = 3072;
    private const int ContentHashByteBound = 71;

    // A column with INTEGER affinity keeps TEXT, REAL or BLOB it cannot convert, and reading those as a number would silently yield 0 or a
    // truncated value. Numbers sort below TEXT and BLOB, so a numeric range admits only numbers, and `%` casts to an integer, so only a
    // stored INTEGER (never a REAL such as 7.5) equals its own remainder. The modulus has the column CLR type (a wider one makes EF cast
    // both sides to INTEGER and hides a REAL), so every Int32 whose magnitude is below int.MaxValue is its own remainder and the only
    // three whose remainder differs (int.MinValue, -int.MaxValue and int.MaxValue) are compared directly. INTEGER affinity stores an
    // integral REAL as INTEGER, so a direct comparison can only match the exact integer.
    private const int Int32Maximum = int.MaxValue;
    private const int Int32Minimum = int.MinValue;
    private const int Int32NegatedMaximum = -int.MaxValue;
    private const long SafeIntegerModulus = 1L << 53;

    /// <summary>The largest integer a browser number represents exactly (2^53 - 1).</summary>
    public const long MaximumSafeInteger = SafeIntegerModulus - 1;

    public static IQueryable<VerificationAttemptOwnershipRow> ProjectVerificationOwnership(
        this IQueryable<Attempt> attempts, IQueryable<Run> runs) =>
        attempts.Join(
            runs,
            attempt => attempt.RunId,
            run => run.Id,
            (attempt, run) => new VerificationAttemptOwnershipRow(
                run.ProjectId,
                attempt.AgentGitWorkspaceId,
                attempt.AgentGitCheckpointId,
                ((byte[])(object)attempt.AgentCheckpointFingerprintSha256!).Length <= FingerprintByteBound
                    ? attempt.AgentCheckpointFingerprintSha256
                    : null,
                attempt.AgentCheckpointFingerprintSha256 != null
                    && ((byte[])(object)attempt.AgentCheckpointFingerprintSha256).Length > FingerprintByteBound));

    public static IQueryable<VerificationCheckpointOwnershipRow> ProjectCheckpointOwnership(
        this IQueryable<GitCheckpoint> checkpoints, IQueryable<GitWorkspace> workspaces) =>
        checkpoints.Join(
            workspaces,
            checkpoint => checkpoint.WorkspaceId,
            workspace => workspace.Id,
            (checkpoint, workspace) => new VerificationCheckpointOwnershipRow(
                workspace.Id,
                workspace.ProjectId,
                checkpoint.CheckpointNumber >= 1
                    && checkpoint.CheckpointNumber <= int.MaxValue
                    && (checkpoint.CheckpointNumber == Int32Maximum || checkpoint.CheckpointNumber % Int32Maximum == checkpoint.CheckpointNumber)
                    ? checkpoint.CheckpointNumber
                    : null,
                ((byte[])(object)checkpoint.FingerprintSha256).Length <= FingerprintByteBound ? checkpoint.FingerprintSha256 : null,
                ((byte[])(object)checkpoint.FingerprintSha256).Length > FingerprintByteBound));

    public static IQueryable<VerificationMembershipRow> ProjectMembership(this IQueryable<AttemptVerificationEvidence> memberships) =>
        memberships.Select(membership => new VerificationMembershipRow(
            membership.Sequence >= 0 && membership.Sequence < PositionBound && membership.Sequence % PositionBound == membership.Sequence
                ? membership.Sequence
                : null,
            membership.VerificationCommandId,
            membership.VerificationExecutionId));

    public static IQueryable<VerificationExecutionRow> ProjectExecution(this IQueryable<VerificationExecution> executions) =>
        executions.Select(execution => new VerificationExecutionRow(
            execution.Id,
            execution.ProjectId,
            execution.GitWorkspaceId,
            execution.GitCheckpointId,
            execution.VerificationCommandId,
            execution.ExecutionNumber >= 1
                && execution.ExecutionNumber <= int.MaxValue
                && (execution.ExecutionNumber == Int32Maximum || execution.ExecutionNumber % Int32Maximum == execution.ExecutionNumber)
                ? execution.ExecutionNumber
                : null,
            ((byte[])(object)execution.CommandName).Length <= CommandNameByteBound ? execution.CommandName : null,
            ((byte[])(object)execution.CommandName).Length > CommandNameByteBound,
            ((byte[])(object)execution.CheckpointFingerprintSha256).Length <= FingerprintByteBound ? execution.CheckpointFingerprintSha256 : null,
            ((byte[])(object)execution.CheckpointFingerprintSha256).Length > FingerprintByteBound,
            ((byte[])(object)execution.CompletionFingerprintSha256!).Length <= FingerprintByteBound ? execution.CompletionFingerprintSha256 : null,
            execution.CompletionFingerprintSha256 != null
                && ((byte[])(object)execution.CompletionFingerprintSha256).Length > FingerprintByteBound,
            execution.Status == VerificationExecutionStatus.Passed ? 1
                : execution.Status == VerificationExecutionStatus.Failed ? 2
                : 0,
            execution.Outcome == null ? 0
                : execution.Outcome == VerificationExecutionOutcome.Exited ? 1
                : execution.Outcome == VerificationExecutionOutcome.TimedOut
                    || execution.Outcome == VerificationExecutionOutcome.Cancelled
                    || execution.Outcome == VerificationExecutionOutcome.Failed ? 2
                : Unrecognized,
            execution.ExitCode >= int.MinValue
                && execution.ExitCode <= int.MaxValue
                && (execution.ExitCode == Int32Maximum
                    || execution.ExitCode == Int32Minimum
                    || execution.ExitCode == Int32NegatedMaximum
                    || execution.ExitCode % Int32Maximum == execution.ExitCode)
                ? execution.ExitCode
                : null,
            execution.ExitCode != null,
            execution.ClaimedAtUtc,
            execution.DispatchedAtUtc,
            execution.CompletedAtUtc));

    /// <summary>Only the two allowlisted purposes are ever selected; any other stored purpose is never read as a supported stream.</summary>
    public static IQueryable<VerificationOutputArtifact> WhereAllowlisted(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Where(output => output.Purpose == VerificationOutputPurpose.StandardOutput
            || output.Purpose == VerificationOutputPurpose.StandardError);

    public static IQueryable<VerificationOutputDescriptorRow> ProjectDescriptor(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Select(output => new VerificationOutputDescriptorRow(
            output.Id,
            output.VerificationExecutionId,
            output.Purpose == VerificationOutputPurpose.StandardOutput ? 1
                : output.Purpose == VerificationOutputPurpose.StandardError ? 2
                : 0,
            output.ByteLength >= 0 && output.ByteLength <= MaximumSafeInteger && output.ByteLength % SafeIntegerModulus == output.ByteLength
                ? output.ByteLength
                : null,
            output.CaptureOutcome == VerificationOutputCaptureOutcome.CapturedWithKnownTruncation ? 1
                : output.CaptureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? 2
                : 0,
            output.Truncated == null ? 0
                : output.Truncated == true ? 1
                : output.Truncated == false ? 2
                : Unrecognized));

    public static IQueryable<VerificationOutputStorageRow> ProjectStorage(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Select(output => new VerificationOutputStorageRow(
            output.Id,
            output.VerificationExecutionId,
            output.Purpose == VerificationOutputPurpose.StandardOutput ? 1
                : output.Purpose == VerificationOutputPurpose.StandardError ? 2
                : 0,
            output.ByteLength >= 0 && output.ByteLength <= MaximumSafeInteger && output.ByteLength % SafeIntegerModulus == output.ByteLength
                ? output.ByteLength
                : null,
            output.CaptureOutcome == VerificationOutputCaptureOutcome.CapturedWithKnownTruncation ? 1
                : output.CaptureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? 2
                : 0,
            output.Truncated == null ? 0
                : output.Truncated == true ? 1
                : output.Truncated == false ? 2
                : Unrecognized,
            ((byte[])(object)output.RelativeStoragePath).Length <= RelativeStoragePathByteBound ? output.RelativeStoragePath : null,
            ((byte[])(object)output.RelativeStoragePath).Length > RelativeStoragePathByteBound,
            ((byte[])(object)output.ContentHash).Length <= ContentHashByteBound ? output.ContentHash : null,
            ((byte[])(object)output.ContentHash).Length > ContentHashByteBound));
}
