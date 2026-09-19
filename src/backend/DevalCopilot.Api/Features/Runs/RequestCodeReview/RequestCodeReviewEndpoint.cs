using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestCodeReview;

/// <summary>
/// Creates one durable Codex code-review attempt for an eligible run, reviewing exactly one
/// explicit, already-recorded, provider-observed Claude implementation execution report. Never
/// accepts or exposes a prompt, local path, environment value, credential, command line, or raw
/// provider payload.
/// </summary>
public sealed class RequestCodeReviewEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/code-review")]
    public async Task<ActionResult<RequestCodeReviewResponse>> RequestCodeReview(
        [FromRoute] Guid runId, [FromBody] RequestCodeReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateCodeReviewAttemptCommand(runId, request.ExecutionReportMessageId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestCodeReviewResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
