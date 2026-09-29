using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetTokenStopThreshold;

/// <summary>
/// Sets or clears one provider's token-activity stop threshold for this run. Once that provider's
/// locally recorded, provider-reported usage reaches the threshold, its next Agent claim is refused;
/// the stop is a retrospective local guardrail, never an account allowance or a per-attempt cap.
/// Accepts no raw provider payload or invocation argument.
/// </summary>
public sealed class SetTokenStopThresholdEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/token-stop-threshold")]
    public async Task<ActionResult<SetTokenStopThresholdResponse>> SetTokenStopThreshold(
        [FromRoute] Guid runId, [FromBody] SetTokenStopThresholdRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new SetTokenStopThresholdCommand(runId, request.Provider, request.ThresholdTokens), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetTokenStopThresholdResponse(result.Value.Provider, result.Value.ThresholdTokens));
    }
}
