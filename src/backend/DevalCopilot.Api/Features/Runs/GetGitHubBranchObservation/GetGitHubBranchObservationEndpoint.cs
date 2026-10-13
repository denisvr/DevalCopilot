using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchObservation;

/// <summary>
/// Protected, bodyless observation of the GitHub repository identity and the exact recorded branch tip of a run's recorded ADR-0040
/// target (ADR-0042). It accepts no repository, node ID, branch, commit, executable or credential and refuses any request body before
/// anything is observed. An unknown run is the fixed 404. It proves no CI result, permission, durable binding or publication approval.
/// </summary>
public sealed class GetGitHubBranchObservationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-branch-observation")]
    [ProducesResponseType<GetGitHubBranchObservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubBranchObservationResponse>> GetGitHubBranchObservation(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        if (await HasBodyAsync(cancellationToken))
        {
            return problemDetails.CreateResponse(Result.Failure(GitHubBranchObservationErrors.BodyNotAllowed()), HttpContext);
        }

        var result = await mediator.SendAsync(new GetGitHubBranchObservationQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubBranchObservationResponse(
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
            value.Observation is null
                ? null
                : new GitHubBranchObservationResponse(
                    value.Observation.RepositoryNodeId,
                    value.Observation.Owner,
                    value.Observation.Repository,
                    value.Observation.ObservedAtUtc,
                    value.Observation.Ref is null
                        ? null
                        : new GitHubBranchRefResponse(value.Observation.Ref.BranchName, value.Observation.Ref.CommitSha))));
    }

    /// <summary>A declared length, a transfer encoding or any readable byte is a body; one byte is peeked so an undeclared body is seen too.</summary>
    private async Task<bool> HasBodyAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.Request;
        if (request.ContentLength is > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding))
        {
            return true;
        }

        return await request.Body.ReadAsync(new byte[1], cancellationToken) > 0;
    }
}
