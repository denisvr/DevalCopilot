using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.ArmPlanningSequence;

/// <summary>
/// Explicitly arms one read-only planning sequence for a pristine manual run (ADR-0055). The body holds exactly the workspace, checkpoint
/// and fingerprint the Human saw, is bounded to 2 KiB, and a query key, an unknown, duplicate, differently cased or escaped member or an
/// oversized body is refused before anything is dispatched. The host decides every other fact from fresh authority, captures fresh Git
/// evidence outside any transaction, re-reads all authority under the write lock and records the sequence with its event atomically;
/// arming invokes no provider, prepares no workspace and consumes no budget. A replay of the identical tuple returns the recorded
/// receipt and never rearms; a different tuple conflicts. Unknown runs are one fixed 404.
/// </summary>
public sealed class ArmPlanningSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = StrictPlanningSequenceArmingBodyFilter.MaximumBodyBytes;

    [HttpPost("{runId:guid}/planning-sequence")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictPlanningSequenceArmingBodyFilter))]
    [ProducesResponseType<ArmPlanningSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ArmPlanningSequenceResponse>> ArmPlanningSequence(
        [FromRoute] Guid runId, [FromBody] ArmPlanningSequenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new ArmPlanningSequenceCommand(runId, request.WorkspaceId, request.CheckpointId, request.FingerprintSha256),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new ArmPlanningSequenceResponse(
            value.SequenceId,
            value.RunId,
            value.WorkspaceId,
            value.CheckpointId,
            value.FingerprintSha256,
            value.State.ToString(),
            value.ArmedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            value.Revision.ToString(CultureInfo.InvariantCulture),
            value.Replayed));
    }
}
