using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.ArmLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.QueueLocalExecutionSequence;

/// <summary>
/// Explicitly queues one local execution sequence for one accepted plan of a running manual run (ADR-0062). The body holds exactly the
/// five members of the Immediate arming, is bounded to 1 KiB and admits no query; an unknown, duplicate, differently cased or escaped
/// member, a query key or an oversized body is refused before anything is dispatched. The route alone selects the queued mode, and the
/// confirmation is the separately versioned queue confirmation, so an Immediate confirmation cannot queue and a queue confirmation
/// cannot arm Immediate execution. Intake observes no Git, pins the recorded source and the original two-hour deadline, and records the
/// sequence, its pinned recipes, its first pending admission and its one Human event atomically; it invokes no provider, runs no
/// verification, claims no attempt, reserves nothing and consumes no budget. A replay of the identical tuple returns the recorded
/// receipt; another tuple or mode conflicts. Unknown runs are one fixed 404.
/// </summary>
public sealed class QueueLocalExecutionSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = StrictLocalExecutionSequenceArmingBodyFilter.MaximumBodyBytes;

    [HttpPost("{runId:guid}/local-execution-sequence/queue")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictLocalExecutionSequenceArmingBodyFilter))]
    [ProducesResponseType<ArmLocalExecutionSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ArmLocalExecutionSequenceResponse>> QueueLocalExecutionSequence(
        [FromRoute] Guid runId, [FromBody] QueueLocalExecutionSequenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new ArmLocalExecutionSequenceCommand(
                runId,
                request.PlanProposalMessageId,
                request.WorkspaceId,
                request.CheckpointId,
                request.FingerprintSha256,
                request.ConfirmationSha256,
                LocalExecutionAdmissionMode.WaitForAdmission),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var response = ArmLocalExecutionSequenceResponses.Map(result.Value);
        return response is null
            ? problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.PersistenceFailed()), HttpContext)
            : Ok(response);
    }
}
