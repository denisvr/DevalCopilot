using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetClaudeModelPreference;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Claude model alias for this run's later
/// CriticalReviewer, Implementer, and ReviewCorrection claims. The alias is a request, never an
/// observed or effective model. Never accepts or exposes a raw provider payload or invocation argument.
/// </summary>
public sealed class SetClaudeModelPreferenceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/claude-model-preference")]
    public async Task<ActionResult<SetClaudeModelPreferenceResponse>> SetClaudeModelPreference(
        [FromRoute] Guid runId, [FromBody] SetClaudeModelPreferenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new SetClaudeModelPreferenceCommand(runId, request.RequestedModel), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetClaudeModelPreferenceResponse(result.Value.RequestedModel));
    }
}
