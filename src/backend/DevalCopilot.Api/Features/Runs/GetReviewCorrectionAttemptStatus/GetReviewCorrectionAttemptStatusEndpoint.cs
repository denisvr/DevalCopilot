using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetReviewCorrectionAttemptStatus;

public sealed class GetReviewCorrectionAttemptStatusEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/review-correction")]
    public async Task<ActionResult<ReviewCorrectionAttemptStatusResponse>> GetReviewCorrectionAttemptStatus(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetReviewCorrectionAttemptStatusQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new ReviewCorrectionAttemptStatusResponse(
            value.HasAttempt, value.AttemptId, value.AttemptNumber, value.ImplementationReviewAttemptId,
            value.Status?.ToString(), value.Outcome?.ToString(), value.StartingGitCheckpointId,
            value.ResultGitCheckpointId, value.RevisionResponseCount, value.ClaimedAtUtc,
            value.DispatchedAtUtc, value.CompletedAtUtc,
            value.Artifacts.Select(artifact => new AgentAttemptArtifactMetadataResponse(
                artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString())).ToArray()));
    }
}
