using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.PrepareRepositoryWorkspace;

public sealed class PrepareRepositoryWorkspaceEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/workspace/prepare")]
    public async Task<ActionResult<PrepareRepositoryWorkspaceResponse>> PrepareRepositoryWorkspace(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new PrepareRepositoryWorkspaceCommand(projectId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new PrepareRepositoryWorkspaceResponse(
                result.Value.WorkspaceId,
                result.Value.WorkspacePath,
                result.Value.BranchName,
                result.Value.SourceCommitSha,
                result.Value.SourceBranchName));
    }
}
