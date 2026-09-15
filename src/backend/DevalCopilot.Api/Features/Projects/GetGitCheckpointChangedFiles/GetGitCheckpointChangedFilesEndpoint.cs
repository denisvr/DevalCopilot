using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointChangedFiles;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetGitCheckpointChangedFiles;

public sealed class GetGitCheckpointChangedFilesEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/workspace/checkpoints/{checkpointId:guid}/changed-files")]
    public async Task<ActionResult<IReadOnlyList<GitCheckpointChangedFileResponse>>> GetGitCheckpointChangedFiles(
        [FromRoute] Guid projectId, [FromRoute] Guid checkpointId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitCheckpointChangedFilesQuery(projectId, checkpointId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(result.Value.Select(file => new GitCheckpointChangedFileResponse(
            file.Path, file.PreviousPath, file.IndexStatus, file.WorkTreeStatus)));
    }
}
