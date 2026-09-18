using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;

public sealed class GetIneligibleAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetIneligibleAgentAttemptsQuery, IReadOnlyList<IneligibleAgentAttempt>>
{
    public async Task<IReadOnlyList<IneligibleAgentAttempt>> HandleAsync(
        GetIneligibleAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent
                && attempt.Status == AttemptStatus.Running
                && attempt.AgentDispatchedAtUtc == null)
            .Join(
                dbContext.Runs.AsNoTracking().Where(run => run.Lifecycle == RunLifecycle.Running),
                attempt => attempt.RunId,
                run => run.Id,
                (attempt, run) => new
                {
                    attempt.Id,
                    attempt.RunId,
                    GitWorkspaceId = attempt.AgentGitWorkspaceId!.Value,
                    GitCheckpointId = attempt.AgentGitCheckpointId!.Value,
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var workspaceIds = candidates.Select(candidate => candidate.GitWorkspaceId).Distinct().ToArray();

        var workspaceStatusById = await dbContext.GitWorkspaces
            .AsNoTracking()
            .Where(workspace => workspaceIds.Contains(workspace.Id))
            .Select(workspace => new { workspace.Id, workspace.Status })
            .ToDictionaryAsync(workspace => workspace.Id, workspace => workspace.Status, cancellationToken);

        var activeLeaseWorkspaceIds = (await dbContext.RepositoryMutationLeases
                .AsNoTracking()
                .Where(lease => workspaceIds.Contains(lease.WorkspaceId) && lease.Status == LeaseStatus.Active)
                .Select(lease => lease.WorkspaceId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var checkpoints = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(checkpoint => workspaceIds.Contains(checkpoint.WorkspaceId))
            .Select(checkpoint => new { checkpoint.WorkspaceId, checkpoint.Id, checkpoint.CheckpointNumber })
            .ToListAsync(cancellationToken);
        var currentCheckpointByWorkspace = checkpoints
            .GroupBy(checkpoint => checkpoint.WorkspaceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(checkpoint => checkpoint.CheckpointNumber).First().Id);

        return candidates
            .Where(candidate =>
                !workspaceStatusById.TryGetValue(candidate.GitWorkspaceId, out var status)
                || status != WorkspaceStatus.Ready
                || !activeLeaseWorkspaceIds.Contains(candidate.GitWorkspaceId)
                || !currentCheckpointByWorkspace.TryGetValue(candidate.GitWorkspaceId, out var currentCheckpointId)
                || currentCheckpointId != candidate.GitCheckpointId)
            .Select(candidate => new IneligibleAgentAttempt(candidate.RunId, candidate.Id))
            .ToArray();
    }
}
