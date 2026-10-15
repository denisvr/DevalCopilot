using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;

public sealed class GetGitHubBranchPublicationApprovalQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetGitHubBranchPublicationApprovalQuery, Result<GetGitHubBranchPublicationApprovalQueryResult>>
{
    public async Task<Result<GetGitHubBranchPublicationApprovalQueryResult>> HandleAsync(
        GetGitHubBranchPublicationApprovalQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubBranchPublicationApprovalQueryResult>.Failure(GitHubBranchPublicationApprovalErrors.NotFound());
        }

        var scopeReading = await GitHubBranchPublicationScopeReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        var approval = await GitHubBranchPublicationApprovalReader.ReadAsync(dbContext, query.RunId, scopeReading, cancellationToken);

        var result = (scopeReading.Scope, approval.Kind) switch
        {
            _ when scopeReading.IsIncoherent || approval.Kind == GitHubBranchPublicationApprovalReadingKind.Incoherent =>
                Unavailable(query.RunId),
            ({ } scope, GitHubBranchPublicationApprovalReadingKind.Coherent) when approval.Approval is { } recorded =>
                new GetGitHubBranchPublicationApprovalQueryResult(
                    query.RunId, GitHubBranchPublicationApprovalStatus.Available, scope.Source.ToView(), scope.Target, scope.Binding,
                    recorded),
            ({ } scope, GitHubBranchPublicationApprovalReadingKind.None) =>
                new GetGitHubBranchPublicationApprovalQueryResult(
                    query.RunId, GitHubBranchPublicationApprovalStatus.ApprovalNotRecorded, scope.Source.ToView(), scope.Target,
                    scope.Binding, null),
            (null, GitHubBranchPublicationApprovalReadingKind.None) =>
                new GetGitHubBranchPublicationApprovalQueryResult(
                    query.RunId, GitHubBranchPublicationApprovalStatus.NotRecorded, null, null, null, null),
            _ => Unavailable(query.RunId),
        };

        return Result<GetGitHubBranchPublicationApprovalQueryResult>.Success(result);
    }

    private static GetGitHubBranchPublicationApprovalQueryResult Unavailable(Guid runId) =>
        new(runId, GitHubBranchPublicationApprovalStatus.Unavailable, null, null, null, null);
}
