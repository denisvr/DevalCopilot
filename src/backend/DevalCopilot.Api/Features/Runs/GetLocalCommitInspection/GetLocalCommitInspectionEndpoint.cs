using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalCommitInspection;

/// <summary>
/// Protected, read-only, bodyless recorded local-commit operation of one run (ADR-0070): one bounded database statement proves the
/// registered run and project, the operation associated with the run and that operation's named workspace and checkpoint, and returns
/// the closed graph. It accepts no query key and no body (refused before dispatch), mints and requests nothing, reads no Git, artifact
/// or external state and no current eligibility, lease, workspace or lifecycle, and changes nothing. An unknown run and a run whose
/// registered project is absent are one fixed 404 that repeats no value; a run without an operation is <c>NotRecorded</c> and an
/// associated operation that cannot be proved is <c>Unavailable</c>, never absent. Only a <c>Completed</c> operation carries its
/// commit identity.
/// </summary>
public sealed class GetLocalCommitInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-commit-inspection")]
    [TypeFilter(typeof(LocalCommitInspectionGuardFilter))]
    [ProducesResponseType<GetLocalCommitInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalCommitInspectionResponse>> GetLocalCommitInspection(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalCommitInspectionQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalCommitInspectionResponse(
            value.RunId, value.Status.ToString(), value.Operation is { } operation ? Map(operation) : null));
    }

    private static string Instant(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static LocalCommitInspectionOperationResponse Map(RecordedLocalCommitOperation operation) => new(
        operation.OperationId,
        operation.ProjectId,
        operation.WorkspaceId,
        operation.CheckpointId,
        operation.CheckpointNumber,
        operation.CheckpointFingerprintSha256,
        operation.Status.ToString(),
        operation.BranchName,
        operation.ParentCommitSha,
        operation.TreeSha,
        operation.CommitSha,
        operation.ChangedPathCount,
        checked((int)operation.TotalBytes),
        Instant(operation.CreatedAtUtc),
        operation.ExecutionStartedAtUtc is { } started ? Instant(started) : null,
        operation.CompletedAtUtc is { } completed ? Instant(completed) : null,
        new LocalCommitInspectionReasonResponse(operation.Reason.Kind.ToString(), operation.Reason.Code));
}
