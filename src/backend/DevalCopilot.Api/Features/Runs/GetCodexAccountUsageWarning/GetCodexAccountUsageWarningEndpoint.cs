using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageWarning;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetCodexAccountUsageWarning;

/// <summary>
/// One explicit, advisory check of the run's saved Codex account-usage warning (ADR-0026). It accepts no threshold, executable,
/// provider identity or earlier observation, makes at most one bounded strict read of the host's Codex account, writes nothing and
/// is never cached. The ordinary cockpit read never reaches it.
/// </summary>
public sealed class GetCodexAccountUsageWarningEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/codex-account-usage-warning")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<GetCodexAccountUsageWarningResponse>> GetCodexAccountUsageWarning(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetCodexAccountUsageWarningQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetCodexAccountUsageWarningResponse(
            value.State.ToString(),
            value.Reason?.ToString(),
            value.ThresholdPercent,
            value.ObservedAtUtc,
            value.Windows
                .Select(window => new CodexAccountUsageWarningWindowResponse(
                    window.BucketId, window.Kind.ToString(), window.UsedPercent, window.ReachedThreshold))
                .ToArray(),
            value.ProviderReportedLimitReached));
    }
}
