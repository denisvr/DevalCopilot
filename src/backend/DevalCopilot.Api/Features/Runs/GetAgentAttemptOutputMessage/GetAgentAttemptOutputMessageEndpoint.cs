using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;
using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptOutputMessage;

/// <summary>
/// Protected, read-only, bodyless inspection of the recorded content of one collaboration message that a proved output set of one Agent
/// attempt contains (ADR-0054). An unknown run, a missing, foreign or non-Agent attempt and a message that is not a member of a proved
/// set are one fixed 404 in the shared Problem Details contract that repeats no value. An unprovable set or a reread association,
/// header, provenance or actor that disagrees with the admitted reference is a 200 Unavailable without any part of the set. Both
/// collaboration sequences are canonical Int64 strings and both times are UTC round-trip strings. No reply, producing-attempt chain,
/// artifact, raw JSON or provider session is exposed.
/// </summary>
public sealed class GetAgentAttemptOutputMessageEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    private const string Unrecognized = "Unrecognized";

    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/outputs/{messageId:guid}")]
    [ProducesResponseType<GetAgentAttemptOutputMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptOutputMessageResponse>> GetAgentAttemptOutputMessage(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, [FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptOutputMessageQuery(runId, attemptId, messageId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptOutputMessageResponse(
            value.RunId,
            value.AttemptId,
            value.CollaborationMessageId,
            value.Status.ToString(),
            value.Attempt is { } attempt
                ? new AgentAttemptOutputMessageAttemptResponse(
                    attempt.Id, attempt.AttemptNumber, attempt.Role.ToString(), attempt.Provider.ToString(), attempt.ResponseContract.ToString())
                : null,
            value.Output is { } output
                ? new AgentAttemptOutputMessageOutputResponse(
                    output.Position,
                    output.CollaborationMessageId,
                    output.Type.ToString(),
                    output.CollaborationSequence.ToString(CultureInfo.InvariantCulture),
                    output.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
                : null,
            value.Message is { } message ? MapMessage(message) : null));
    }

    private static AgentAttemptOutputCollaborationMessageResponse MapMessage(CollaborationHistoryEntry message) => new(
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
        message.Fields.Select(field => new AgentAttemptOutputMessageFieldResponse(field.Name, field.Value)).ToArray());

    private static AgentAttemptOutputMessageParticipantResponse MapParticipant(CollaborationHistoryParticipant participant) => new(
        participant.Kind?.ToString() ?? Unrecognized, participant.Role?.ToString(), participant.Provider?.ToString());
}
