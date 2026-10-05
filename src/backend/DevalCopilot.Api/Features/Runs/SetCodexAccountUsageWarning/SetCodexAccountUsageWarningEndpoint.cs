using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetCodexAccountUsageWarning;

/// <summary>
/// Sets or clears the owner's optional, run-scoped advisory Codex account-usage warning (ADR-0026): a used-percent threshold from 1
/// through 100 that a separate, explicit check compares with one strict observation. It contacts no provider, refuses and stops
/// nothing, and is independent of the account-usage stop. Accepts no raw provider payload or observation, and its request body is
/// bounded to 8 KiB.
/// </summary>
public sealed class SetCodexAccountUsageWarningEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/codex-account-usage-warning")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<SetCodexAccountUsageWarningResponse>> SetCodexAccountUsageWarning(
        [FromRoute] Guid runId, [FromBody] SetCodexAccountUsageWarningRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetCodexAccountUsageWarningCommand(runId, request.Percent), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetCodexAccountUsageWarningResponse(result.Value.Percent));
    }
}
