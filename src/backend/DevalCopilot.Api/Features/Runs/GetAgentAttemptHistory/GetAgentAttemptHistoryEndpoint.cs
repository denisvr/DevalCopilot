using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptHistory;

/// <summary>
/// Protected, read-only, run-scoped page of Agent attempts in descending attempt-number order with a
/// stable exclusive before-number cursor and a hard page cap. Only Agent attempts appear. An unknown
/// run is a safe 404; a non-positive cursor or limit is a 400. Performs no provider call, Git
/// capture, or artifact read.
/// </summary>
public sealed class GetAgentAttemptHistoryEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int DefaultLimit = 10;
    private const int HardMaxLimit = 20;

    [HttpGet("{runId:guid}/agent-attempts")]
    public async Task<ActionResult<AgentAttemptHistoryResponse>> GetAgentAttemptHistory(
        [FromRoute] Guid runId,
        [FromQuery] int? beforeAttemptNumber,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (beforeAttemptNumber is < 1 || limit is < 1)
        {
            return BadRequest();
        }

        var boundedLimit = Math.Min(limit ?? DefaultLimit, HardMaxLimit);

        var result = await mediator.SendAsync(
            new GetAgentAttemptHistoryQuery(runId, beforeAttemptNumber, boundedLimit), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new AgentAttemptHistoryResponse(
            value.Items
                .Select(entry => new AgentAttemptHistoryEntryResponse(
                    entry.AttemptId,
                    entry.AttemptNumber,
                    entry.Status?.ToString(),
                    entry.ClaimedAtUtc,
                    entry.CompletedAtUtc,
                    entry.DispatchedAtUtc,
                    entry.IdentityValid,
                    entry.Role?.ToString(),
                    entry.Provider?.ToString(),
                    entry.ResponseContract?.ToString(),
                    entry.Outcome?.ToString(),
                    entry.RepairSourceAttemptId,
                    entry.RepairSourceAttemptNumber))
                .ToArray(),
            value.HasMore,
            value.NextBeforeAttemptNumber));
    }
}
