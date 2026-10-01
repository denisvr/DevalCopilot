using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetPlanningImplementationAuthorization;

public sealed class GetPlanningImplementationAuthorizationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/planning-escalations/{escalationMessageId:guid}/implementation-authorization")]
    public async Task<ActionResult<PlanningImplementationAuthorizationResponse>> GetPlanningImplementationAuthorization(
        [FromRoute] Guid runId, [FromRoute] Guid escalationMessageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new GetPlanningImplementationAuthorizationQuery(runId, escalationMessageId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new PlanningImplementationAuthorizationResponse(
            value.RunId, value.EscalationMessageId, value.State.ToString(), value.FinalProposalMessageId,
            value.OrderedDecisionMessageIds, value.AuthorizationId, value.HumanInstructionMessageId, value.Rationale,
            value.AuthorizedAtUtc, value.ConsumedByAttemptId, value.ConsumedAtUtc));
    }
}
