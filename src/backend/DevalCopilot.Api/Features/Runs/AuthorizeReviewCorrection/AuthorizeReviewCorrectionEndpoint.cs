using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrection;

public sealed class AuthorizeReviewCorrectionEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/review-correction-escalations/{escalationId:guid}/authorize")]
    public async Task<ActionResult<AuthorizeReviewCorrectionResponse>> AuthorizeReviewCorrection(
        [FromRoute] Guid runId, [FromRoute] Guid escalationId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new AuthorizeReviewCorrectionCommand(runId, escalationId), cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new AuthorizeReviewCorrectionResponse(
                result.Value.Status, result.Value.EscalationId, result.Value.HumanInstructionMessageId,
                result.Value.AuthorizationId, result.Value.LatestEventSequence));
    }
}
