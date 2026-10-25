using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionReleaseInspection;

/// <summary>
/// Protected, read-only, bodyless recorded quarantine-release receipt of one recorded step of a run's local execution sequence
/// (ADR-0067). One bounded database statement proves the exact historical chain of the step's witness, original-host physical
/// termination observation, release and Human event and returns the closed graph: only <c>Recorded</c> carries a receipt. It accepts no
/// query key and no body, mints no confirmation, reads nothing external and no current eligibility, and changes nothing. An unknown run,
/// a run without a sequence and a step outside it are the one fixed inspection 404 that repeats no value.
/// </summary>
public sealed class GetLocalExecutionReleaseInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}/ownership-release")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionReleaseInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionReleaseInspectionResponse>> GetLocalExecutionReleaseInspection(
        [FromRoute] Guid runId, [FromRoute] Guid stepId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionReleaseInspectionQuery(runId, stepId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalExecutionReleaseInspectionResponse(
            value.RunId, value.StepId, value.Status.ToString(), value.Receipt is { } receipt ? Map(receipt) : null));
    }

    private static LocalExecutionReleaseInspectionReceiptResponse Map(LocalExecutionReleaseInspectionReceipt receipt)
    {
        var facts = receipt.Facts;
        return new LocalExecutionReleaseInspectionReceiptResponse(
            receipt.ReleaseId,
            LocalExecutionOwnershipReleaseConfirmation.Time(receipt.ReleasedAtUtc),
            receipt.EventSequence.ToString(CultureInfo.InvariantCulture),
            facts.SequenceId,
            facts.SequenceState.ToString(),
            facts.AdmissionMode.ToString(),
            facts.Kind.ToString(),
            facts.Ordinal,
            facts.PassNumber,
            facts.Family.ToString(),
            facts.OperationId,
            facts.SourceCheckpointId,
            facts.SourceFingerprintSha256,
            facts.OperationStatus,
            facts.ProcessOutcome.ToString(),
            facts.SemanticExitCode,
            facts.ProcessDurationTicks.ToString(CultureInfo.InvariantCulture),
            facts.WitnessId,
            facts.ObservationId,
            LocalExecutionOwnershipReleaseConfirmation.Time(facts.DispatchedAtUtc),
            LocalExecutionOwnershipReleaseConfirmation.Time(facts.RecordedAtUtc),
            facts.PhysicalExitCode,
            facts.ObservedElapsedTicks.ToString(CultureInfo.InvariantCulture));
    }
}
