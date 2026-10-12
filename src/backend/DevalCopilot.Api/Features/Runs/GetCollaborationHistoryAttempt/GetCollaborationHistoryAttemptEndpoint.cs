using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCollaborationHistoryAttempt;

/// <summary>
/// Protected, read-only, bodyless inspection of the Agent attempt a collaboration message itself references (ADR-0037). An unknown run or
/// a missing or foreign source is one fixed 404 and an unusable stored source identity or sequence is one fixed 409; both are the shared
/// Problem Details contract and neither repeats a rejected value. The attempt is a minimal recorded header: no message text, outcome,
/// model or effort text, session, path, argument, artifact or token data, and it describes recorded attribution only.
/// </summary>
public sealed class GetCollaborationHistoryAttemptEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/collaboration-history/{messageId:guid}/attempt")]
    [ProducesResponseType<GetCollaborationHistoryAttemptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GetCollaborationHistoryAttemptResponse>> GetCollaborationHistoryAttempt(
        [FromRoute] Guid runId, [FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCollaborationHistoryAttemptQuery(runId, messageId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetCollaborationHistoryAttemptResponse(
            value.RunId,
            value.MessageId,
            CollaborationHistoryCursor.Format(value.SourceSequence),
            value.Status.ToString(),
            value.Attempt is { } attempt ? Map(attempt) : null));
    }

    private static CollaborationAttemptHeaderResponse Map(CollaborationAttemptHeader attempt) => new(
        attempt.Id,
        attempt.AttemptNumber,
        attempt.Role.ToString(),
        attempt.Provider.ToString(),
        attempt.ResponseContract.ToString(),
        attempt.Status.ToString(),
        attempt.ClaimedAtUtc,
        attempt.DispatchedAtUtc,
        attempt.CompletedAtUtc);
}
