using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.StartSimulatedRun;

public sealed class StartSimulatedRunEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("simulated")]
    public async Task<ActionResult<StartSimulatedRunResponse>> StartSimulatedRun(
        [FromBody] StartSimulatedRunRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new StartSimulatedRunCommand(request.ProjectId, request.Objective), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new StartSimulatedRunResponse(result.Value.RunId, result.Value.ExecutionNumber));
    }
}
