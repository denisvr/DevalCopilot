using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputs;

/// <summary>
/// Protected, read-only, bodyless inspection of the recorded collaboration-input references of one Agent attempt (ADR-0051), independent
/// of any message the attempt produced. An unknown run and a missing, foreign or non-Agent attempt are one fixed 404 in the shared Problem
/// Details contract that repeats no value. The answer is references only: no message text, prompt, artifact or session.
/// </summary>
public sealed class GetAgentAttemptInputsEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/inputs")]
    [ProducesResponseType<GetAgentAttemptInputsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptInputsResponse>> GetAgentAttemptInputs(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptInputsQuery(runId, attemptId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptInputsResponse(
            value.RunId,
            value.AttemptId,
            value.Status.ToString(),
            value.Attempt is { } attempt
                ? new AgentAttemptInputsHeaderResponse(
                    attempt.Id, attempt.AttemptNumber, attempt.Role.ToString(), attempt.Provider.ToString(), attempt.ResponseContract.ToString())
                : null,
            value.InputMessages
                .Select(input => new AgentAttemptInputReferenceResponse(
                    input.Sequence,
                    input.CollaborationMessageId,
                    input.Type.ToString(),
                    input.CollaborationSequence.ToString(CultureInfo.InvariantCulture),
                    input.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray(),
            value.InputMessageTotalCount,
            value.InputMessagesOmitted));
    }
}
