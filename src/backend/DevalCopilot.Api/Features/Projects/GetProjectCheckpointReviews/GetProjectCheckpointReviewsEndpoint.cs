using Devalente.Shared.Cqrs;
using Devalente.Shared.AspNetCore.Mvc;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectCheckpointReviews;

public sealed class GetProjectCheckpointReviewsEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/reviews")]
    public async Task<ActionResult<IReadOnlyList<CheckpointReviewResponse>>> GetProjectCheckpointReviews(
        Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectCheckpointReviewsQuery(projectId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        return Ok(result.Value.Select(review => new CheckpointReviewResponse(
            review.ReviewId,
            review.GitCheckpointId,
            review.CheckpointNumber,
            review.CheckpointFingerprintSha256,
            review.Evidence
                .Select(evidence => new CheckpointReviewEvidenceResponse(
                    evidence.VerificationCommandId,
                    evidence.VerificationExecutionId,
                    evidence.VerificationExecutionNumber,
                    evidence.VerificationExecutionStatus.ToString(),
                    evidence.VerificationExecutionOutcome?.ToString()))
                .ToArray(),
            review.ActorKind.ToString(),
            review.Decision.ToString(),
            review.IsApplicable,
            review.StaleReasonCode,
            review.RecordedAtUtc)).ToArray());
    }
}
