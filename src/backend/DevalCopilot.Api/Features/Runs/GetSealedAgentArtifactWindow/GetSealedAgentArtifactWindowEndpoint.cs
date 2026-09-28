using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetSealedAgentArtifactWindow;

/// <summary>
/// Bounded, read-only inspection of one sealed Agent-attempt artifact, extending the existing
/// collaboration evidence drill-down. Resolved solely through the collaboration message's own
/// durable <c>AttemptId</c> foreign key, restricted to the closed four-purpose Agent-artifact
/// allowlist (context manifest, stdout, stderr, final response) — never the two Process-attempt
/// purposes <c>GetProcessAttemptOutput</c> already serves. An unknown run/message pair or an
/// unrecognized purpose segment is a safe 404; every other resolution outcome is a real 200 body
/// with an explicit, distinct <c>status</c> — never an ambiguous empty success, and never
/// substituted content. Performs no provider call and never renders content as HTML.
/// </summary>
public sealed class GetSealedAgentArtifactWindowEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int DefaultMaxBytes = 16 * 1024;
    private const int HardMaxBytes = 64 * 1024;

    /// <summary>Comfortably larger than the longest possible UTF-8 codepoint (4 bytes) — see
    /// <c>GetProcessAttemptOutputEndpoint</c>'s identical constant for the full rationale.</summary>
    private const int MinMaxBytes = 64;

    [HttpGet("{runId:guid}/collaboration-messages/{messageId:guid}/evidence/artifact-window/{purpose}")]
    public async Task<ActionResult<SealedAgentArtifactWindowResponse>> GetSealedAgentArtifactWindow(
        [FromRoute] Guid runId,
        [FromRoute] Guid messageId,
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
            new GetSealedAgentArtifactWindowQuery(runId, messageId, mappedPurpose, fromOffset, boundedMaxBytes), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new SealedAgentArtifactWindowResponse(
            value.Status.ToString(), value.Text, value.NextOffset, value.TotalLengthSoFar, value.Truncated));
    }

    /// <summary>The closed, four-value route allowlist for this operation — every other
    /// <see cref="ArtifactPurpose"/> member (the two Process-attempt purposes) is an unrecognized
    /// segment here, exactly as an unrecognized <c>stream</c> segment is for
    /// <c>GetProcessAttemptOutputEndpoint</c>.</summary>
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
