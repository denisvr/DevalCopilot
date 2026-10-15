using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryPermissionObservation;

/// <summary>
/// Protected, bodyless observation of the repository permission GitHub reports for the stored default authentication on the repository of
/// a run whose identity is bound (ADR-0049). It accepts no query, repository, node ID, branch, commit, executable or credential and
/// refuses any query key or request body before anything is dispatched or observed. An unknown run is the fixed 404. The answer is
/// reported repository access for one moment, never permission to push a branch, readiness, an approval or a publication authority.
/// </summary>
public sealed class GetGitHubRepositoryPermissionObservationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-repository-permission-observation")]
    [TypeFilter(typeof(GitHubRepositoryPermissionObservationReadGuardFilter))]
    [ProducesResponseType<GetGitHubRepositoryPermissionObservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubRepositoryPermissionObservationResponse>> GetGitHubRepositoryPermissionObservation(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitHubRepositoryPermissionObservationQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubRepositoryPermissionObservationResponse(
            value.RunId,
            value.Status.ToString(),
            value.Source is null
                ? null
                : new GitHubObservationSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            value.Target is null
                ? null
                : new GitHubObservationScopeResponse(
                    value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                    value.Target.RecordedAtUtc),
            value.Binding is null
                ? null
                : new GitHubRepositoryIdentityBindingResponse(
                    value.Binding.Id, value.Binding.EventId, value.Binding.RepositoryNodeId, value.Binding.Owner,
                    value.Binding.Repository, value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc),
            value.Observation is null
                ? null
                : new GitHubRepositoryPermissionObservationResponse(
                    value.Observation.RepositoryNodeId,
                    value.Observation.Owner,
                    value.Observation.Repository,
                    value.Observation.ObservedAtUtc,
                    value.Observation.ViewerPermission)));
    }
}
