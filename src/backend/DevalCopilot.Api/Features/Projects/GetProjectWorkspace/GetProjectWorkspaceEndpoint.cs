using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectWorkspace;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectWorkspace;

public sealed class GetProjectWorkspaceEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/workspace")]
    public async Task<ActionResult<GetProjectWorkspaceResponse>> GetProjectWorkspace(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectWorkspaceQuery(projectId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(new GetProjectWorkspaceResponse(
            result.Value.PhysicalIdentityStatus,
            result.Value.PhysicalIdentityBlockedMessage,
            result.Value.State.ToString(),
            result.Value.CandidatePath,
            result.Value.BranchName,
            result.Value.SourceCommitSha,
            result.Value.SourceBranchName,
            result.Value.LeaseStatus,
            result.Value.BlockedReasonCode,
            result.Value.BlockedReasonMessage));
    }
}
