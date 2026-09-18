using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetClaudeCriticalReviewAttemptStatus;

/// <summary>
/// Bounded status and artifact metadata for the most recent Claude critical-review attempt on a
/// run. An unknown run is a safe 404, not a body a caller must interpret; an existing run with no
/// such attempt yet is a real 200 body with <c>hasAttempt: false</c> — never an ambiguous
/// null/204. Never exposes a local path, prompt, transcript, credential, command line, or raw
/// provider payload.
/// </summary>
public sealed class GetClaudeCriticalReviewAttemptStatusEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/claude-critical-review")]
    public async Task<ActionResult<ClaudeCriticalReviewAttemptStatusResponse>> GetClaudeCriticalReviewAttemptStatus(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetClaudeCriticalReviewAttemptStatusQuery(runId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new ClaudeCriticalReviewAttemptStatusResponse(
            value.HasAttempt,
            value.AttemptId,
            value.AttemptNumber,
            value.ReviewedProposalMessageId,
            value.Status?.ToString(),
            value.Outcome?.ToString(),
            value.ClaimedAtUtc,
            value.DispatchedAtUtc,
            value.CompletedAtUtc,
            value.Artifacts
                .Select(artifact => new AgentAttemptArtifactMetadataResponse(
                    artifact.Purpose.ToString(), artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome.ToString()))
                .ToArray()));
    }
}
