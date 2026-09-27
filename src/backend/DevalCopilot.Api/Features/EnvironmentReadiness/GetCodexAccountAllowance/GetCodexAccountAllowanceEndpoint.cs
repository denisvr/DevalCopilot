using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetCodexAccountAllowance;

public sealed class GetCodexAccountAllowanceEndpoint(IApplicationMediator mediator) : EnvironmentBaseEndpoint
{
    [HttpGet("codex-account-allowance")]
    public async Task<ActionResult<CodexAccountAllowanceResponse>> GetCodexAccountAllowance(CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCodexAccountAllowanceQuery(), cancellationToken);

        return Ok(new CodexAccountAllowanceResponse(
            result.Status.ToString(),
            result.RetrievedAtUtc,
            result.Buckets.Select(bucket => new CodexAllowanceBucketResponse(
                bucket.LimitId,
                MapWindow(bucket.Primary),
                MapWindow(bucket.Secondary))).ToArray()));
    }

    private static CodexAllowanceWindowResponse? MapWindow(CodexAllowanceWindow? window) =>
        window is null ? null : new CodexAllowanceWindowResponse(window.UsedPercent, window.WindowDurationMins, window.ResetsAtUtc);
}
