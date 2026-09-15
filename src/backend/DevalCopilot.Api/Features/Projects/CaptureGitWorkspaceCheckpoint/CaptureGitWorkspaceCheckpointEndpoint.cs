using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.CaptureGitWorkspaceCheckpoint;

public sealed class CaptureGitWorkspaceCheckpointEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/workspace/checkpoints")]
    public async Task<ActionResult<CaptureGitWorkspaceCheckpointResponse>> CaptureGitWorkspaceCheckpoint(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new CaptureGitWorkspaceCheckpointCommand(projectId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(new CaptureGitWorkspaceCheckpointResponse(
            result.Value.CheckpointId,
            result.Value.CheckpointNumber,
            result.Value.HeadCommitSha,
            result.Value.FingerprintSha256,
            result.Value.ChangedFileCount));
    }
}
