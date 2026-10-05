using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.CreateManualRun;

public sealed class CreateManualRunEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("manual")]
    public async Task<ActionResult<CreateManualRunResponse>> CreateManualRun(
        [FromBody] CreateManualRunRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateManualRunCommand(
                request.ProjectId, request.Objective, request.MaximumAgentAttempts, request.MaximumAgentInvocationMinutes),
            cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new CreateManualRunResponse(result.Value.RunId, result.Value.ExecutionNumber));
    }
}
