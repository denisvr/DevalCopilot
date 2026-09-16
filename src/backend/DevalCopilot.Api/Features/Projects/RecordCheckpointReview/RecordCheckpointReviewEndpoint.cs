using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.RecordCheckpointReview;

public sealed class RecordCheckpointReviewEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpPost("{projectId:guid}/reviews")]
    public async Task<ActionResult<RecordCheckpointReviewResponse>> RecordCheckpointReview(
        Guid projectId,
        [FromBody] RecordCheckpointReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReviewActorKind>(request.ActorKind, ignoreCase: true, out var actorKind)
            || !Enum.IsDefined(actorKind)
            || !Enum.TryParse<ReviewDecision>(request.Decision, ignoreCase: true, out var decision)
            || !Enum.IsDefined(decision))
        {
            return BadRequest();
        }

        var result = await mediator.SendAsync(new RecordCheckpointReviewCommand(
            projectId, request.GitCheckpointId, request.VerificationExecutionId, actorKind, decision), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Created($"/api/projects/{projectId}/reviews",
            new RecordCheckpointReviewResponse(result.Value.ReviewId, result.Value.Decision.ToString()));
    }
}
