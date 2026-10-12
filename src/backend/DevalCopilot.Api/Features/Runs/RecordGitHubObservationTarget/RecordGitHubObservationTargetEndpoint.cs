using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RecordGitHubObservationTarget;

/// <summary>
/// Explicitly records the immutable GitHub observation scope of a run's completed manual local delivery (ADR-0040). The host decides
/// from pinned rows that the operation and commit are the run's recorded delivery, derives the branch, and saves the target and one
/// Human-authored event atomically. It is local scope selection: nothing is verified, pushed or observed, and no process, network or
/// provider is touched. A replay of the same selection returns the recorded target; a different one conflicts. The request body is
/// bounded to 8 KiB.
/// </summary>
public sealed class RecordGitHubObservationTargetEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/github-observation-target")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [ProducesResponseType<RecordGitHubObservationTargetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordGitHubObservationTargetResponse>> RecordGitHubObservationTarget(
        [FromRoute] Guid runId, [FromBody] RecordGitHubObservationTargetRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new RecordGitHubObservationTargetCommand(runId, request.OperationId, request.CommitSha, request.Owner, request.Repository),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new RecordGitHubObservationTargetResponse(
            value.RunId,
            new GitHubObservationSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            new GitHubObservationScopeResponse(
                value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                value.Target.RecordedAtUtc)));
    }
}
