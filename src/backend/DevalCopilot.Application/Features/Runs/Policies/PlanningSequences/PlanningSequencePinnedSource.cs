using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

/// <summary>
/// Whether the exact source a planning sequence was armed for still holds (ADR-0055): the run is still a manual Agent run with the
/// bound objective, and the project's latest workspace is still the pinned, Ready, owned one with its active lease and its latest
/// checkpoint and fingerprint. A newer workspace or checkpoint grants no continuation and is never followed automatically.
/// </summary>
internal static class PlanningSequencePinnedSource
{
    public static async Task<bool> HoldsAsync(
        IDevalCopilotDbContext dbContext, PlanningSequence sequence, Run run, CancellationToken cancellationToken)
    {
        if (run.Id != sequence.RunId
            || run.ProjectId != sequence.ProjectId
            || !string.Equals(PlanningSequenceObjective.Hash(run.Objective), sequence.ObjectiveSha256, StringComparison.Ordinal)
            || await CurrentRunExecutionMode.ReadAsync(dbContext, run.Id, cancellationToken) != RunExecutionMode.ManualAgent
            || run.Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            return false;
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Id != sequence.WorkspaceId || workspace.Status != WorkspaceStatus.Ready)
        {
            return false;
        }

        if (!await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return false;
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return checkpoint is not null
            && checkpoint.Id == sequence.CheckpointId
            && string.Equals(checkpoint.FingerprintSha256, sequence.FingerprintSha256, StringComparison.Ordinal);
    }
}
