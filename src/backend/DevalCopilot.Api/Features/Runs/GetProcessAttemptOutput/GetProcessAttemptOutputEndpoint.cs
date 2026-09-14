using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetProcessAttemptOutput;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetProcessAttemptOutput;

public sealed class GetProcessAttemptOutputEndpoint(IApplicationMediator mediator) : RunsBaseEndpoint
{
    private const int DefaultMaxBytes = 16 * 1024;
    private const int HardMaxBytes = 64 * 1024;

    /// <summary>
    /// Comfortably larger than the longest possible UTF-8 codepoint (4 bytes). The boundary-safe
    /// cursor (see <c>FilesystemArtifactStore.ReadWindowAsync</c>) defers an incomplete trailing
    /// codepoint to the next read rather than ever splitting it, so an unreasonably small
    /// requested window could otherwise make no forward progress at all.
    /// </summary>
    private const int MinMaxBytes = 64;

    [HttpGet("{runId:guid}/attempts/{attemptId:guid}/output/{stream}")]
    public async Task<ActionResult<GetProcessAttemptOutputResponse>> GetProcessAttemptOutput(
        [FromRoute] Guid runId,
        [FromRoute] Guid attemptId,
        [FromRoute] string stream,
        [FromQuery] long fromOffset,
        [FromQuery] int? maxBytes,
        CancellationToken cancellationToken)
    {
        if (!TryMapPurpose(stream, out var purpose))
        {
            return NotFound();
        }

        if (fromOffset < 0)
        {
            return BadRequest();
        }

        var boundedMaxBytes = Math.Clamp(maxBytes ?? DefaultMaxBytes, MinMaxBytes, HardMaxBytes);

        var result = await mediator.SendAsync(
            new GetProcessAttemptOutputQuery(runId, attemptId, purpose, fromOffset, boundedMaxBytes), cancellationToken);

        if (result.Status == ProcessAttemptOutputStatus.AttemptNotFound)
        {
            return NotFound();
        }

        return Ok(new GetProcessAttemptOutputResponse(
            result.Status.ToString(), result.Text, result.NextOffset, result.TotalLengthSoFar, result.IsFinal, result.Truncated));
    }

    private static bool TryMapPurpose(string stream, out ArtifactPurpose purpose)
    {
        switch (stream.ToLowerInvariant())
        {
            case "stdout":
                purpose = ArtifactPurpose.ProcessStandardOutput;
                return true;
            case "stderr":
                purpose = ArtifactPurpose.ProcessStandardError;
                return true;
            default:
                purpose = default;
                return false;
        }
    }
}
