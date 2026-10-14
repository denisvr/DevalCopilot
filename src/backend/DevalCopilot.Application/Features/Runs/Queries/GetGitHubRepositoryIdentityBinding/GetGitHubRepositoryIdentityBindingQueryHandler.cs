using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;

public sealed class GetGitHubRepositoryIdentityBindingQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetGitHubRepositoryIdentityBindingQuery, Result<GetGitHubRepositoryIdentityBindingQueryResult>>
{
    public async Task<Result<GetGitHubRepositoryIdentityBindingQueryResult>> HandleAsync(
        GetGitHubRepositoryIdentityBindingQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetGitHubRepositoryIdentityBindingQueryResult>.Failure(GitHubRepositoryIdentityBindingErrors.NotFound());
        }

        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        var targetReading = await GitHubObservationTargetReader.ReadAsync(dbContext, query.RunId, source, cancellationToken);
        var target = targetReading.Kind == GitHubObservationTargetReadingKind.Coherent ? targetReading.Target : null;
        var binding = await GitHubRepositoryIdentityBindingReader.ReadAsync(dbContext, query.RunId, source, target, cancellationToken);

        var result = (targetReading.Kind, binding) switch
        {
            (GitHubObservationTargetReadingKind.None, { Kind: GitHubRepositoryIdentityBindingReadingKind.None }) =>
                new GetGitHubRepositoryIdentityBindingQueryResult(
                    query.RunId, GitHubRepositoryIdentityBindingState.NotRecorded, null, null, null),
            (GitHubObservationTargetReadingKind.Coherent, { Kind: GitHubRepositoryIdentityBindingReadingKind.None })
                when source is not null && target is not null =>
                new GetGitHubRepositoryIdentityBindingQueryResult(
                    query.RunId, GitHubRepositoryIdentityBindingState.NotRecorded, source.ToView(), target, null),
            (GitHubObservationTargetReadingKind.Coherent, { Kind: GitHubRepositoryIdentityBindingReadingKind.Coherent, Binding: { } recorded })
                when source is not null && target is not null =>
                new GetGitHubRepositoryIdentityBindingQueryResult(
                    query.RunId, GitHubRepositoryIdentityBindingState.Available, source.ToView(), target, recorded),
            _ => new GetGitHubRepositoryIdentityBindingQueryResult(
                query.RunId, GitHubRepositoryIdentityBindingState.Unavailable, null, null, null),
        };

        return Result<GetGitHubRepositoryIdentityBindingQueryResult>.Success(result);
    }
}
