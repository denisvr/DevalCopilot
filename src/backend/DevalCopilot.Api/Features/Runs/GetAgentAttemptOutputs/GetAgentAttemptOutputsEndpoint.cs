using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputs;

/// <summary>
/// Protected, read-only, bodyless inspection of the references of every collaboration message recorded against one Agent attempt
/// (ADR-0054). An unknown run and a missing, foreign or non-Agent attempt are one fixed 404 in the shared Problem Details contract that
/// repeats no value. An unprovable attempt or association set is a 200 Unavailable without any part of it. The complete bounded set is
/// returned with no limit or cursor; collaboration sequences are canonical Int64 strings and times are UTC round-trip strings. The answer
/// is references only: no message text, prompt, artifact or session.
/// </summary>
public sealed class GetAgentAttemptOutputsEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/outputs")]
    [ProducesResponseType<GetAgentAttemptOutputsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptOutputsResponse>> GetAgentAttemptOutputs(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptOutputsQuery(runId, attemptId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptOutputsResponse(
            value.RunId,
            value.AttemptId,
            value.Status.ToString(),
            value.Attempt is { } attempt
                ? new AgentAttemptOutputsHeaderResponse(
                    attempt.Id, attempt.AttemptNumber, attempt.Role.ToString(), attempt.Provider.ToString(), attempt.ResponseContract.ToString())
                : null,
            value.OutputMessageTotalCount,
            value.OutputMessages
                .Select(output => new AgentAttemptOutputReferenceResponse(
                    output.Position,
                    output.CollaborationMessageId,
                    output.Type.ToString(),
                    output.CollaborationSequence.ToString(CultureInfo.InvariantCulture),
                    output.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray()));
    }
}
