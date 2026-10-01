using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestReviewCorrection;

/// <summary>
/// Claims one ordinary review-correction attempt for a completed changes-requested review, or creates the human
/// escalation at budget exhaustion. Optional short advisory direct human guidance (see ADR-0015) is accepted only within
/// the ordinary correction budget; at exhaustion a request carrying guidance is refused and creates nothing.
/// </summary>
public sealed class RequestReviewCorrectionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/agent-attempts/review-correction")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<RequestReviewCorrectionResponse>> RequestReviewCorrection(
        [FromRoute] Guid runId,
        [FromBody] RequestReviewCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new CreateReviewCorrectionAttemptCommand(runId, request.ImplementationReviewAttemptId, request.Guidance), cancellationToken);
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
