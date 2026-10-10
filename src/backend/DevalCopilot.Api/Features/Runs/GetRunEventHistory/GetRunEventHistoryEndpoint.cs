using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetRunEventHistory;

/// <summary>
/// Protected, read-only, run-scoped page of persisted event headers in descending sequence order with an exclusive string cursor
/// (ADR-0034). An unknown run is a safe 404, a cursor or limit outside its bounds (or one that is not canonical) is a 400 that is
/// never clamped, and a selected header without an identity or positive sequence is one fixed 409; all are the shared Problem
/// Details contract and none repeats a rejected value. It reads header columns only and exposes no payload, attempt, message,
/// artifact, path or stored enum text.
/// </summary>
public sealed class GetRunEventHistoryEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const string Unrecognized = "Unrecognized";

    [HttpGet("{runId:guid}/event-history")]
    [ProducesResponseType<GetRunEventHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GetRunEventHistoryResponse>> GetRunEventHistory(
        [FromRoute] Guid runId,
        [FromQuery, ModelBinder(typeof(RunEventHistoryCursorModelBinder))] string? beforeSequence,
        [FromQuery, ModelBinder(typeof(RunEventHistoryLimitModelBinder))] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetRunEventHistoryQuery(runId, beforeSequence, limit), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetRunEventHistoryResponse(
            value.RunId,
            value.Entries.Select(Map).ToArray(),
            value.HasMore,
            value.NextBeforeSequence is { } next ? RunEventHistoryCursor.Format(next) : null));
    }

    private static RunEventHistoryEntryResponse Map(RunEventHistoryEntry entry) => new(
        entry.RunId,
        entry.Id,
        RunEventHistoryCursor.Format(entry.Sequence),
        entry.OccurredAtUtc,
        entry.EventType ?? Unrecognized,
        new RunEventHistoryActorResponse(
            entry.Actor.Kind?.ToString() ?? Unrecognized,
            entry.Actor.Role?.ToString(),
            entry.Actor.Provider?.ToString()));
}
