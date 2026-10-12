using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistory;

/// <summary>
/// Protected, read-only, run-scoped page of recorded collaboration messages in descending sequence order with an exclusive string
/// cursor (ADR-0035). An unknown run is a safe 404, a cursor or limit outside its bounds (or one that is not canonical) is a 400 that
/// is never clamped, and a selected row without an identity or positive sequence is one fixed 409; all are the shared Problem Details
/// contract and none repeats a rejected value. A message whose recorded metadata or content is not recognized, coherent, safe and
/// within the existing content policy keeps its header and is reported as unavailable without its text. It exposes no attempt, reply
/// target, raw JSON, artifact, path or stored enum text, and describes recorded content only, never a current approval.
/// </summary>
public sealed class GetCollaborationHistoryEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/collaboration-history")]
    [ProducesResponseType<GetCollaborationHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GetCollaborationHistoryResponse>> GetCollaborationHistory(
        [FromRoute] Guid runId,
        [FromQuery, ModelBinder(typeof(CollaborationHistoryCursorModelBinder))] string? beforeSequence,
        [FromQuery, ModelBinder(typeof(CollaborationHistoryLimitModelBinder))] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCollaborationHistoryQuery(runId, beforeSequence, limit), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetCollaborationHistoryResponse(
            value.RunId,
            value.Entries.Select(CollaborationHistoryEntryMapper.Map).ToArray(),
            value.HasMore,
            value.NextBeforeSequence is { } next ? CollaborationHistoryCursor.Format(next) : null));
    }
}
