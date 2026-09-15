using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;

public sealed class ConfigureVerificationCommandEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/verification-commands")]
    public async Task<ActionResult<ConfigureVerificationCommandResponse>> ConfigureVerificationCommand(
        [FromRoute] Guid projectId,
        [FromBody] ConfigureVerificationCommandRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new ConfigureVerificationCommandCommand(
                projectId,
                request.Name,
                request.ExecutablePath,
                request.Arguments,
                request.TimeoutSeconds,
                request.IsEnabled),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(new ConfigureVerificationCommandResponse(result.Value.VerificationCommandId, result.Value.CommandNumber));
    }
}
