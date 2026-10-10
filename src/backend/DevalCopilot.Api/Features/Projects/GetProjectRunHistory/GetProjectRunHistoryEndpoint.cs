using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectRunHistory;

/// <summary>
/// Protected, read-only, project-scoped page of recorded runs in descending execution-number order with an exclusive
/// before-number cursor (ADR-0033). An unknown project is a safe 404 and a cursor or limit outside its bounds, or one that is not a
/// whole 32-bit integer, is a 400, never clamped; both are the shared Problem Details contract and neither repeats a rejected value.
/// It reads persisted facts only and exposes no path, provider payload, artifact or stored enum text.
/// </summary>
public sealed class GetProjectRunHistoryEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/run-history")]
    [ProducesResponseType<GetProjectRunHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetProjectRunHistoryResponse>> GetProjectRunHistory(
        [FromRoute] Guid projectId,
        [FromQuery, ModelBinder(typeof(RunHistoryScalarModelBinder))] int? beforeExecutionNumber,
        [FromQuery, ModelBinder(typeof(RunHistoryScalarModelBinder))] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectRunHistoryQuery(projectId, beforeExecutionNumber, limit), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetProjectRunHistoryResponse(
            value.ProjectId,
            value.Entries.Select(entry => Map(value.ProjectId, entry)).ToArray(),
            value.HasMore,
            value.NextBeforeExecutionNumber));
    }

    private static ProjectRunHistoryEntryResponse Map(Guid projectId, ProjectRunHistoryEntry entry) => new(
        projectId,
        entry.RunId,
        entry.ExecutionNumber,
        entry.Objective,
        entry.Lifecycle?.ToString() ?? RunExecutionModeResponse.Unrecognized,
        entry.Stage?.ToString() ?? RunExecutionModeResponse.Unrecognized,
        RunExecutionModeResponse.From(entry.ExecutionMode),
        entry.CreatedAtUtc,
        entry.LastAdvancedAtUtc,
        entry.ReceiptSource is { } source
            ? new ProjectRunHistoryReceiptSourceResponse(
                source.RunId, source.OperationId, source.CommitSha, source.CheckpointId, source.CheckpointNumber)
            : null);
}
