using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.ClaimVerificationExecution;

public sealed class ClaimVerificationExecutionEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/verification-commands/{verificationCommandId:guid}/executions")]
    public async Task<ActionResult<ClaimVerificationExecutionResponse>> ClaimVerificationExecution(
        Guid projectId,
        Guid verificationCommandId,
        [FromBody] ClaimVerificationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new ClaimVerificationExecutionCommand(projectId, verificationCommandId, request.GitCheckpointId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Accepted(new ClaimVerificationExecutionResponse(result.Value.VerificationExecutionId, result.Value.ExecutionNumber));
    }
}
