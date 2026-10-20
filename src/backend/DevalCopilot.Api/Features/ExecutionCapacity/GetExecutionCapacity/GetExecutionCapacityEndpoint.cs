using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetExecutionCapacity;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.ExecutionCapacity.GetExecutionCapacity;

/// <summary>
/// Protected, read-only, bodyless and queryless reading of how many potentially mutating execution owners the host records across its
/// database (ADR-0058). It reads only existing durable rows through the shared capacity policy: never Git, a provider, a process table
/// or AgentSupervision, and it writes nothing. Unsafe or unrecognized ownership is the closed Unavailable answer, never a fabricated count.
/// </summary>
public sealed class GetExecutionCapacityEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : ExecutionCapacityBaseEndpoint
{
    [HttpGet]
    [TypeFilter(typeof(ExecutionCapacityBodylessGuardFilter))]
    [ProducesResponseType<GetExecutionCapacityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetExecutionCapacityResponse>> GetExecutionCapacity(CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetExecutionCapacityQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetExecutionCapacityResponse(value.Limit, value.RecordedOwners, value.Status.ToString(), value.ReasonCode));
    }
}
