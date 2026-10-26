using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

/// <summary>
/// Protected, read-only, bodyless recorded index of one run's planning sequence (ADR-0068): the stored bookkeeping header and every
/// recorded step with its recorded link, proved structurally as a whole from bounded persisted facts. It accepts no query key and no
/// body, invokes no live status, arming, classifier, admission, fence or mutation handler and observes no lease, latest workspace or
/// checkpoint, Git, process, provider or account state. An unknown run is one fixed 404 in the shared Problem Details contract; a known
/// run without a sequence is <c>NotRecorded</c>.
/// </summary>
public sealed class GetPlanningSequenceInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/planning-sequence/inspection")]
    [TypeFilter(typeof(PlanningSequenceInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetPlanningSequenceInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetPlanningSequenceInspectionResponse>> GetPlanningSequenceInspection(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetPlanningSequenceInspectionQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetPlanningSequenceInspectionResponse(
            value.RunId,
            value.Status.ToString(),
            value.Sequence is { } header ? Map(header) : null,
            value.Steps.Select(Map).ToArray()));
    }

    internal static PlanningSequenceInspectionStepResponse Map(PlanningSequenceInspectionStep step) => new(
        step.StepId,
        step.Ordinal,
        step.Role.ToString(),
        step.Provider.ToString(),
        step.ResponseContract.ToString(),
        step.RecordedState.ToString(),
        step.ReasonCode,
        step.Revision.ToString(CultureInfo.InvariantCulture),
        step.PredecessorAttemptId,
        step.InputProposalMessageId,
        step.AttemptId,
        Time(step.PreparationReservedAtUtc),
        Time(step.PreparationDeadlineUtc),
        Time(step.UpdatedAtUtc));

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static PlanningSequenceInspectionHeaderResponse Map(PlanningSequenceInspectionHeader header) => new(
        header.SequenceId,
        header.WorkspaceId,
        header.CheckpointId,
        header.FingerprintSha256,
        header.RecordedState.ToString(),
        header.ReasonCode,
        header.Revision.ToString(CultureInfo.InvariantCulture),
        Time(header.ArmedAtUtc),
        Time(header.UpdatedAtUtc),
        header.StopRequestedAtUtc is { } stopRequested ? Time(stopRequested) : null,
        header.EndedAtUtc is { } ended ? Time(ended) : null);
}
