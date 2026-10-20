using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.ExecutionCapacity.SetExecutionCapacityLimit;

/// <summary>
/// Protected, queryless explicit change of the host-wide recorded-mutating admission limit, one or two (ADR-0059). The body is exactly one
/// JSON integer member, bounded to 512 bytes whether or not its length is declared; any other shape is refused before anything is
/// dispatched. The command decides under the database write boundary, so an equal value is an idempotent no-op, a limit below the
/// recorded owners and an unreadable state are fixed 409 refusals with nothing written, and success returns the capacity reading under the
/// new limit. Changing the limit starts, cancels, releases, retries and queues nothing and calls no provider, process or Git.
/// </summary>
public sealed class SetExecutionCapacityLimitEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : ExecutionCapacityBaseEndpoint
{
    private const int MaximumRequestBodyBytes = StrictExecutionCapacityLimitBodyFilter.MaximumBodyBytes;

    [HttpPut("limit")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictExecutionCapacityLimitBodyFilter))]
    [ProducesResponseType<SetExecutionCapacityLimitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<SetExecutionCapacityLimitResponse>> SetExecutionCapacityLimit(
        [FromBody] SetExecutionCapacityLimitRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetExecutionCapacityLimitCommand(request.Limit), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new SetExecutionCapacityLimitResponse(value.Limit, value.RecordedOwners, value.Status.ToString(), value.ReasonCode));
    }
}
