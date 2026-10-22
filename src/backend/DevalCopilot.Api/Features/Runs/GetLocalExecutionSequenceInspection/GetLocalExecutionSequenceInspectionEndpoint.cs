using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// Protected, read-only, bodyless recorded index of one run's local execution sequence (ADR-0061): the stored bookkeeping header and every
/// recorded step with its recorded link, proved as a whole from bounded persisted facts. It accepts no query key and no body, invokes no
/// admission, ownership or mutation handler and observes no lease, workspace, Git, process, provider or account state. An unknown run is
/// one fixed 404 in the shared Problem Details contract; a known run without a sequence is <c>NotRecorded</c>.
/// </summary>
public sealed class GetLocalExecutionSequenceInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/inspection")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionSequenceInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionSequenceInspectionResponse>> GetLocalExecutionSequenceInspection(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionSequenceInspectionQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalExecutionSequenceInspectionResponse(
            value.RunId,
            value.Status.ToString(),
            value.Sequence is { } header ? Map(header) : null,
            value.Steps.Select(Map).ToArray()));
    }

    internal static LocalExecutionInspectionStepResponse Map(LocalExecutionInspectionStep step) => new(
        step.StepId,
        step.Ordinal,
        step.Kind.ToString(),
        step.PassNumber,
        step.RecipeOrdinal,
        step.RecipeName,
        step.RecordedState.ToString(),
        step.ReasonCode,
        step.SourceCheckpointId,
        step.SourceFingerprintSha256,
        step.ReportMessageId,
        step.SubjectAttemptId,
        step.OperationId,
        Time(step.PreparationReservedAtUtc),
        Time(step.PreparationDeadlineUtc),
        step.ClaimedAtUtc is { } claimed ? Time(claimed) : null,
        step.ResultDeadlineUtc is { } deadline ? Time(deadline) : null);

    internal static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static LocalExecutionInspectionHeaderResponse Map(LocalExecutionInspectionHeader header) => new(
        header.SequenceId,
        header.PlanProposalMessageId,
        header.PlanAuthority.ToString(),
        header.WorkspaceId,
        header.CheckpointId,
        header.FingerprintSha256,
        header.RecordedState.ToString(),
        header.ReasonCode,
        header.Revision.ToString(CultureInfo.InvariantCulture),
        header.RecipeCount,
        header.CorrectionAllowance,
        Time(header.ArmedAtUtc),
        Time(header.UpdatedAtUtc),
        header.EndedAtUtc is { } ended ? Time(ended) : null,
        header.AdmissionMode.ToString());
}
