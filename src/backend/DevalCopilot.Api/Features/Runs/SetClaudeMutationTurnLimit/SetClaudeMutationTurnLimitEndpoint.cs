using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetClaudeMutationTurnLimit;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Claude agentic-turn limit for this run's later
/// initial implementation and review correction claims. The value is a request, never a measured turn
/// count, a token, cost, or account ceiling, or a host-enforced limit, and it never changes an
/// already-claimed attempt. Accepts no raw provider payload or invocation argument.
/// </summary>
public sealed class SetClaudeMutationTurnLimitEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/claude-mutation-turn-limit")]
    public async Task<ActionResult<SetClaudeMutationTurnLimitResponse>> SetClaudeMutationTurnLimit(
        [FromRoute] Guid runId, [FromBody] SetClaudeMutationTurnLimitRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetClaudeMutationTurnLimitCommand(runId, request.MaxTurns), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetClaudeMutationTurnLimitResponse(result.Value.MaxTurns));
    }
}
