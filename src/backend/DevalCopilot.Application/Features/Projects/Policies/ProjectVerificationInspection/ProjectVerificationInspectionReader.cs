using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// Proves the exact historical ownership and result facts of one project verification execution from bounded scalar reads (ADR-0069), for
/// the metadata inspection and every sealed window, which deliberately change together. Membership is a database predicate over the stored
/// identities and the registered project, decided before any selected fact is interpreted, so a damaged row elsewhere cannot make a foreign,
/// absent or orphaned (project row absent) execution look
/// like a member. The ownership and result facts of a member then come from one bounded relational statement over the execution, its project, the
/// workspace and the checkpoint it names: the workspace belongs to the project, the checkpoint to that workspace and its fingerprint is the
/// claim fingerprint, and the complete supported status, outcome, time, exit-code and completion-fingerprint tuple is coherent. The recipe
/// is a recorded snapshot and is never read; no latest or current execution, workspace or checkpoint, lease, eligibility, Agent
/// membership, local sequence, Git, filesystem, process or provider reader is consulted. It is unsigned consistency of the facts read now,
/// writes nothing and swallows no fault.
/// </summary>
internal static class ProjectVerificationInspectionReader
{
    private const int MaximumCommandNameLength = 200;

    public static async Task<ProjectVerificationInspectionReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid projectId, Guid executionId, CancellationToken cancellationToken)
    {
        // The registered project itself is part of the proof: retained executions of an absent project are never members.
        var member = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => execution.ProjectId == projectId && execution.Id == executionId)
            .Join(dbContext.Projects.AsNoTracking(), execution => execution.ProjectId, project => project.Id, (execution, project) => execution.Id)
            .AnyAsync(cancellationToken);
        if (!member)
        {
            return ProjectVerificationInspectionReading.NotFound;
        }

        // At most one by the primary key; observing a second is damaged storage, never a choice between two.
        var rows = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => execution.ProjectId == projectId && execution.Id == executionId)
            .ProjectExecution(dbContext.Projects.AsNoTracking(), dbContext.GitWorkspaces.AsNoTracking(), dbContext.GitCheckpoints.AsNoTracking())
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return rows.Length == 1 && Prove(rows[0], projectId, executionId) is { } execution
            ? ProjectVerificationInspectionReading.Recorded(execution)
            : ProjectVerificationInspectionReading.Unavailable;
    }

    private static ProjectVerificationRecordedExecution? Prove(ProjectVerificationExecutionRow row, Guid projectId, Guid executionId)
    {
        var workspaceId = ProjectVerificationInspectionStorage.ParseIdentifier(row.WorkspaceId);
        var checkpointId = ProjectVerificationInspectionStorage.ParseIdentifier(row.CheckpointId);
        var commandId = ProjectVerificationInspectionStorage.ParseIdentifier(row.CommandId);
        var claimedAt = ProjectVerificationInspectionStorage.ParseUtc(row.ClaimedAtUtc);
        var dispatchedAt = ProjectVerificationInspectionStorage.ParseUtc(row.DispatchedAtUtc);
        var completedAt = ProjectVerificationInspectionStorage.ParseUtc(row.CompletedAtUtc);
        if (ProjectVerificationInspectionStorage.ParseIdentifier(row.Id) != executionId
            || ProjectVerificationInspectionStorage.ParseIdentifier(row.ProjectId) != projectId
            || workspaceId is not { } workspace
            || checkpointId is not { } checkpoint
            || commandId is not { } command
            || row.Number is not { } number
            || row.CheckpointNumber is not { } checkpointNumber
            || claimedAt is not { } claimed
            || (row.DispatchedPresent && dispatchedAt is null)
            || (row.CompletedPresent && completedAt is null)
            || !IsCommandName(row)
            || row.ClaimFingerprint is not { } claimFingerprint)
        {
            return null;
        }

        // The recorded workspace and checkpoint by identity, never the project's latest: the workspace belongs to the project, the
        // checkpoint to that workspace, and its fingerprint is the one the execution claimed against.
        if (ProjectVerificationInspectionStorage.ParseIdentifier(row.WorkspaceRowId) != workspace
            || ProjectVerificationInspectionStorage.ParseIdentifier(row.WorkspaceProjectId) != projectId
            || ProjectVerificationInspectionStorage.ParseIdentifier(row.CheckpointRowId) != checkpoint
            || ProjectVerificationInspectionStorage.ParseIdentifier(row.CheckpointWorkspaceId) != workspace
            || row.CheckpointFingerprintOversized
            || !string.Equals(row.CheckpointFingerprint, claimFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return ProjectVerificationExecutionTuple.TryClassify(row, claimed, dispatchedAt, completedAt, out var status, out var outcome)
            ? new ProjectVerificationRecordedExecution(
                number,
                command,
                row.CommandName!,
                workspace,
                checkpoint,
                checkpointNumber,
                claimFingerprint,
                row.CompletionFingerprint,
                status,
                outcome,
                row.ExitCode,
                claimed,
                dispatchedAt,
                completedAt)
            : null;
    }

    private static bool IsCommandName(ProjectVerificationExecutionRow row) =>
        !row.CommandNameOversized
        && !string.IsNullOrWhiteSpace(row.CommandName)
        && row.CommandName.Length <= MaximumCommandNameLength;
}
