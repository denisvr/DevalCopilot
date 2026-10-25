using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;

/// <summary>
/// Protected, read-only, bodyless release candidate or receipt of one recorded step of a run's local execution sequence (ADR-0065). One
/// bounded database statement proves the exact historical chain of the step's witness, original-host physical termination observation,
/// release and Human event and returns the closed graph: only <c>Available</c> carries a candidate with its short-lived exact confirmation
/// and only <c>Released</c> a receipt. It accepts no query key and no body, reads nothing external and no current eligibility, and
/// changes nothing. An unknown run, a run without a sequence and a step outside it are one fixed 404 that repeats no value.
/// </summary>
public sealed class GetLocalExecutionOwnershipReleaseEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/steps/{stepId:guid}/ownership-release")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<LocalExecutionOwnershipReleaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocalExecutionOwnershipReleaseResponse>> GetLocalExecutionOwnershipRelease(
        [FromRoute] Guid runId, [FromRoute] Guid stepId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionOwnershipReleaseQuery(runId, stepId), cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(LocalExecutionOwnershipReleaseResponses.Map(result.Value));
    }
}
