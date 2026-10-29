using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetRunAbandonmentInspection;

/// <summary>
/// Protected, read-only, bodyless and queryless recorded abandonment of one run (ADR-0071): one bounded database statement proves the
/// registered run and project and the run's exact run.abandoned events and returns the closed graph. It accepts no query key and no
/// body (refused before dispatch), reads no present eligibility, blocker, workspace or external state and changes nothing. An unknown
/// run and a run whose registered project is absent are one fixed 404 that repeats no value; a recognized run without any abandonment
/// fact is <c>NotRecorded</c> and abandonment evidence that cannot be proved is <c>Unavailable</c>, never absent. Only <c>Recorded</c>
/// carries the reason, time and supporting event identity.
/// </summary>
public sealed class GetRunAbandonmentInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/abandonment-inspection")]
    [TypeFilter(typeof(RunAbandonmentInspectionGuardFilter))]
    [ProducesResponseType<GetRunAbandonmentInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetRunAbandonmentInspectionResponse>> GetRunAbandonmentInspection(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetRunAbandonmentInspectionQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetRunAbandonmentInspectionResponse(
            value.RunId, value.Status.ToString(), value.Abandonment is { } abandonment ? Map(abandonment) : null));
    }

    private static RunAbandonmentInspectionRecordedResponse Map(RecordedAbandonment abandonment) => new(
        abandonment.ProjectId,
        abandonment.Reason,
        abandonment.AbandonedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        abandonment.EventId,
        abandonment.EventSequence.ToString(CultureInfo.InvariantCulture));
}
