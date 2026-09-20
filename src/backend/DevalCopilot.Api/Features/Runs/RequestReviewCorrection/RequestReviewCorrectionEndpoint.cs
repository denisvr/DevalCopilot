using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestReviewCorrection;

public sealed class RequestReviewCorrectionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/review-correction")]
    public async Task<ActionResult<RequestReviewCorrectionResponse>> RequestReviewCorrection(
        [FromRoute] Guid runId,
        [FromBody] RequestReviewCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateReviewCorrectionAttemptCommand(runId, request.ImplementationReviewAttemptId), cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestReviewCorrectionResponse(result.Value.AttemptId, result.Value.AttemptNumber));
    }
}
