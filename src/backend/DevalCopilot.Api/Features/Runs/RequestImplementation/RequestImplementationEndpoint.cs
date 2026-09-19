using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestImplementation;

/// <summary>
/// Creates one durable Claude implementation attempt for an eligible run, implementing exactly
/// one authoritative resolved plan, identified only by its Proposal message id. Never accepts or
/// exposes a prompt, local path, environment value, credential, command line, or raw provider
/// payload.
/// </summary>
public sealed class RequestImplementationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/implementation")]
    public async Task<ActionResult<RequestImplementationResponse>> RequestImplementation(
        [FromRoute] Guid runId, [FromBody] RequestImplementationRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateImplementationAttemptCommand(runId, request.PlanProposalMessageId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestImplementationResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
