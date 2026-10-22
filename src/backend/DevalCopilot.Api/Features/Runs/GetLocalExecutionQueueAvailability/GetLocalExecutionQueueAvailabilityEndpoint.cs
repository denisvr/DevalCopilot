using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionQueueAvailability;

/// <summary>
/// Protected, read-only, bodyless recorded queue availability of one explicitly selected plan for one run (ADR-0062): the exact queue
/// confirmation tuple (the Immediate confirmation cannot queue), the bounded recipe identifiers, numbers and safe names, the requested
/// assignment disclosures and the automatic correction allowance, or one fixed refusal code. The single required query key
/// <c>planProposalMessageId</c> appears once in exactly this ordinal spelling; any other query shape or body is the fixed 400. It
/// performs no Git or provider observation and runs nothing: queue intake pins the recorded source and the source must still match when
/// the admitted work starts. An unknown run is one fixed 404.
/// </summary>
public sealed class GetLocalExecutionQueueAvailabilityEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/queue/availability")]
    [TypeFilter(typeof(StrictLocalExecutionQueueAvailabilityQueryFilter))]
    [ProducesResponseType<GetLocalExecutionSequenceAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionSequenceAvailabilityResponse>> GetLocalExecutionQueueAvailability(
        [FromRoute] Guid runId, [FromQuery] string planProposalMessageId, CancellationToken cancellationToken)
    {
        if (planProposalMessageId.Length != 36
            || !Guid.TryParseExact(planProposalMessageId, "D", out var planId)
            || planId == Guid.Empty)
        {
            return problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.InvalidRequest()), HttpContext);
        }

        var result = await mediator.SendAsync(
            new GetLocalExecutionSequenceAvailabilityQuery(runId, planId, LocalExecutionAdmissionMode.WaitForAdmission), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var response = GetLocalExecutionSequenceAvailabilityResponses.Map(result.Value);
        return response is null
            ? problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.PersistenceFailed()), HttpContext)
            : Ok(response);
    }
}
