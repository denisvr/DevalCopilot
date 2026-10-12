using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubObservationTarget;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetGitHubObservationTarget;

/// <summary>The recorded, read-only GitHub observation scope of a run's completed local delivery (ADR-0040). An unknown run is the
/// fixed 404. It reads persisted rows only: no write, lock, Git, process, network or provider, and it infers nothing from an origin or
/// authentication observation.</summary>
public sealed class GetGitHubObservationTargetEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-observation-target")]
    [ProducesResponseType<GetGitHubObservationTargetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubObservationTargetResponse>> GetGitHubObservationTarget(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitHubObservationTargetQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubObservationTargetResponse(
            value.RunId,
            value.State.ToString(),
            value.Source is null
                ? null
                : new GitHubObservationSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            value.Target is null
                ? null
                : new GitHubObservationScopeResponse(
                    value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                    value.Target.RecordedAtUtc)));
    }
}
