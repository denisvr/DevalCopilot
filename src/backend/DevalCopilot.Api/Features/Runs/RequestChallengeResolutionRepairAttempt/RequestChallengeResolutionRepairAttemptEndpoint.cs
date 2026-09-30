using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestChallengeResolutionRepairAttempt;

/// <summary>
/// Requests the one manual format repair of one exact challenge-resolution attempt that ended with an
/// invalid structured response. The server alone decides eligibility (same run, the exact failed
/// source shape, latest Agent attempt, not itself a repair, not already repaired, exact current inputs)
/// and derives the target from the source's persisted inputs; every protection of an ordinary claim
/// applies. Never accepts a body, target, prompt, path, provider, model, or free text, and never returns
/// the source's response, parser detail, or artifact path; the result is a fresh attempt, not a
/// correction of the source.
/// </summary>
public sealed class RequestChallengeResolutionRepairAttemptEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/{sourceAttemptId:guid}/challenge-resolution-repair")]
    public async Task<ActionResult<RequestChallengeResolutionRepairAttemptResponse>> RequestChallengeResolutionRepairAttempt(
        [FromRoute] Guid runId, [FromRoute] Guid sourceAttemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(CreateChallengeResolutionAttemptCommand.ForRepair(runId, sourceAttemptId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestChallengeResolutionRepairAttemptResponse(result.Value.AttemptId, result.Value.AttemptNumber, sourceAttemptId));
    }
}
