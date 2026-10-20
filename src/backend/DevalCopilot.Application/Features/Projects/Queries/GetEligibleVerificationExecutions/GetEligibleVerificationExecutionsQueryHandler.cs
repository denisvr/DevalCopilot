using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;

public sealed class GetEligibleVerificationExecutionsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleVerificationExecutionsQuery, IReadOnlyList<EligibleVerificationExecution>>
{
    public async Task<IReadOnlyList<EligibleVerificationExecution>> HandleAsync(
        GetEligibleVerificationExecutionsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.VerificationExecutions
            .Where(execution => execution.Status == VerificationExecutionStatus.Running && execution.DispatchedAtUtc == null)
            .Join(
                dbContext.GitWorkspaces.Where(workspace => workspace.Status == WorkspaceStatus.Ready),
                execution => execution.GitWorkspaceId,
                workspace => workspace.Id,
                (execution, workspace) => new { execution, workspace })
            .Join(
                dbContext.RepositoryMutationLeases.Where(lease => lease.Status == LeaseStatus.Active),
                candidate => candidate.workspace.Id,
                lease => lease.WorkspaceId,
                (candidate, _) => new EligibleVerificationExecution(
                    candidate.execution.Id,
                    candidate.execution.CheckpointFingerprintSha256,
                    candidate.execution.ExecutablePath,
                    candidate.execution.Arguments,
                    candidate.execution.WorkspacePath,
                    candidate.execution.TimeoutSeconds,
                    candidate.execution.ProjectId,
                    candidate.execution.GitWorkspaceId,
                    candidate.execution.GitCheckpointId,
                    candidate.execution.VerificationCommandId))
            .ToListAsync(cancellationToken);

        // Advisory (ADR-0057): a pending execution that something else owns the workspace beside is not proposed, so the supervisor does
        // not capture Git for it on every poll. The dispatch gate re-decides this under its own write lock.
        var eligible = new List<EligibleVerificationExecution>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await WorkspaceExecutionFence.CheckVerificationDispatchAsync(
                    dbContext, candidate.ProjectId, candidate.WorkspaceId, candidate.VerificationExecutionId, cancellationToken) is null)
            {
                eligible.Add(candidate);
            }
        }

        return eligible;
    }
}
