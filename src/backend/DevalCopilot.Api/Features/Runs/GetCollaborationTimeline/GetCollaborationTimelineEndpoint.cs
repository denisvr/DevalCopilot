using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationTimeline;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationTimeline;

public sealed class GetCollaborationTimelineEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/collaboration-timeline")]
    public async Task<ActionResult<IReadOnlyList<CollaborationMessageTimelineResponse>>> GetCollaborationTimeline(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCollaborationTimelineQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var response = result.Value
            .Select(message => new CollaborationMessageTimelineResponse(
                message.Sequence,
                message.Id,
                message.AttemptId,
                message.ProtocolVersion,
                ParticipantIdentityResponse.FromDomain(message.Actor),
                ParticipantIdentityResponse.FromDomain(message.Recipient),
                message.Type.ToString(),
                message.InReplyToMessageId,
                message.Summary,
                message.StructuredContentJson,
                message.Provenance.ToString(),
                message.OccurredAtUtc))
            .ToArray();

        return Ok(response);
    }
}
