using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>
/// Resolves the one exact source of a GitHub observation scope from persisted rows (ADR-0040): a ManualAgent run with an Available
/// ADR-0032 receipt (read by the unchanged <see cref="LocalDeliveryReceiptReader"/>), the pinned operation and workspace rows, and a
/// workspace branch that the recorded workspace number reproduces through <see cref="WorkspaceBranchNamePolicy"/>. Every read is
/// fresh and untracked and nothing is looked up as "latest" or "current". Anything missing, foreign or contradictory is
/// <see langword="null"/>: the source is then simply not admissible. No Git, filesystem, process or provider is touched.
/// </summary>
internal static class GitHubObservationSourceReader
{
    public static async Task<GitHubObservationSource?> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking()
            .Where(candidate => candidate.Id == runId)
            .Select(candidate => new { candidate.ProjectId, candidate.Objective })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null || await CurrentRunExecutionMode.ReadAsync(dbContext, runId, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return null;
        }

        var receipt = await new LocalDeliveryReceiptReader(dbContext).ReadAsync(runId, run.ProjectId, run.Objective, cancellationToken);
        if (receipt is not { State: LocalDeliveryReceiptState.Available, Receipt: { } view } || view.RunId != runId
            || !GitHubObservationTarget.IsCommitSha(view.CommitSha) || string.IsNullOrWhiteSpace(view.BranchName)
            || view.BranchName.Length > GitHubObservationTarget.MaximumBranchNameLength)
        {
            return null;
        }

        var operation = await dbContext.LocalCommitOperations.AsNoTracking()
            .Where(candidate => candidate.Id == view.OperationId)
            .Select(candidate => new
            {
                candidate.RunId,
                candidate.ProjectId,
                candidate.GitWorkspaceId,
                candidate.CommitSha,
                candidate.BranchName,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null || operation.RunId != runId || operation.ProjectId != run.ProjectId
            || !string.Equals(operation.CommitSha, view.CommitSha, StringComparison.Ordinal)
            || !string.Equals(operation.BranchName, view.BranchName, StringComparison.Ordinal))
        {
            return null;
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.Id == operation.GitWorkspaceId)
            .Select(candidate => new { candidate.ProjectId, candidate.WorkspaceNumber, candidate.BranchName })
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.ProjectId != run.ProjectId || workspace.WorkspaceNumber < 1
            || !string.Equals(workspace.BranchName, view.BranchName, StringComparison.Ordinal)
            || !string.Equals(
                WorkspaceBranchNamePolicy.Compute(run.ProjectId, workspace.WorkspaceNumber), view.BranchName, StringComparison.Ordinal))
        {
            return null;
        }

        return new GitHubObservationSource(runId, run.ProjectId, view.OperationId, operation.GitWorkspaceId, view.CommitSha, view.BranchName);
    }
}
