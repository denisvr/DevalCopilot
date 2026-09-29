using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetClaudeModelPreference;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Claude model alias and optional effort level for this
/// run's later
/// CriticalReviewer, Implementer, and ReviewCorrection claims. The pair is a request, never an
/// observed or effective model or effort. Never accepts or exposes a raw provider payload or invocation argument.
/// </summary>
public sealed class SetClaudeModelPreferenceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/claude-model-preference")]
    public async Task<ActionResult<SetClaudeModelPreferenceResponse>> SetClaudeModelPreference(
        [FromRoute] Guid runId, [FromBody] SetClaudeModelPreferenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetClaudeModelPreferenceCommand(runId, request.RequestedModel, request.RequestedEffort), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetClaudeModelPreferenceResponse(result.Value.RequestedModel, result.Value.RequestedEffort));
    }
}
