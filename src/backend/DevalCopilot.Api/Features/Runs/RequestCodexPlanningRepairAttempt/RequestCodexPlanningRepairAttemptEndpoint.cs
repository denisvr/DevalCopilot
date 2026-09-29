using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestCodexPlanningRepairAttempt;

/// <summary>
/// Requests the one manual format repair of one exact Codex planning attempt that ended with an
/// invalid structured response. The server alone decides eligibility (same run, latest Agent
/// attempt, not itself a repair, not already repaired) and applies every protection of an ordinary
/// planning claim. Never accepts a body, prompt, path, or free text, and never returns the source's
/// response, parser detail, or artifact path; the result is a fresh Proposal attempt, not a
/// correction of the source.
/// </summary>
public sealed class RequestCodexPlanningRepairAttemptEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/agent-attempts/{sourceAttemptId:guid}/codex-plan-repair")]
    public async Task<ActionResult<RequestCodexPlanningRepairAttemptResponse>> RequestCodexPlanningRepairAttempt(
        [FromRoute] Guid runId, [FromRoute] Guid sourceAttemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(runId, sourceAttemptId), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestCodexPlanningRepairAttemptResponse(
                result.Value.AttemptId, result.Value.AttemptNumber, sourceAttemptId));
    }
}
