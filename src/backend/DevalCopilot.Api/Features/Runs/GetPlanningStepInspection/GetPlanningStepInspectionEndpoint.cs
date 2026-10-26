using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetPlanningStepInspection;

/// <summary>
/// Protected, read-only, bodyless selected association of one recorded step of a run's planning sequence (ADR-0068). It re-proves the
/// bounded structure and the exact step on every request and returns, for a linked step, only the step and the existing immutable
/// attempt header. It accepts no query key and no body and reads no message or artifact byte. An unknown run, a run without a sequence
/// and a step outside that run's sequence are one fixed 404 that repeats no value.
/// </summary>
public sealed class GetPlanningStepInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/planning-sequence/inspection/steps/{stepId:guid}")]
    [TypeFilter(typeof(PlanningSequenceInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetPlanningStepInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPlanningStepInspectionResponse>> GetPlanningStepInspection(
        [FromRoute] Guid runId, [FromRoute] Guid stepId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetPlanningStepInspectionQuery(runId, stepId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetPlanningStepInspectionResponse(
            value.RunId,
            value.SequenceId,
            value.StepId,
            value.Status.ToString(),
            value.Step is { } step ? GetPlanningSequenceInspectionEndpoint.Map(step) : null,
            value.Agent is { } agent
                ? new PlanningStepInspectionAgentResponse(
                    agent.Id, agent.AttemptNumber, agent.Role.ToString(), agent.Provider.ToString(), agent.ResponseContract.ToString())
                : null));
    }
}
