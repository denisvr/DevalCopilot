using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.RecheckProjectPhysicalIdentity;

public sealed class RecheckProjectPhysicalIdentityEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/physical-identity/recheck")]
    public async Task<ActionResult<RecheckProjectPhysicalIdentityResponse>> RecheckProjectPhysicalIdentity(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new RecheckProjectPhysicalIdentityCommand(projectId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RecheckProjectPhysicalIdentityResponse(result.Value.Status.ToString()));
    }
}
