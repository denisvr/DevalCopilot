using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.StopLocalExecutionSequence;

/// <summary>
/// "Stop after current operation" for one exact local execution sequence of one exact run (ADR-0060). The request is bodyless: a query
/// key, a declared or undeclared body or a transfer encoding is refused before anything is dispatched. If it wins before a step's claim
/// commits, that step's preparation authority is revoked and nothing is claimed or consumed; if the claim wins, the already claimed
/// operation may still dispatch and finish and no successor is allowed. It is not process cancellation, run Stop or Pause. Repeated
/// requests are idempotent and a sequence that already ended is returned unchanged. An unknown run and a foreign sequence are one fixed 404.
/// </summary>
public sealed class StopLocalExecutionSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/local-execution-sequence/{sequenceId:guid}/stop")]
    [TypeFilter(typeof(LocalExecutionSequenceBodylessGuardFilter))]
    [ProducesResponseType<StopLocalExecutionSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StopLocalExecutionSequenceResponse>> StopLocalExecutionSequence(
        [FromRoute] Guid runId, [FromRoute] Guid sequenceId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new StopLocalExecutionSequenceCommand(runId, sequenceId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new StopLocalExecutionSequenceResponse(
            value.SequenceId,
            value.RunId,
            value.State.ToString(),
            value.ReasonCode,
            value.Revision.ToString(CultureInfo.InvariantCulture),
            value.Changed));
    }
}
