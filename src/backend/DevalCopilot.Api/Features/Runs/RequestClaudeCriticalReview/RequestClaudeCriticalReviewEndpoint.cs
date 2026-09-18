using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestClaudeCriticalReview;

/// <summary>
/// Creates one durable Claude critical-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Codex proposal. Never accepts or exposes a
/// prompt, local path, environment value, credential, command line, or raw provider payload.
/// </summary>
public sealed class RequestClaudeCriticalReviewEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/claude-critical-review")]
    public async Task<ActionResult<RequestClaudeCriticalReviewResponse>> RequestClaudeCriticalReview(
        [FromRoute] Guid runId, [FromBody] RequestClaudeCriticalReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateClaudeCriticalReviewAttemptCommand(runId, request.ProposalMessageId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestClaudeCriticalReviewResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
