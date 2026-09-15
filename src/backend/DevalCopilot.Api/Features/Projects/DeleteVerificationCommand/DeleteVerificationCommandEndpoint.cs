using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.DeleteVerificationCommand;

public sealed class DeleteVerificationCommandEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpDelete("{projectId:guid}/verification-commands/{verificationCommandId:guid}")]
    public async Task<IActionResult> DeleteVerificationCommand(
        [FromRoute] Guid projectId, [FromRoute] Guid verificationCommandId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new DeleteVerificationCommandCommand(projectId, verificationCommandId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return NoContent();
    }
}
