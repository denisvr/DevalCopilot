using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrectionWithGuidance;

/// <summary>
/// Authorizes one additional review correction for an exhausted-review escalation and records short
/// human guidance with it. The bodyless <c>…/authorize</c> operation is unchanged. The request body is
/// size-capped, the guidance is normalized and bounded by the server, and no response or error
/// echoes it; the accepted text is visible only in the recorded HumanInstruction and the sealed
/// manifest of the correction that consumes the authorization.
/// </summary>
public sealed class AuthorizeReviewCorrectionWithGuidanceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/review-correction-escalations/{escalationId:guid}/authorize-with-guidance")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<AuthorizeReviewCorrectionResponse>> AuthorizeReviewCorrectionWithGuidance(
        [FromRoute] Guid runId,
        [FromRoute] Guid escalationId,
        [FromBody] AuthorizeReviewCorrectionWithGuidanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new AuthorizeReviewCorrectionCommand(runId, escalationId, request.Guidance ?? string.Empty), cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new AuthorizeReviewCorrectionResponse(
                result.Value.Status, result.Value.EscalationId, result.Value.HumanInstructionMessageId,
                result.Value.AuthorizationId, result.Value.LatestEventSequence));
    }
}
