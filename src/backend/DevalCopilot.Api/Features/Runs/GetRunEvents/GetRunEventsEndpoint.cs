using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunEvents;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetRunEvents;

public sealed class GetRunEventsEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<RunEventResponse>>> GetRunEvents(
        Guid runId,
        [FromQuery] long after,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetRunEventsQuery(runId, after), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var response = result.Value
            .Select(runEvent => new RunEventResponse(
                runEvent.Sequence,
                runEvent.Id,
                runEvent.AttemptId,
                runEvent.EventType,
                ParticipantIdentityResponse.FromDomain(runEvent.Actor),
                runEvent.PayloadJson,
                runEvent.OccurredAtUtc))
            .ToArray();

        return Ok(response);
    }
}
