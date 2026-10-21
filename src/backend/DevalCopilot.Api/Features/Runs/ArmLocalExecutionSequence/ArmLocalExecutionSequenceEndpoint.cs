using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.ArmLocalExecutionSequence;

/// <summary>
/// Explicitly arms one local execution sequence for one accepted plan of a running manual run (ADR-0060). The body holds exactly the
/// plan, workspace, checkpoint, fingerprint and confirmation the Human saw, is bounded to 1 KiB, and a query key, an unknown, duplicate,
/// differently cased or escaped member or an oversized body is refused before anything is dispatched. The host decides every other fact
/// from fresh authority, captures fresh Git evidence outside any transaction, re-reads all authority under the write lock and records
/// the sequence, its pinned recipes and its event atomically; arming invokes no provider, runs no verification, claims no attempt and
/// consumes no budget. A replay of the identical tuple returns the recorded receipt and never rearms; a different tuple conflicts.
/// Unknown runs are one fixed 404.
/// </summary>
public sealed class ArmLocalExecutionSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = StrictLocalExecutionSequenceArmingBodyFilter.MaximumBodyBytes;

    [HttpPost("{runId:guid}/local-execution-sequence")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictLocalExecutionSequenceArmingBodyFilter))]
    [ProducesResponseType<ArmLocalExecutionSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ArmLocalExecutionSequenceResponse>> ArmLocalExecutionSequence(
        [FromRoute] Guid runId, [FromBody] ArmLocalExecutionSequenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new ArmLocalExecutionSequenceCommand(
                runId,
                request.PlanProposalMessageId,
                request.WorkspaceId,
                request.CheckpointId,
                request.FingerprintSha256,
                request.ConfirmationSha256),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new ArmLocalExecutionSequenceResponse(
            value.SequenceId,
            value.RunId,
            value.PlanProposalMessageId,
            value.WorkspaceId,
            value.CheckpointId,
            value.FingerprintSha256,
            value.ConfirmationSha256,
            value.State.ToString(),
            value.RecipeCount,
            value.CorrectionAllowance,
            value.ArmedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            value.AdvancementDeadlineUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            value.Revision.ToString(CultureInfo.InvariantCulture),
            value.Replayed));
    }
}
