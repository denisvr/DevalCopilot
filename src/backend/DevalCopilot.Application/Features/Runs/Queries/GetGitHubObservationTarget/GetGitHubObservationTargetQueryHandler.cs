using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubObservationTarget;

public sealed class GetGitHubObservationTargetQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetGitHubObservationTargetQuery, Result<GetGitHubObservationTargetQueryResult>>
{
    public async Task<Result<GetGitHubObservationTargetQueryResult>> HandleAsync(
        GetGitHubObservationTargetQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubObservationTargetQueryResult>.Failure(GitHubObservationTargetErrors.NotFound());
        }

        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        var reading = await GitHubObservationTargetReader.ReadAsync(dbContext, query.RunId, source, cancellationToken);

        var result = reading switch
        {
            { Kind: GitHubObservationTargetReadingKind.None } when source is not null => new GetGitHubObservationTargetQueryResult(
                query.RunId, GitHubObservationTargetState.NotRecorded, source.ToView(), null),
            { Kind: GitHubObservationTargetReadingKind.None } => new GetGitHubObservationTargetQueryResult(
                query.RunId, GitHubObservationTargetState.NotRecorded, null, null),
            { Kind: GitHubObservationTargetReadingKind.Coherent, Target: { } target } when source is not null =>
                new GetGitHubObservationTargetQueryResult(
                    query.RunId, GitHubObservationTargetState.Available, source.ToView(), target),
            _ => new GetGitHubObservationTargetQueryResult(query.RunId, GitHubObservationTargetState.Unavailable, null, null),
        };

        return Result<GetGitHubObservationTargetQueryResult>.Success(result);
    }
}
