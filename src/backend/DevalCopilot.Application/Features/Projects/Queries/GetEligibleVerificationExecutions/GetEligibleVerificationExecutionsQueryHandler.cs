using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;

public sealed class GetEligibleVerificationExecutionsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleVerificationExecutionsQuery, IReadOnlyList<EligibleVerificationExecution>>
{
    public async Task<IReadOnlyList<EligibleVerificationExecution>> HandleAsync(
        GetEligibleVerificationExecutionsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.VerificationExecutions
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
                    candidate.execution.TimeoutSeconds))
            .ToListAsync(cancellationToken);
    }
}
