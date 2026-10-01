using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.AuthorizePlanningImplementation;

/// <summary>
/// Records the explicit human authorization of exactly one implementation claim for the final plan of a completed second
/// challenge-resolution round (ADR-0016). The body carries only a rationale; the final Proposal is derived from the
/// persisted escalation. It claims no Agent attempt, spends no budget, and starts no provider. No response or error echoes
/// the rationale, which is visible only in the recorded HumanInstruction and the sealed manifest of the claim that uses it.
/// </summary>
public sealed class AuthorizePlanningImplementationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/planning-escalations/{escalationMessageId:guid}/implementation-authorization")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<AuthorizePlanningImplementationResponse>> AuthorizePlanningImplementation(
        [FromRoute] Guid runId,
        [FromRoute] Guid escalationMessageId,
        [FromBody] AuthorizePlanningImplementationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new AuthorizePlanningImplementationCommand(runId, escalationMessageId, request.Rationale ?? string.Empty),
            cancellationToken);
        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new AuthorizePlanningImplementationResponse(
                result.Value.Status, result.Value.AuthorizationId, result.Value.EscalationMessageId,
                result.Value.FinalProposalMessageId, result.Value.HumanInstructionMessageId, result.Value.LatestEventSequence));
    }
}
