using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetWorkspaceExecutionStatus;

public sealed class GetWorkspaceExecutionStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetWorkspaceExecutionStatusQuery, Result<GetWorkspaceExecutionStatusQueryResult>>
{
    public async Task<Result<GetWorkspaceExecutionStatusQueryResult>> HandleAsync(
        GetWorkspaceExecutionStatusQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AsNoTracking().AnyAsync(candidate => candidate.Id == query.ProjectId, cancellationToken))
        {
            return Result<GetWorkspaceExecutionStatusQueryResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var assessment = await WorkspaceExecutionPolicy.AssessLatestAsync(dbContext, query.ProjectId, cancellationToken);
        return Result<GetWorkspaceExecutionStatusQueryResult>.Success(new GetWorkspaceExecutionStatusQueryResult(
            query.ProjectId, assessment.WorkspaceId, assessment.Status, ReasonCodeOf(assessment.Status)));
    }

    /// <summary>The closed reason code of each status (ADR-0057's tuple table).</summary>
    private static string? ReasonCodeOf(WorkspaceExecutionStatus status) => status switch
    {
        WorkspaceExecutionStatus.Idle => null,
        WorkspaceExecutionStatus.NoWorkspace => "workspace_not_prepared",
        WorkspaceExecutionStatus.AgentAttemptActive => "agent_attempt_active",
        WorkspaceExecutionStatus.VerificationActive => "verification_active",
        WorkspaceExecutionStatus.CommitReserved => "local_commit_reserved",
        WorkspaceExecutionStatus.Conflicted => "conflicting_execution_owners",
        _ => "workspace_execution_unavailable",
    };
}
