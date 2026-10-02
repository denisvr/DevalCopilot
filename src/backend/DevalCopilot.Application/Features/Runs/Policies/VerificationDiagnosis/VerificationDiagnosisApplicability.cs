using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// The fresh, untracked answer to "does this claimed verification diagnosis still apply?", shared by dispatch and result
/// recording (ADR-0018). It re-reads the run, workspace, lease, current checkpoint, the diagnosed ExecutionReport's
/// Implementer chain (so the true ImplementedPlan is re-resolved through the validated lineage), and the complete
/// verification selection, and compares the selection to the memberships the attempt pinned at claim: the ordered (command, execution) pairs and the
/// snapshot digest of every command, execution, and sealed failed-output fact (a diagnosis membership without a valid digest
/// fails closed), so a changed hash, length, path, capture flag, or coherent execution metadata is detected even when the
/// identifiers are unchanged. It reads nothing from a tracked
/// entity, so an earlier projection confers no authority.
/// </summary>
internal static class VerificationDiagnosisApplicability
{
    internal enum Verdict
    {
        /// <summary>Everything the claim decided against still holds.</summary>
        Applicable,

        /// <summary>The run, workspace, lease, or current checkpoint no longer permits this attempt (ordinary eligibility).</summary>
        WorkspaceNoLongerEligible,

        /// <summary>The report chain, the enabled set, a latest execution, or the failed output the claim pinned changed.</summary>
        VerificationEvidenceChanged,
    }

    public static async Task<Verdict> EvaluateAsync(
        IDevalCopilotDbContext dbContext, Attempt attempt, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == attempt.RunId, cancellationToken);
        var workspace = attempt.AgentGitWorkspaceId is { } workspaceId
            ? await dbContext.GitWorkspaces.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == workspaceId, cancellationToken)
            : null;
        if (run is null || run.Lifecycle != RunLifecycle.Running || workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Verdict.WorkspaceNoLongerEligible;
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases.AsNoTracking()
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        var currentCheckpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(checkpoint => checkpoint.WorkspaceId == workspace.Id)
            .OrderByDescending(checkpoint => checkpoint.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (!leaseIsActive
            || currentCheckpoint is null
            || currentCheckpoint.Id != attempt.AgentGitCheckpointId
            || !string.Equals(currentCheckpoint.FingerprintSha256, attempt.AgentCheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return Verdict.WorkspaceNoLongerEligible;
        }

        var reportId = await VerificationDiagnosisInputIdentity.ReadPinnedExecutionReportIdAsync(dbContext, attempt.Id, cancellationToken);
        var pinned = await VerificationDiagnosisInputIdentity.ReadPinnedMembershipsAsync(dbContext, attempt.Id, cancellationToken);
        if (reportId is null || pinned.Count == 0 || pinned.Any(row => !AttemptVerificationEvidence.IsValidSnapshotDigest(row.Snapshot)))
        {
            return Verdict.VerificationEvidenceChanged;
        }

        try
        {
            var report = await dbContext.CollaborationMessages.AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == reportId.Value && message.RunId == run.Id, cancellationToken);
            var chain = report is null
                ? null
                : await ImplementerExecutionReportEligibility.ResolveAsync(
                    dbContext, report, run.Id, workspace.Id, currentCheckpoint.Id, cancellationToken);
            var selection = await VerificationDiagnosisEvidence.ReadAsync(
                dbContext, run.ProjectId, workspace.Id, currentCheckpoint, asNoTracking: true, cancellationToken);
            return chain is not null && selection.Value is { } current && current.OrderedPairs.SequenceEqual(pinned.Select(row => (row.CommandId, row.ExecutionId)))
                && current.OrderedSnapshots.SequenceEqual(pinned.Select(row => row.Snapshot!))
                ? Verdict.Applicable
                : Verdict.VerificationEvidenceChanged;
        }
        catch (InvalidOperationException)
        {
            // A persisted row could not be materialized (typically an unparseable stored enum string): fail closed.
            return Verdict.VerificationEvidenceChanged;
        }
    }
}
