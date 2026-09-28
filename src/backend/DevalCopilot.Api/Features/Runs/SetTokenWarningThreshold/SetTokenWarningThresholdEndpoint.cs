using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetTokenWarningThreshold;

/// <summary>
/// Sets or clears one provider's advisory token-activity warning threshold for this run. The
/// threshold is a warning on locally recorded provider-reported usage, never a budget or an
/// eligibility rule. Accepts no raw provider payload or invocation argument.
/// </summary>
public sealed class SetTokenWarningThresholdEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/token-warning-threshold")]
    public async Task<ActionResult<SetTokenWarningThresholdResponse>> SetTokenWarningThreshold(
        [FromRoute] Guid runId, [FromBody] SetTokenWarningThresholdRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new SetTokenWarningThresholdCommand(runId, request.Provider, request.ThresholdTokens), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetTokenWarningThresholdResponse(result.Value.Provider, result.Value.ThresholdTokens));
    }
}
