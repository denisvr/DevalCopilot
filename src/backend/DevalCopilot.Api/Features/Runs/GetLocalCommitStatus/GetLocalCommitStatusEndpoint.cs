using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.RequestLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalCommitStatus;

/// <summary>Read-only eligibility and the recorded local-commit operation of a run. An unknown run is a safe 404. It exposes no
/// path, Git output, commit message text or provider payload.</summary>
public sealed class GetLocalCommitStatusEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-commit")]
    public async Task<ActionResult<GetLocalCommitStatusResponse>> GetLocalCommitStatus(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalCommitStatusQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalCommitStatusResponse(
            value.Eligible,
            value.RefusalCode,
            value.CheckpointId,
            value.CheckpointNumber,
            value.CodeReviewAttemptId,
            value.HumanCheckpointReviewId,
            value.Operation is null ? null : RequestLocalCommitEndpoint.Map(value.Operation),
            value.LatestEventSequence));
    }
}
