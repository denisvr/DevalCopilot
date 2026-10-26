using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// The bounded, untracked scalar projections of the recorded project verification inspection (ADR-0069). Nothing outside the listed
/// columns is ever selected: no workspace path, executable, argument, timeout or stored enum text, and a storage path or content hash only
/// in a window's storage projection. Every stored enum is classified by the database, every number is guarded to an exact stored integer
/// in its range, every text is bounded by its stored bytes (a cast to a blob counts an embedded NUL) and every identifier and time is
/// projected as its raw stored bytes within its bound and parsed by <see cref="ProjectVerificationInspectionStorage"/>, so malformed
/// SQLite storage reads as an explicit refusal rather than a plausible value or a conversion fault. The exact character bounds are applied
/// again in memory by the readers.
/// </summary>
internal static class ProjectVerificationInspectionProjection
{
    /// <summary>The fixed page size; one more row is read so a reachable extra row proves that older executions exist.</summary>
    public const int PageSize = 20;

    public const long MaximumSafeInteger = (1L << 53) - 1;

    // A UTF-16 unit is at most three UTF-8 bytes, so every admissible value fits its guard and the exact bound is applied again in memory:
    // a fingerprint is 64 ASCII characters, a command name at most 200 units, a relative path at most 1024 units and a content hash exactly
    // 71 ASCII characters (sha256: and 64 lowercase hexadecimal digits).
    private const int FingerprintByteBound = 64;
    private const int CommandNameByteBound = 600;
    private const int RelativeStoragePathByteBound = 3072;
    private const int ContentHashByteBound = 71;
    private const int TruncatedByteBound = 8;
    private const int IdentifierBytes = ProjectVerificationInspectionStorage.IdentifierBytes;
    private const int TimeBytes = ProjectVerificationInspectionStorage.TimeBytes;
    private const int Unrecognized = 99;

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

    public static IQueryable<ProjectVerificationHistoryRow> ProjectHistory(this IQueryable<VerificationExecution> executions) =>
        executions.Select(execution => new ProjectVerificationHistoryRow(
            ((byte[])(object)execution.Id).Length <= IdentifierBytes ? (byte[])(object)execution.Id : null,
            execution.ExecutionNumber >= 1
                && execution.ExecutionNumber <= int.MaxValue
                && (execution.ExecutionNumber == Int32Maximum || execution.ExecutionNumber % Int32Maximum == execution.ExecutionNumber)
                ? execution.ExecutionNumber
                : null,
            ((byte[])(object)execution.CommandName).Length <= CommandNameByteBound ? execution.CommandName : null,
            ((byte[])(object)execution.CommandName).Length > CommandNameByteBound,
            ((byte[])(object)execution.ClaimedAtUtc).Length <= TimeBytes ? (byte[])(object)execution.ClaimedAtUtc : null));

    /// <summary>The execution, its project, the workspace it names and the checkpoint it names, joined by the stored identities in one
    /// statement, so an execution whose project row is absent selects nothing.</summary>
    public static IQueryable<ProjectVerificationExecutionRow> ProjectExecution(
        this IQueryable<VerificationExecution> executions,
        IQueryable<Project> projects,
        IQueryable<GitWorkspace> workspaces,
        IQueryable<GitCheckpoint> checkpoints) =>
        executions
            .Join(projects, execution => execution.ProjectId, project => project.Id, (execution, project) => execution)
            .Join(
                workspaces,
                execution => execution.GitWorkspaceId,
                workspace => workspace.Id,
                (execution, workspace) => new { execution, workspace })
            .Join(
                checkpoints,
                pair => pair.execution.GitCheckpointId,
                checkpoint => checkpoint.Id,
                (pair, checkpoint) => new ProjectVerificationExecutionRow(
                    ((byte[])(object)pair.execution.Id).Length <= IdentifierBytes ? (byte[])(object)pair.execution.Id : null,
                    ((byte[])(object)pair.execution.ProjectId).Length <= IdentifierBytes ? (byte[])(object)pair.execution.ProjectId : null,
                    ((byte[])(object)pair.execution.GitWorkspaceId).Length <= IdentifierBytes
                        ? (byte[])(object)pair.execution.GitWorkspaceId
                        : null,
                    ((byte[])(object)pair.execution.GitCheckpointId).Length <= IdentifierBytes
                        ? (byte[])(object)pair.execution.GitCheckpointId
                        : null,
                    ((byte[])(object)pair.execution.VerificationCommandId).Length <= IdentifierBytes
                        ? (byte[])(object)pair.execution.VerificationCommandId
                        : null,
                    pair.execution.ExecutionNumber >= 1
                        && pair.execution.ExecutionNumber <= int.MaxValue
                        && (pair.execution.ExecutionNumber == Int32Maximum
                            || pair.execution.ExecutionNumber % Int32Maximum == pair.execution.ExecutionNumber)
                        ? pair.execution.ExecutionNumber
                        : null,
                    ((byte[])(object)pair.execution.CommandName).Length <= CommandNameByteBound ? pair.execution.CommandName : null,
                    ((byte[])(object)pair.execution.CommandName).Length > CommandNameByteBound,
                    ((byte[])(object)pair.execution.CheckpointFingerprintSha256).Length <= FingerprintByteBound
                        ? pair.execution.CheckpointFingerprintSha256
                        : null,
                    ((byte[])(object)pair.execution.CheckpointFingerprintSha256).Length > FingerprintByteBound,
                    ((byte[])(object)pair.execution.CompletionFingerprintSha256!).Length <= FingerprintByteBound
                        ? pair.execution.CompletionFingerprintSha256
                        : null,
                    pair.execution.CompletionFingerprintSha256 != null
                        && ((byte[])(object)pair.execution.CompletionFingerprintSha256).Length > FingerprintByteBound,
                    pair.execution.CompletionFingerprintSha256 != null,
                    pair.execution.Status == VerificationExecutionStatus.Running ? 1
                        : pair.execution.Status == VerificationExecutionStatus.Passed ? 2
                        : pair.execution.Status == VerificationExecutionStatus.Failed ? 3
                        : pair.execution.Status == VerificationExecutionStatus.TimedOut ? 4
                        : pair.execution.Status == VerificationExecutionStatus.Cancelled ? 5
                        : pair.execution.Status == VerificationExecutionStatus.Interrupted ? 6
                        : pair.execution.Status == VerificationExecutionStatus.SourceChanged ? 7
                        : pair.execution.Status == VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused ? 8
                        : 0,
                    pair.execution.Outcome == null ? 0
                        : pair.execution.Outcome == VerificationExecutionOutcome.Exited ? 1
                        : pair.execution.Outcome == VerificationExecutionOutcome.TimedOut ? 2
                        : pair.execution.Outcome == VerificationExecutionOutcome.Cancelled ? 3
                        : pair.execution.Outcome == VerificationExecutionOutcome.Failed ? 4
                        : Unrecognized,
                    pair.execution.ExitCode >= int.MinValue
                        && pair.execution.ExitCode <= int.MaxValue
                        && (pair.execution.ExitCode == Int32Maximum
                            || pair.execution.ExitCode == Int32Minimum
                            || pair.execution.ExitCode == Int32NegatedMaximum
                            || pair.execution.ExitCode % Int32Maximum == pair.execution.ExitCode)
                        ? pair.execution.ExitCode
                        : null,
                    pair.execution.ExitCode != null,
                    ((byte[])(object)pair.execution.ClaimedAtUtc).Length <= TimeBytes ? (byte[])(object)pair.execution.ClaimedAtUtc : null,
                    ((byte[])(object)pair.execution.DispatchedAtUtc!).Length <= TimeBytes
                        ? (byte[])(object)pair.execution.DispatchedAtUtc
                        : null,
                    pair.execution.DispatchedAtUtc != null,
                    ((byte[])(object)pair.execution.CompletedAtUtc!).Length <= TimeBytes
                        ? (byte[])(object)pair.execution.CompletedAtUtc
                        : null,
                    pair.execution.CompletedAtUtc != null,
                    ((byte[])(object)pair.workspace.Id).Length <= IdentifierBytes ? (byte[])(object)pair.workspace.Id : null,
                    ((byte[])(object)pair.workspace.ProjectId).Length <= IdentifierBytes ? (byte[])(object)pair.workspace.ProjectId : null,
                    ((byte[])(object)checkpoint.Id).Length <= IdentifierBytes ? (byte[])(object)checkpoint.Id : null,
                    ((byte[])(object)checkpoint.WorkspaceId).Length <= IdentifierBytes ? (byte[])(object)checkpoint.WorkspaceId : null,
                    checkpoint.CheckpointNumber >= 1
                        && checkpoint.CheckpointNumber <= int.MaxValue
                        && (checkpoint.CheckpointNumber == Int32Maximum
                            || checkpoint.CheckpointNumber % Int32Maximum == checkpoint.CheckpointNumber)
                        ? checkpoint.CheckpointNumber
                        : null,
                    ((byte[])(object)checkpoint.FingerprintSha256).Length <= FingerprintByteBound ? checkpoint.FingerprintSha256 : null,
                    ((byte[])(object)checkpoint.FingerprintSha256).Length > FingerprintByteBound));

    /// <summary>Only the two allowlisted purposes are ever selected; any other stored purpose is never read as a supported stream.</summary>
    public static IQueryable<VerificationOutputArtifact> WhereAllowlisted(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Where(output => output.Purpose == VerificationOutputPurpose.StandardOutput
            || output.Purpose == VerificationOutputPurpose.StandardError);

    public static IQueryable<ProjectVerificationOutputDescriptorRow> ProjectDescriptor(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Select(output => new ProjectVerificationOutputDescriptorRow(
            ((byte[])(object)output.Id).Length <= IdentifierBytes ? (byte[])(object)output.Id : null,
            ((byte[])(object)output.VerificationExecutionId).Length <= IdentifierBytes
                ? (byte[])(object)output.VerificationExecutionId
                : null,
            output.Purpose == VerificationOutputPurpose.StandardOutput ? 1
                : output.Purpose == VerificationOutputPurpose.StandardError ? 2
                : 0,
            output.ByteLength >= 0 && output.ByteLength <= MaximumSafeInteger && output.ByteLength % SafeIntegerModulus == output.ByteLength
                ? output.ByteLength
                : null,
            output.CaptureOutcome == VerificationOutputCaptureOutcome.CapturedWithKnownTruncation ? 1
                : output.CaptureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? 2
                : 0,
            ((byte[])(object)output.Truncated!).Length <= TruncatedByteBound ? (byte[])(object)output.Truncated : null,
            output.Truncated != null));

    public static IQueryable<ProjectVerificationOutputStorageRow> ProjectStorage(this IQueryable<VerificationOutputArtifact> outputs) =>
        outputs.Select(output => new ProjectVerificationOutputStorageRow(
            ((byte[])(object)output.Id).Length <= IdentifierBytes ? (byte[])(object)output.Id : null,
            ((byte[])(object)output.VerificationExecutionId).Length <= IdentifierBytes
                ? (byte[])(object)output.VerificationExecutionId
                : null,
            output.Purpose == VerificationOutputPurpose.StandardOutput ? 1
                : output.Purpose == VerificationOutputPurpose.StandardError ? 2
                : 0,
            output.ByteLength >= 0 && output.ByteLength <= MaximumSafeInteger && output.ByteLength % SafeIntegerModulus == output.ByteLength
                ? output.ByteLength
                : null,
            output.CaptureOutcome == VerificationOutputCaptureOutcome.CapturedWithKnownTruncation ? 1
                : output.CaptureOutcome == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? 2
                : 0,
            ((byte[])(object)output.Truncated!).Length <= TruncatedByteBound ? (byte[])(object)output.Truncated : null,
            output.Truncated != null,
            ((byte[])(object)output.RelativeStoragePath).Length <= RelativeStoragePathByteBound ? output.RelativeStoragePath : null,
            ((byte[])(object)output.RelativeStoragePath).Length > RelativeStoragePathByteBound,
            ((byte[])(object)output.ContentHash).Length <= ContentHashByteBound ? output.ContentHash : null,
            ((byte[])(object)output.ContentHash).Length > ContentHashByteBound));
}
