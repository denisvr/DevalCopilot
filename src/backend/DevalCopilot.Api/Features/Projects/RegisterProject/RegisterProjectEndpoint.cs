using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.RegisterProject;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.RegisterProject;

public sealed class RegisterProjectEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost]
    public async Task<ActionResult<RegisterProjectResponse>> RegisterProject(
        [FromBody] RegisterProjectRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new RegisterProjectCommand(request.Name, request.Path), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RegisterProjectResponse(result.Value.ProjectId));
    }
}
