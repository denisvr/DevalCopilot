using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>
/// Protected, read-only, bodyless status of one run's planning sequence (ADR-0055): the recorded state, bounded step receipts, fixed reason
/// and next-action text, the immutable pinned tuple, the exact final plan or escalation references when proved, and the manual-control
/// fence. For a run with no sequence it also reports the recorded arming availability. It performs no Git or provider observation and
/// proves no runtime readiness; an unknown run is one fixed 404 in the shared Problem Details contract. Event sequences and revisions are
/// canonical Int64 strings and times are UTC round-trip strings.
/// </summary>
public sealed class GetPlanningSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/planning-sequence")]
    [TypeFilter(typeof(PlanningSequenceBodylessGuardFilter))]
    [ProducesResponseType<GetPlanningSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPlanningSequenceResponse>> GetPlanningSequence(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetPlanningSequenceQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetPlanningSequenceResponse(
            value.RunId,
            value.State is { } state ? state.ToString() : "None",
            value.Sequence is { } sequence ? ToResponse(sequence) : null,
            value.Arming is { } arming
                ? new PlanningSequenceArmingResponse(
                    arming.Available,
                    arming.RefusalCode,
                    PlanningSequenceText.ArmingRefusalText(arming.RefusalCode),
                    arming.WorkspaceId,
                    arming.CheckpointId,
                    arming.FingerprintSha256)
                : null,
            value.ManualFence.ToString(),
            value.LatestEventSequence.ToString(CultureInfo.InvariantCulture)));
    }

    private static PlanningSequenceResponse ToResponse(PlanningSequenceSnapshot sequence) => new(
        sequence.SequenceId,
        sequence.WorkspaceId,
        sequence.CheckpointId,
        sequence.FingerprintSha256,
        sequence.State.ToString(),
        sequence.RecordedState.ToString(),
        sequence.ObservedBeforePersisted,
        sequence.ReasonCode,
        PlanningSequenceText.ReasonText(sequence.ReasonCode),
        sequence.DetailCode,
        sequence.NextActionCode!,
        PlanningSequenceText.NextActionText(sequence.NextActionCode),
        sequence.Revision.ToString(CultureInfo.InvariantCulture),
        Time(sequence.ArmedAtUtc),
        Time(sequence.UpdatedAtUtc),
        sequence.StopRequestedAtUtc is { } stop ? Time(stop) : null,
        sequence.EndedAtUtc is { } ended ? Time(ended) : null,
        sequence.AwaitingReconciliation,
        sequence.OutstandingAttemptId,
        DevalCopilot.Domain.Features.Runs.PlanningSequencePolicy.MaximumSteps,
        sequence.Steps
            .Select(step => new PlanningSequenceStepResponse(
                step.StepId,
                step.Ordinal,
                step.Role.ToString(),
                step.Provider.ToString(),
                step.State.ToString(),
                step.ReasonCode,
                step.AttemptId,
                step.AttemptNumber,
                step.AttemptStatus?.ToString(),
                step.AttemptOutcome?.ToString(),
                step.Dispatched,
                Time(step.PreparationReservedAtUtc),
                Time(step.PreparationDeadlineUtc),
                step.ClaimedAtUtc is { } claimed ? Time(claimed) : null,
                step.ResultDeadlineUtc is { } deadline ? Time(deadline) : null))
            .ToArray(),
        sequence.Final is { } final
            ? new PlanningSequenceFinalResponse(
                final.FinalProposalMessageId, final.AcceptanceMessageId, final.EscalationMessageId, final.ProvingAttemptId)
            : null);

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
