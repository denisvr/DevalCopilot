using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetCollaborationHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistoryReply;

/// <summary>
/// Protected, read-only, bodyless inspection of the one recorded reply parent of a collaboration message (ADR-0036). An unknown run or a
/// missing or foreign source is one fixed 404 and an unusable stored source identity or sequence is one fixed 409; both are the shared
/// Problem Details contract and neither repeats a rejected value. The parent keeps the ADR-0035 disclosure; no raw JSON, attempt,
/// artifact or foreign or damaged pointer is exposed, and it describes the recorded relationship only.
/// </summary>
public sealed class GetCollaborationHistoryReplyEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/collaboration-history/{messageId:guid}/reply")]
    [ProducesResponseType<GetCollaborationHistoryReplyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GetCollaborationHistoryReplyResponse>> GetCollaborationHistoryReply(
        [FromRoute] Guid runId, [FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCollaborationHistoryReplyQuery(runId, messageId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetCollaborationHistoryReplyResponse(
            value.RunId,
            value.MessageId,
            CollaborationHistoryCursor.Format(value.SourceSequence),
            value.Status.ToString(),
            value.Parent is { } parent ? CollaborationHistoryEntryMapper.Map(parent) : null));
    }
}
