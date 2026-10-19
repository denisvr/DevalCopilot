using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetPlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.StopPlanningSequence;

/// <summary>
/// "Stop after current step" for one exact planning sequence of one exact run (ADR-0055). The request is bodyless: a query key, a
/// declared or undeclared body or a transfer encoding is refused before anything is dispatched. If it wins before a step's claim commits,
/// that step's preparation authority is revoked and nothing is claimed or consumed; if the claim wins, the already claimed read-only step
/// may still dispatch and finish and no successor is allowed. It is not process cancellation, run Stop or Pause. Repeated requests are
/// idempotent and a sequence that already ended is returned unchanged. An unknown run and a foreign sequence are one fixed 404.
/// </summary>
public sealed class StopPlanningSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/planning-sequence/{sequenceId:guid}/stop")]
    [TypeFilter(typeof(PlanningSequenceBodylessGuardFilter))]
    [ProducesResponseType<StopPlanningSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StopPlanningSequenceResponse>> StopPlanningSequence(
        [FromRoute] Guid runId, [FromRoute] Guid sequenceId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new StopPlanningSequenceCommand(runId, sequenceId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new StopPlanningSequenceResponse(
            value.SequenceId,
            value.RunId,
            value.State.ToString(),
            value.ReasonCode,
            value.Revision.ToString(CultureInfo.InvariantCulture),
            value.Changed));
    }
}
