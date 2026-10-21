using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequence;

/// <summary>
/// Protected, read-only, bodyless status of one run's local execution sequence (ADR-0060): the recorded state, bounded step receipts,
/// fixed reason and next-action text, the immutable pinned tuple and current source, the allowance and deadline, the exact final
/// references when proved, and the manual-control fence. It performs no Git or provider observation and proves no runtime readiness;
/// an unknown run is one fixed 404 in the shared Problem Details contract. Event sequences and revisions are canonical Int64 strings and
/// times are UTC round-trip strings. An answer whose tuple is internally inconsistent is refused instead of being shown (the fixed 422 of the shared Problem Details contract).
/// </summary>
public sealed class GetLocalExecutionSequenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence")]
    [TypeFilter(typeof(LocalExecutionSequenceBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionSequenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetLocalExecutionSequenceResponse>> GetLocalExecutionSequence(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionSequenceQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        if ((value.State is null) != (value.Sequence is null) || (value.Sequence is { } check && check.State != value.State))
        {
            return Inconsistent();
        }

        LocalExecutionSequenceResponse? sequence = null;
        if (value.Sequence is { } snapshot)
        {
            sequence = ToResponse(snapshot);
            if (sequence is null)
            {
                return Inconsistent();
            }
        }

        return Ok(new GetLocalExecutionSequenceResponse(
            value.RunId,
            value.State is { } state ? state.ToString() : "None",
            sequence,
            value.ManualFence.ToString(),
            value.LatestEventSequence.ToString(CultureInfo.InvariantCulture)));
    }

    private ActionResult<GetLocalExecutionSequenceResponse> Inconsistent() =>
        problemDetails.CreateResponse(Result.Failure(LocalExecutionSequenceErrors.PersistenceFailed()), HttpContext);

    /// <summary>The safe mapping: every tuple that is not internally consistent is refused (null) instead of shown.</summary>
    private static LocalExecutionSequenceResponse? ToResponse(LocalExecutionSequenceSnapshot sequence)
    {
        var steps = sequence.Steps;
        var final = sequence.Final;
        var isFinalState = sequence.RecordedState is LocalExecutionSequenceState.ReadyForHumanApproval or LocalExecutionSequenceState.HumanEscalation;
        if (steps.Count > sequence.MaximumSteps
            || sequence.CorrectionsClaimed < 0
            || sequence.CorrectionsClaimed > sequence.CorrectionAllowance
            || !Enum.IsDefined(sequence.State)
            || !Enum.IsDefined(sequence.RecordedState)
            || (final is not null && !isFinalState)
            || (final is { ReviewAttemptId: null, EscalationMessageId: null })
            || steps.Select((step, index) => step.Ordinal == index).Any(consistent => !consistent)
            || steps.Any(step => (step.State is LocalExecutionSequenceStepState.Claimed or LocalExecutionSequenceStepState.Concluded)
                    != (step.OperationId is not null)
                || (step.Kind == LocalExecutionStepKind.Verification) != (step.RecipeOrdinal is not null)))
        {
            return null;
        }

        return new LocalExecutionSequenceResponse(
            sequence.SequenceId,
            sequence.PlanProposalMessageId,
            sequence.PlanAuthority.ToString(),
            sequence.WorkspaceId,
            sequence.CheckpointId,
            sequence.FingerprintSha256,
            sequence.CurrentCheckpointId,
            sequence.CurrentFingerprintSha256,
            sequence.State.ToString(),
            sequence.RecordedState.ToString(),
            sequence.ObservedBeforePersisted,
            sequence.ReasonCode,
            LocalExecutionSequenceText.ReasonText(sequence.ReasonCode),
            sequence.DetailCode,
            sequence.NextActionCode,
            LocalExecutionSequenceText.NextActionText(sequence.NextActionCode),
            sequence.Revision.ToString(CultureInfo.InvariantCulture),
            sequence.RecipeCount,
            sequence.MaximumSteps,
            sequence.CorrectionAllowance,
            sequence.CorrectionsClaimed,
            sequence.PassesStarted,
            sequence.AgentClaims,
            Time(sequence.ArmedAtUtc),
            Time(sequence.AdvancementDeadlineUtc),
            Time(sequence.UpdatedAtUtc),
            sequence.StopRequestedAtUtc is { } stop ? Time(stop) : null,
            sequence.EndedAtUtc is { } ended ? Time(ended) : null,
            sequence.AwaitingReconciliation,
            sequence.OutstandingOperationId,
            steps
                .Select(step => new LocalExecutionSequenceStepResponse(
                    step.StepId,
                    step.Ordinal,
                    step.Kind.ToString(),
                    step.PassNumber,
                    step.RecipeOrdinal,
                    step.RecipeName,
                    step.State.ToString(),
                    step.ReasonCode,
                    step.OperationId,
                    step.OperationStatus,
                    step.OperationOutcome,
                    step.Dispatched,
                    step.ExitRecorded,
                    step.Quarantined,
                    Time(step.PreparationReservedAtUtc),
                    Time(step.PreparationDeadlineUtc),
                    step.ClaimedAtUtc is { } claimed ? Time(claimed) : null,
                    step.ResultDeadlineUtc is { } deadline ? Time(deadline) : null))
                .ToArray(),
            final is null ? null : new LocalExecutionSequenceFinalResponse(final.ReportMessageId, final.ReviewAttemptId, final.EscalationMessageId));
    }

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
