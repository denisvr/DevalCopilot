using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// Proves the exact recorded local-commit operation of one run from one bounded scalar statement (ADR-0070): the registered run and its
/// registered project, the operation associated by the unique run key and that operation's named workspace and named checkpoint are read
/// together as left joins and then proved in memory. Absence is decided only by the run-keyed association, never by a failure to parse an
/// identity or by a filtered-out row, so a present but damaged (or doubled) operation is Unavailable and never NotRecorded. The run must
/// be a ManualAgent run of the project, the operation must belong to that run and project, the workspace to the project with exactly the
/// operation's branch, and the checkpoint to that workspace with exactly the operation's pinned number, fingerprint and parent head; the
/// complete status, time and reason tuple is coherent. Current workspace state, the latest checkpoint, lease state, run lifecycle and
/// stage, authority members, approvals, digests, artifacts, Git, files, processes and providers are never consulted. It is unsigned
/// consistency of the facts read now, writes nothing and swallows no fault.
/// </summary>
internal static class RecordedLocalCommitReader
{
    public static async Task<RecordedLocalCommitReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        // At most one by the unique run index; observing a second is damaged storage, never a choice between two.
        var rows = await (
            from run in dbContext.Runs.AsNoTracking()
            where run.Id == runId
            join project in dbContext.Projects.AsNoTracking() on run.ProjectId equals project.Id
            join operation in dbContext.LocalCommitOperations.AsNoTracking() on run.Id equals operation.RunId into operationGroup
            from operation in operationGroup.DefaultIfEmpty()
            join workspace in dbContext.GitWorkspaces.AsNoTracking()
                on (Guid?)operation.GitWorkspaceId equals (Guid?)workspace.Id into workspaceGroup
            from workspace in workspaceGroup.DefaultIfEmpty()
            join checkpoint in dbContext.GitCheckpoints.AsNoTracking()
                on (Guid?)operation.GitCheckpointId equals (Guid?)checkpoint.Id into checkpointGroup
            from checkpoint in checkpointGroup.DefaultIfEmpty()
            select new RecordedLocalCommitJoin
            {
                Run = run,
                Project = project,
                Operation = operation,
                Workspace = workspace,
                Checkpoint = checkpoint,
            })
            .Project()
            .Take(2)
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return RecordedLocalCommitReading.NotFound;
        }

        if (rows.Length > 1)
        {
            return RecordedLocalCommitReading.Unavailable;
        }

        if (!rows[0].OperationPresent)
        {
            return RecordedLocalCommitReading.NotRecorded;
        }

        return Prove(rows[0], runId) is { } operationFacts
            ? RecordedLocalCommitReading.Recorded(operationFacts)
            : RecordedLocalCommitReading.Unavailable;
    }

    private static RecordedLocalCommitOperation? Prove(RecordedLocalCommitRow row, Guid runId)
    {
        var projectId = RecordedLocalCommitStorage.ParseIdentifier(row.ProjectId);
        var operationId = RecordedLocalCommitStorage.ParseIdentifier(row.OperationId);
        var workspaceId = RecordedLocalCommitStorage.ParseIdentifier(row.OperationWorkspaceId);
        var checkpointId = RecordedLocalCommitStorage.ParseIdentifier(row.OperationCheckpointId);
        var created = RecordedLocalCommitStorage.ParseUtc(row.CreatedAtUtc);
        var started = RecordedLocalCommitStorage.ParseUtc(row.ExecutionStartedAtUtc);
        var completed = RecordedLocalCommitStorage.ParseUtc(row.CompletedAtUtc);
        if (RecordedLocalCommitStorage.ParseIdentifier(row.RunId) != runId
            || RecordedLocalCommitStorage.ParseIdentifier(row.RunProjectId) != projectId
            || RunExecutionModeStorage.Read(row.StoredMode) != RunExecutionMode.ManualAgent
            || projectId is not { } project
            || operationId is not { } operation
            || workspaceId is not { } workspace
            || checkpointId is not { } checkpoint
            || RecordedLocalCommitStorage.ParseIdentifier(row.OperationRunId) != runId
            || RecordedLocalCommitStorage.ParseIdentifier(row.OperationProjectId) != project
            || row.CheckpointNumber is not { } checkpointNumber
            || row.ChangedPathCount is not { } changedPathCount
            || row.TotalBytes is not { } totalBytes
            || created is not { } createdAt
            || (row.ExecutionStartedPresent && started is null)
            || (row.CompletedPresent && completed is null))
        {
            return null;
        }

        // The named workspace and checkpoint by identity, never the project's latest or current: the workspace belongs to the project and
        // has exactly the operation's branch, and the checkpoint belongs to that workspace with exactly the pinned number, fingerprint
        // and parent head.
        if (RecordedLocalCommitStorage.ParseIdentifier(row.WorkspaceId) != workspace
            || RecordedLocalCommitStorage.ParseIdentifier(row.WorkspaceProjectId) != project
            || RecordedLocalCommitStorage.ParseIdentifier(row.CheckpointId) != checkpoint
            || RecordedLocalCommitStorage.ParseIdentifier(row.CheckpointWorkspaceId) != workspace
            || row.CheckpointRowNumber != checkpointNumber
            || row.BranchNameOversized
            || row.WorkspaceBranchNameOversized
            || !RecordedLocalCommitStorage.IsBranchName(row.BranchName)
            || !string.Equals(row.WorkspaceBranchName, row.BranchName, StringComparison.Ordinal)
            || row.CheckpointFingerprintOversized
            || row.CheckpointRowFingerprintOversized
            || !RecordedLocalCommitStorage.IsFingerprint(row.CheckpointFingerprint)
            || !string.Equals(row.CheckpointRowFingerprint, row.CheckpointFingerprint, StringComparison.Ordinal)
            || row.ParentCommitShaOversized
            || row.CheckpointHeadCommitShaOversized
            || !RecordedLocalCommitStorage.IsObjectId(row.ParentCommitSha)
            || !string.Equals(row.CheckpointHeadCommitSha, row.ParentCommitSha, StringComparison.Ordinal)
            || row.TreeShaOversized
            || !RecordedLocalCommitStorage.IsObjectId(row.TreeSha))
        {
            return null;
        }

        if (!RecordedLocalCommitTuple.TryClassify(row, createdAt, started, completed, out var status, out var reason))
        {
            return null;
        }

        return new RecordedLocalCommitOperation(
            operation,
            project,
            workspace,
            checkpoint,
            checkpointNumber,
            row.CheckpointFingerprint!,
            status,
            row.BranchName!,
            row.ParentCommitSha!,
            row.TreeSha!,
            status == LocalCommitStatus.Completed ? row.CommitSha : null,
            changedPathCount,
            totalBytes,
            createdAt,
            started,
            completed,
            reason);
    }
}
