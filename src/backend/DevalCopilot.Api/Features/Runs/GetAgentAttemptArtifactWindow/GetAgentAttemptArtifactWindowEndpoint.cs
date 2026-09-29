using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetSealedAgentArtifactWindow;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptArtifactWindow;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptArtifactWindow;

/// <summary>
/// Protected, read-only, bounded sealed text window of one artifact of an Agent attempt selected from
/// the run history, restricted to the closed four-purpose allowlist (context manifest, stdout,
/// stderr, final response). Resolved by the exact run and attempt identity and served only through
/// the artifact store's integrity-verifying sealed read; never partial or unverified text. An unknown,
/// foreign-run, or non-Agent attempt, or an unrecognized purpose segment, is a safe 404; every other
/// outcome is a 200 with an explicit <c>status</c>. Performs no provider call and never renders HTML.
/// </summary>
public sealed class GetAgentAttemptArtifactWindowEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int DefaultMaxBytes = 16 * 1024;
    private const int HardMaxBytes = 64 * 1024;
    private const int MinMaxBytes = 64;

    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/evidence/artifact-window/{purpose}")]
    public async Task<ActionResult<SealedAgentArtifactWindowResponse>> GetAgentAttemptArtifactWindow(
        [FromRoute] Guid runId,
        [FromRoute] Guid attemptId,
        [FromRoute] string purpose,
        [FromQuery] long fromOffset,
        [FromQuery] int? maxBytes,
        CancellationToken cancellationToken)
    {
        if (!TryMapPurpose(purpose, out var mappedPurpose))
        {
            return NotFound();
        }

        if (fromOffset < 0)
        {
            return BadRequest();
        }

        var boundedMaxBytes = Math.Clamp(maxBytes ?? DefaultMaxBytes, MinMaxBytes, HardMaxBytes);

        var result = await mediator.SendAsync(
            new GetAgentAttemptArtifactWindowQuery(runId, attemptId, mappedPurpose, fromOffset, boundedMaxBytes), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new SealedAgentArtifactWindowResponse(
            value.Status.ToString(), value.Text, value.NextOffset, value.TotalLengthSoFar, value.Truncated));
    }

    private static bool TryMapPurpose(string purpose, out ArtifactPurpose mappedPurpose)
    {
        switch (purpose.ToLowerInvariant())
        {
            case "context-manifest":
                mappedPurpose = ArtifactPurpose.AgentContextManifest;
                return true;
            case "stdout":
                mappedPurpose = ArtifactPurpose.AgentStandardOutput;
                return true;
            case "stderr":
                mappedPurpose = ArtifactPurpose.AgentStandardError;
                return true;
            case "final-response":
                mappedPurpose = ArtifactPurpose.AgentFinalResponse;
                return true;
            default:
                mappedPurpose = default;
                return false;
        }
    }
}
