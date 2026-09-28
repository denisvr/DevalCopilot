using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.SetCodexAssignmentPreference;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Codex model/effort for this run's
/// later Planner, Challenge Resolver, and Code Reviewer claims. Never accepts or exposes a raw
/// provider payload, credential, or invocation argument.
/// </summary>
public sealed class SetCodexAssignmentPreferenceEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpPost("{runId:guid}/codex-assignment-preference")]
    public async Task<ActionResult<SetCodexAssignmentPreferenceResponse>> SetCodexAssignmentPreference(
        [FromRoute] Guid runId, [FromBody] SetCodexAssignmentPreferenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new SetCodexAssignmentPreferenceCommand(runId, request.RequestedModel, request.RequestedEffort), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new SetCodexAssignmentPreferenceResponse(result.Value.RequestedModel, result.Value.RequestedEffort));
    }
}
