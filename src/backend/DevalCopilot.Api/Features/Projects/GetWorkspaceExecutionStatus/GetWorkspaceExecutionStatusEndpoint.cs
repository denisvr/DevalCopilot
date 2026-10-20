using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetWorkspaceExecutionStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetWorkspaceExecutionStatus;

/// <summary>
/// Protected, read-only, bodyless and queryless reading of who may execute in the project's latest owned workspace (ADR-0057). It reads
/// only existing durable rows through the shared exclusion policy: never Git, a provider, a process table or AgentSupervision, and it
/// writes nothing. An unknown project is the same fixed, non-disclosing 404 every project read returns.
/// </summary>
public sealed class GetWorkspaceExecutionStatusEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/workspace-execution-status")]
    [TypeFilter(typeof(WorkspaceExecutionStatusBodylessGuardFilter))]
    [ProducesResponseType<GetWorkspaceExecutionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetWorkspaceExecutionStatusResponse>> GetWorkspaceExecutionStatus(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetWorkspaceExecutionStatusQuery(projectId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetWorkspaceExecutionStatusResponse(value.ProjectId, value.WorkspaceId, value.Status.ToString(), value.ReasonCode));
    }
}
