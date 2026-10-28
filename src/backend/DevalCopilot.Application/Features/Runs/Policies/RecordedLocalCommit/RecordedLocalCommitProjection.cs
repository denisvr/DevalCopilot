using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The bounded, untracked scalar projection of the recorded local-commit inspection (ADR-0070). Nothing outside the listed columns is
/// ever selected: no authority member, request, authority or index digest, commit message, author, commit time, physical index receipt,
/// prepared artifact path or digest, lease, review or attempt identity, workspace path or stored enum text, and the prepared commit SHA
/// only while the stored status is Completed. Every stored enum is classified by the database, every number is guarded to an exact stored
/// INTEGER in its range, every text is bounded by its stored bytes (a cast to a blob counts an embedded NUL) and every identifier and time
/// is projected as its raw stored bytes within its bound and parsed by <see cref="RecordedLocalCommitStorage"/>, so malformed SQLite
/// storage reads as an explicit refusal rather than a plausible value or a conversion fault. The exact character bounds are applied again
/// in memory by the reader.
/// </summary>
internal static class RecordedLocalCommitProjection
{
    // A UTF-16 unit is at most three UTF-8 bytes, so every admissible branch name fits its guard and the exact bound is applied again in
    // memory. An object ID and a fingerprint are ASCII, so anything longer than their exact length in bytes is oversized, and a reason of
    // at most 128 characters fits 384 bytes.
    private const int ObjectIdByteBound = RecordedLocalCommitStorage.ObjectIdLength;
    private const int FingerprintByteBound = RecordedLocalCommitStorage.FingerprintLength;
    private const int BranchNameByteBound = RecordedLocalCommitStorage.MaximumBranchLength * 3;
    private const int ReasonByteBound = RecordedLocalCommitStorage.MaximumReasonLength * 3;
    private const int ModeByteBound = 8;
    private const int IdentifierBytes = RecordedLocalCommitStorage.IdentifierBytes;
    private const int TimeBytes = RecordedLocalCommitStorage.TimeBytes;

    // A column with INTEGER affinity keeps TEXT, REAL or BLOB it cannot convert, and reading those as a number would silently yield 0 or a
    // truncated value. Numbers sort below TEXT and BLOB, so a numeric range admits only numbers, and `%` casts to an integer, so only a
    // stored INTEGER (never a REAL such as 7.5) equals its own remainder. The modulus has the column CLR type (a wider one makes EF cast
    // both sides to INTEGER and hides a REAL), so every Int32 whose magnitude is below int.MaxValue is its own remainder and the only
    // positive one whose remainder differs (int.MaxValue) is compared directly. INTEGER affinity stores an integral REAL as INTEGER, so a
    // direct comparison can only match the exact integer.
    private const int Int32Maximum = int.MaxValue;
    private const long SafeIntegerModulus = 1L << 53;

    public static IQueryable<RecordedLocalCommitRow> Project(this IQueryable<RecordedLocalCommitJoin> joins) =>
        joins.Select(row => new RecordedLocalCommitRow(
            ((byte[])(object)row.Run.Id).Length <= IdentifierBytes ? (byte[])(object)row.Run.Id : null,
            ((byte[])(object)row.Run.ProjectId).Length <= IdentifierBytes ? (byte[])(object)row.Run.ProjectId : null,
            ((byte[])(object)EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)).Length <= ModeByteBound
                ? EF.Property<string>(row.Run, Run.ExecutionModeStorageProperty)
                : null,
            ((byte[])(object)row.Project.Id).Length <= IdentifierBytes ? (byte[])(object)row.Project.Id : null,
            (byte[]?)(object)row.Operation.Id != null,
            ((byte[])(object)row.Operation.Id).Length <= IdentifierBytes ? (byte[])(object)row.Operation.Id : null,
            ((byte[])(object)row.Operation.RunId).Length <= IdentifierBytes ? (byte[])(object)row.Operation.RunId : null,
            ((byte[])(object)row.Operation.ProjectId).Length <= IdentifierBytes ? (byte[])(object)row.Operation.ProjectId : null,
            ((byte[])(object)row.Operation.GitWorkspaceId).Length <= IdentifierBytes ? (byte[])(object)row.Operation.GitWorkspaceId : null,
            ((byte[])(object)row.Operation.GitCheckpointId).Length <= IdentifierBytes
                ? (byte[])(object)row.Operation.GitCheckpointId
                : null,
            row.Operation.CheckpointNumber >= 1
                && row.Operation.CheckpointNumber <= int.MaxValue
                && (row.Operation.CheckpointNumber == Int32Maximum || row.Operation.CheckpointNumber % Int32Maximum == row.Operation.CheckpointNumber)
                ? row.Operation.CheckpointNumber
                : null,
            ((byte[])(object)row.Operation.CheckpointFingerprintSha256).Length <= FingerprintByteBound
                ? row.Operation.CheckpointFingerprintSha256
                : null,
            row.Operation.CheckpointFingerprintSha256 != null
                && ((byte[])(object)row.Operation.CheckpointFingerprintSha256).Length > FingerprintByteBound,
            row.Operation.Status == LocalCommitStatus.Prepared ? 1
                : row.Operation.Status == LocalCommitStatus.Executing ? 2
                : row.Operation.Status == LocalCommitStatus.Completed ? 3
                : row.Operation.Status == LocalCommitStatus.Failed ? 4
                : row.Operation.Status == LocalCommitStatus.Interrupted ? 5
                : row.Operation.Status == LocalCommitStatus.NeedsAttention ? 6
                : 0,
            ((byte[])(object)row.Operation.BranchName).Length <= BranchNameByteBound ? row.Operation.BranchName : null,
            row.Operation.BranchName != null && ((byte[])(object)row.Operation.BranchName).Length > BranchNameByteBound,
            ((byte[])(object)row.Operation.ParentCommitSha).Length <= ObjectIdByteBound ? row.Operation.ParentCommitSha : null,
            row.Operation.ParentCommitSha != null && ((byte[])(object)row.Operation.ParentCommitSha).Length > ObjectIdByteBound,
            ((byte[])(object)row.Operation.TreeSha).Length <= ObjectIdByteBound ? row.Operation.TreeSha : null,
            row.Operation.TreeSha != null && ((byte[])(object)row.Operation.TreeSha).Length > ObjectIdByteBound,
            row.Operation.Status == LocalCommitStatus.Completed && ((byte[])(object)row.Operation.CommitSha).Length <= ObjectIdByteBound
                ? row.Operation.CommitSha
                : null,
            row.Operation.CommitSha != null
                && row.Operation.Status == LocalCommitStatus.Completed
                && ((byte[])(object)row.Operation.CommitSha).Length > ObjectIdByteBound,
            row.Operation.ChangedPathCount >= 1
                && row.Operation.ChangedPathCount <= LocalCommitOperation.MaximumChangedPaths
                && row.Operation.ChangedPathCount % Int32Maximum == row.Operation.ChangedPathCount
                ? row.Operation.ChangedPathCount
                : null,
            row.Operation.TotalBytes >= 0
                && row.Operation.TotalBytes <= LocalCommitOperation.MaximumTotalBytes
                && row.Operation.TotalBytes % SafeIntegerModulus == row.Operation.TotalBytes
                ? row.Operation.TotalBytes
                : null,
            ((byte[])(object)row.Operation.CreatedAtUtc).Length <= TimeBytes ? (byte[])(object)row.Operation.CreatedAtUtc : null,
            ((byte[])(object)row.Operation.ExecutionStartedAtUtc!).Length <= TimeBytes
                ? (byte[])(object)row.Operation.ExecutionStartedAtUtc
                : null,
            row.Operation.ExecutionStartedAtUtc != null,
            ((byte[])(object)row.Operation.CompletedAtUtc!).Length <= TimeBytes ? (byte[])(object)row.Operation.CompletedAtUtc : null,
            row.Operation.CompletedAtUtc != null,
            ((byte[])(object)row.Operation.OutcomeReasonCode!).Length <= ReasonByteBound ? row.Operation.OutcomeReasonCode : null,
            row.Operation.OutcomeReasonCode != null,
            row.Operation.OutcomeReasonCode != null && ((byte[])(object)row.Operation.OutcomeReasonCode).Length > ReasonByteBound,
            ((byte[])(object)row.Workspace.Id).Length <= IdentifierBytes ? (byte[])(object)row.Workspace.Id : null,
            ((byte[])(object)row.Workspace.ProjectId).Length <= IdentifierBytes ? (byte[])(object)row.Workspace.ProjectId : null,
            ((byte[])(object)row.Workspace.BranchName).Length <= BranchNameByteBound ? row.Workspace.BranchName : null,
            row.Workspace.BranchName != null && ((byte[])(object)row.Workspace.BranchName).Length > BranchNameByteBound,
            ((byte[])(object)row.Checkpoint.Id).Length <= IdentifierBytes ? (byte[])(object)row.Checkpoint.Id : null,
            ((byte[])(object)row.Checkpoint.WorkspaceId).Length <= IdentifierBytes ? (byte[])(object)row.Checkpoint.WorkspaceId : null,
            row.Checkpoint.CheckpointNumber >= 1
                && row.Checkpoint.CheckpointNumber <= int.MaxValue
                && (row.Checkpoint.CheckpointNumber == Int32Maximum || row.Checkpoint.CheckpointNumber % Int32Maximum == row.Checkpoint.CheckpointNumber)
                ? row.Checkpoint.CheckpointNumber
                : null,
            ((byte[])(object)row.Checkpoint.FingerprintSha256).Length <= FingerprintByteBound ? row.Checkpoint.FingerprintSha256 : null,
            row.Checkpoint.FingerprintSha256 != null && ((byte[])(object)row.Checkpoint.FingerprintSha256).Length > FingerprintByteBound,
            ((byte[])(object)row.Checkpoint.HeadCommitSha).Length <= ObjectIdByteBound ? row.Checkpoint.HeadCommitSha : null,
            row.Checkpoint.HeadCommitSha != null && ((byte[])(object)row.Checkpoint.HeadCommitSha).Length > ObjectIdByteBound));
}
