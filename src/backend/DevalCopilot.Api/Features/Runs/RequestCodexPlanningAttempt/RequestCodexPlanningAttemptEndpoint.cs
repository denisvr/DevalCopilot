using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestCodexPlanningAttempt;

/// <summary>
/// Creates one durable Codex planning attempt for an eligible run. Never accepts or exposes a
/// prompt, local path, environment value, credential, command line, or raw provider payload.
/// </summary>
public sealed class RequestCodexPlanningAttemptEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/codex-plan")]
    public async Task<ActionResult<RequestCodexPlanningAttemptResponse>> RequestCodexPlanningAttempt(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestCodexPlanningAttemptResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
