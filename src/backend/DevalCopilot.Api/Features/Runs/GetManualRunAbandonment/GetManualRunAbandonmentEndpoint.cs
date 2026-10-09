using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetManualRunAbandonment;

/// <summary>Read-only abandonment eligibility and the recorded abandonment of a run. An unknown run is a safe 404. It writes nothing,
/// invokes no provider and authorizes nothing: the abandonment command decides again.</summary>
public sealed class GetManualRunAbandonmentEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/abandonment")]
    public async Task<ActionResult<GetManualRunAbandonmentResponse>> GetManualRunAbandonment(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetManualRunAbandonmentQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetManualRunAbandonmentResponse(
            value.Eligible,
            value.RefusalCode,
            value.Abandonment is null
                ? null
                : new ManualRunAbandonmentResponse(value.Abandonment.Reason, value.Abandonment.AbandonedAtUtc),
            value.LatestEventSequence));
    }
}
