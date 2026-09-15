using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;

public sealed class UpdateVerificationCommandEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPut("{projectId:guid}/verification-commands/{verificationCommandId:guid}")]
    public async Task<IActionResult> UpdateVerificationCommand(
        [FromRoute] Guid projectId,
        [FromRoute] Guid verificationCommandId,
        [FromBody] UpdateVerificationCommandRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new UpdateVerificationCommandCommand(
                projectId,
                verificationCommandId,
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

        return NoContent();
    }
}
