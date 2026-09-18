using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestChallengeResolution;

/// <summary>
/// Creates one durable Codex challenge-resolution attempt for an eligible run, resolving exactly
/// the complete, already-recorded Challenge set of one specific, already-completed Challenged
/// Claude critical-review attempt. Never accepts or exposes a prompt, local path, environment
/// value, credential, command line, or raw provider payload.
/// </summary>
public sealed class RequestChallengeResolutionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/challenge-resolution")]
    public async Task<ActionResult<RequestChallengeResolutionResponse>> RequestChallengeResolution(
        [FromRoute] Guid runId, [FromBody] RequestChallengeResolutionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateChallengeResolutionAttemptCommand(runId, request.ChallengedReviewAttemptId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestChallengeResolutionResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
