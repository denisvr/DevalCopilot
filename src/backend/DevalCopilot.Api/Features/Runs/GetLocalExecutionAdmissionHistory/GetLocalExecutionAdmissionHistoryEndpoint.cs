using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;

/// <summary>
/// Protected, read-only, bodyless recorded admission ledger of one run's queued local execution cycle (ADR-0063): the stored sequence
/// header and every recorded admission, including a wait retired without a step, with the exact recorded step link, proved as a whole
/// from one database statement. It accepts no query key and no body (the shared inspection guard), invokes no admission, ownership or
/// mutation handler, reads no artifact, and observes no lease, workspace, Git, process, provider or account state. An unknown run is one
/// fixed 404 in the shared Problem Details contract; a known run without a sequence is <c>NotRecorded</c> and an Immediate sequence is
/// <c>NotQueued</c>.
/// </summary>
public sealed class GetLocalExecutionAdmissionHistoryEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/inspection/admissions")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionAdmissionHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionAdmissionHistoryResponse>> GetLocalExecutionAdmissionHistory(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionAdmissionHistoryQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalExecutionAdmissionHistoryResponse(
            value.RunId,
            value.Status.ToString(),
            value.Sequence is { } header ? Map(header) : null,
            value.Admissions.Select(Map).ToArray()));
    }

    private static LocalExecutionAdmissionHistoryHeaderResponse Map(LocalExecutionAdmissionHistoryHeader header) => new(
        header.SequenceId,
        header.AdmissionMode.ToString(),
        header.RecordedState.ToString(),
        header.ReasonCode,
        header.SequenceRevision.ToString(CultureInfo.InvariantCulture),
        GetLocalExecutionSequenceInspectionEndpoint.Time(header.ArmedAtUtc),
        GetLocalExecutionSequenceInspectionEndpoint.Time(header.AdvancementDeadlineUtc),
        GetLocalExecutionSequenceInspectionEndpoint.Time(header.UpdatedAtUtc),
        header.EndedAtUtc is { } ended ? GetLocalExecutionSequenceInspectionEndpoint.Time(ended) : null);

    private static LocalExecutionAdmissionHistoryAdmissionResponse Map(LocalExecutionInspectionAdmission admission) => new(
        admission.AdmissionId,
        admission.AdmissionNumber.ToString(CultureInfo.InvariantCulture),
        admission.Ordinal,
        admission.Kind.ToString(),
        admission.PassNumber,
        admission.RecipeOrdinal,
        admission.RecipeName,
        admission.RecordedState.ToString(),
        admission.WaitingReason,
        GetLocalExecutionSequenceInspectionEndpoint.Time(admission.WaitingReasonRecordedAtUtc),
        GetLocalExecutionSequenceInspectionEndpoint.Time(admission.EnqueuedAtUtc),
        GetLocalExecutionSequenceInspectionEndpoint.Time(admission.UpdatedAtUtc),
        admission.Revision.ToString(CultureInfo.InvariantCulture),
        admission.ReasonCode,
        admission.StepId);
}
