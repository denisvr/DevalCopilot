using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentSupervision;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentSupervision;

/// <summary>
/// Protected, read-only, bodyless and queryless current host supervision of one run's selected Agent attempt (ADR-0056): the phase the
/// host observer last reported, its last committed heartbeat and the absolute phase deadline, with the attempt's real recorded status.
/// It writes nothing, performs no Git or provider observation, and proves neither provider progress nor a live child process; an elapsed
/// deadline reads as attention immediately. An unknown run is one fixed 404 in the shared Problem Details contract. Revisions are
/// canonical decimal Int64 strings and times are UTC round-trip strings.
/// </summary>
public sealed class GetAgentSupervisionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-supervision")]
    [TypeFilter(typeof(AgentSupervisionBodylessGuardFilter))]
    [ProducesResponseType<GetAgentSupervisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentSupervisionResponse>> GetAgentSupervision(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentSupervisionQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentSupervisionResponse(
            value.RunId,
            value.Status.ToString(),
            value.ReasonCode,
            AgentSupervisionText.ReasonText(value.ReasonCode),
            value.NextActionCode,
            AgentSupervisionText.NextActionText(value.NextActionCode),
            value.Attempt is { } attempt ? ToResponse(attempt) : null,
            value.Observation is { } observation ? ToResponse(observation) : null,
            value.Attention is { } attention ? ToResponse(attention) : null,
            Time(value.ObservedAtUtc)));
    }

    private static AgentSupervisionAttemptResponse ToResponse(AgentSupervisionAttemptView attempt) => new(
        attempt.AttemptId,
        attempt.AttemptNumber,
        attempt.Role.ToString(),
        attempt.Provider.ToString(),
        attempt.ResponseContract.ToString(),
        attempt.Status.ToString(),
        attempt.Outcome?.ToString(),
        attempt.DispatchedAtUtc is not null,
        Time(attempt.ClaimedAtUtc),
        attempt.DispatchedAtUtc is { } dispatched ? Time(dispatched) : null,
        attempt.CompletedAtUtc is { } completed ? Time(completed) : null);

    private static AgentSupervisionObservationResponse ToResponse(AgentSupervisionObservationView observation) => new(
        observation.HostInstanceId,
        observation.ObserverId,
        observation.OwnedByCurrentHost,
        observation.Revision.ToString(CultureInfo.InvariantCulture),
        observation.Phase.ToString(),
        Time(observation.StartedAtUtc),
        Time(observation.LastHeartbeatAtUtc),
        Time(observation.PhaseEnteredAtUtc),
        Time(observation.PhaseDeadlineUtc),
        observation.ClosedAtUtc is null,
        observation.ClosureReason,
        observation.ClosedAtUtc is { } closed ? Time(closed) : null);

    private static AgentSupervisionAttentionResponse ToResponse(AgentSupervisionAttentionView attention) => new(
        attention.EffectiveReason,
        AgentSupervisionText.ReasonText(attention.EffectiveReason)!,
        Time(attention.EffectiveAtUtc),
        attention.RecordedReason,
        attention.RecordedAtUtc is { } recorded ? Time(recorded) : null);

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
