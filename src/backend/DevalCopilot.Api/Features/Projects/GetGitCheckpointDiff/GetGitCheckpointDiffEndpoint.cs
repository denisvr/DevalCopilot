using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointDiff;

public sealed class GetGitCheckpointDiffEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/workspace/checkpoints/{checkpointId:guid}/diff")]
    public async Task<ActionResult<GetGitCheckpointDiffResponse>> GetGitCheckpointDiff(
        [FromRoute] Guid projectId, [FromRoute] Guid checkpointId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitCheckpointDiffQuery(projectId, checkpointId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(new GetGitCheckpointDiffResponse(result.Value.FingerprintSha256, result.Value.CompleteDiff));
    }
}
