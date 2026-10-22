using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceAvailability;

/// <summary>
/// Protected, read-only, bodyless and queryless recorded arming availability of one explicitly selected plan for one run (ADR-0060):
/// the exact confirmation tuple with the bounded recipe identifiers, numbers and safe names, the requested assignment disclosures and the
/// automatic correction allowance, or one fixed refusal code. It performs no Git or provider observation and runs nothing. The plan
/// identifier is a path segment in the exact 36-character form; any other spelling is the fixed 400. An unknown run is one fixed 404.
/// </summary>
public sealed class GetLocalExecutionSequenceAvailabilityEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/availability/{planProposalMessageId}")]
    [TypeFilter(typeof(LocalExecutionSequenceBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionSequenceAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionSequenceAvailabilityResponse>> GetLocalExecutionSequenceAvailability(
        [FromRoute] Guid runId, [FromRoute] string planProposalMessageId, CancellationToken cancellationToken)
    {
        if (planProposalMessageId.Length != 36
            || !Guid.TryParseExact(planProposalMessageId, "D", out var planId)
            || planId == Guid.Empty)
        {
            return problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.InvalidRequest()), HttpContext);
        }

        var result = await mediator.SendAsync(new GetLocalExecutionSequenceAvailabilityQuery(runId, planId), cancellationToken);
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
