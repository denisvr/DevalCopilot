using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.ReleaseLocalExecutionOwnership;

/// <summary>
/// Explicitly releases the retained ownership quarantine of one terminal local execution operation after durable original-host
/// termination evidence (ADR-0065). The body holds exactly the observation and the versioned confirmation with the UTC expiry the Human
/// saw, is bounded to 512 bytes, and a query key, an unknown, duplicate, differently cased or escaped member or an oversized body is
/// refused before anything is dispatched. The host re-proves the whole chain from fresh facts under its write lock and appends the one
/// immutable release and the one Human event atomically; an identical replay returns the recorded receipt (also after expiry) with no
/// write, and a stale, mismatched or expired confirmation, an ineligible step and unavailable evidence are fixed 409 refusals. The release
/// changes no outcome, cycle, workspace, lease, approval, artifact or budget. An unknown run or step is one fixed 404.
/// </summary>
public sealed class ReleaseLocalExecutionOwnershipEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = StrictLocalExecutionOwnershipReleaseBodyFilter.MaximumBodyBytes;

    [HttpPost("{runId:guid}/local-execution-sequence/steps/{stepId:guid}/ownership-release")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictLocalExecutionOwnershipReleaseBodyFilter))]
    [ProducesResponseType<LocalExecutionOwnershipReleaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<LocalExecutionOwnershipReleaseResponse>> ReleaseLocalExecutionOwnership(
        [FromRoute] Guid runId, [FromRoute] Guid stepId, [FromBody] ReleaseLocalExecutionOwnershipRequest request, CancellationToken cancellationToken)
    {
        // The strict filter already proved the exact canonical forms; a value that still does not parse is refused the same way.
        if (!Guid.TryParseExact(request.ObservationId, "D", out var observationId)
            || !int.TryParse(request.ConfirmationVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || !LocalExecutionOwnershipReleaseConfirmation.TryParseTime(request.ConfirmationExpiresAtUtc, out var expiresAtUtc))
        {
            return problemDetails.CreateResponse(Result.Failure(LocalExecutionOwnershipReleaseErrors.InvalidRequest()), HttpContext);
        }

        var result = await mediator.SendAsync(
            new ReleaseLocalExecutionOwnershipCommand(
                runId, stepId, observationId, version, request.ConfirmationSha256, expiresAtUtc),
            cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(LocalExecutionOwnershipReleaseResponses.Map(result.Value));
    }
}
