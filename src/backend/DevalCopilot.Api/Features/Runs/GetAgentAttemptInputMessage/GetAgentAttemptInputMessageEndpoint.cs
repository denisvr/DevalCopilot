using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptInputMessage;

/// <summary>
/// Protected, read-only, bodyless inspection of the recorded content of one collaboration message that a proved input set of one Agent
/// attempt references (ADR-0053). An unknown run, a missing, foreign or non-Agent attempt and a message that is not a member of a proved
/// set are one fixed 404 in the shared Problem Details contract that repeats no value. An unprovable set or a reread header that disagrees
/// with the admitted reference is a 200 Unavailable without any part of the set. Both collaboration sequences are canonical Int64 strings
/// and both times are UTC round-trip strings. No reply, producing attempt, artifact, raw JSON or provider session is exposed.
/// </summary>
public sealed class GetAgentAttemptInputMessageEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const string Unrecognized = "Unrecognized";

    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/inputs/{messageId:guid}")]
    [ProducesResponseType<GetAgentAttemptInputMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptInputMessageResponse>> GetAgentAttemptInputMessage(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, [FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptInputMessageQuery(runId, attemptId, messageId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptInputMessageResponse(
            value.RunId,
            value.AttemptId,
            value.CollaborationMessageId,
            value.Status.ToString(),
            value.Attempt is { } attempt
                ? new AgentAttemptInputMessageAttemptResponse(
                    attempt.Id, attempt.AttemptNumber, attempt.Role.ToString(), attempt.Provider.ToString(), attempt.ResponseContract.ToString())
                : null,
            value.Input is { } input
                ? new AgentAttemptInputMessageInputResponse(
                    input.Sequence,
                    input.CollaborationMessageId,
                    input.Type.ToString(),
                    input.CollaborationSequence.ToString(CultureInfo.InvariantCulture),
                    input.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
                : null,
            value.Message is { } message ? MapMessage(message) : null));
    }

    private static AgentAttemptInputCollaborationMessageResponse MapMessage(CollaborationHistoryEntry message) => new(
        message.RunId,
        message.Id,
        message.Sequence.ToString(CultureInfo.InvariantCulture),
        message.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        message.ProtocolVersion ?? Unrecognized,
        message.Type ?? Unrecognized,
        message.Provenance ?? Unrecognized,
        MapParticipant(message.Actor),
        MapParticipant(message.Recipient),
        message.ContentAvailable ? "Available" : "Unavailable",
        message.Summary,
        message.Fields.Select(field => new AgentAttemptInputMessageFieldResponse(field.Name, field.Value)).ToArray());

    private static AgentAttemptInputMessageParticipantResponse MapParticipant(CollaborationHistoryParticipant participant) => new(
        participant.Kind?.ToString() ?? Unrecognized, participant.Role?.ToString(), participant.Provider?.ToString());
}
