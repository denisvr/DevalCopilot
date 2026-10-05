using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetCodexAccountUsageStop;

/// <summary>
/// Sets or clears the owner's optional, run-scoped Codex account-usage stop (ADR-0025): a used-percent threshold from 1 through 100
/// enforced on this run's later Codex claims and again before their invocation. It is a local guard over a provider-reported
/// percentage, never account access, readiness or remaining quota. Changing it never alters the threshold an already-claimed attempt
/// recorded, but a claimed attempt is still checked before dispatch. Accepts no raw provider payload, observation or invocation
/// argument, and its request body is bounded to 8 KiB.
/// </summary>
public sealed class SetCodexAccountUsageStopEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/codex-account-usage-stop")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<SetCodexAccountUsageStopResponse>> SetCodexAccountUsageStop(
        [FromRoute] Guid runId, [FromBody] SetCodexAccountUsageStopRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetCodexAccountUsageStopCommand(runId, request.Percent), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetCodexAccountUsageStopResponse(result.Value.Percent));
    }
}
