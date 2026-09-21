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
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return result.Value switch
        {
            CreateReviewCorrectionAttemptCommandResult.AttemptCreated created => Ok(
                new RequestReviewCorrectionResponse("AttemptCreated", created.AttemptId, created.AttemptNumber, null, null, created.LatestEventSequence)),
            CreateReviewCorrectionAttemptCommandResult.Escalated escalated => Ok(
                new RequestReviewCorrectionResponse("Escalated", null, null, escalated.EscalationId, escalated.EscalationMessageId, escalated.LatestEventSequence)),
            _ => throw new InvalidOperationException("The correction result variant is not supported."),
        };
    }
}
