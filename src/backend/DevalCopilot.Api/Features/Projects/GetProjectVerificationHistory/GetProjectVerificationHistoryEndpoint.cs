using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>
/// Protected, read-only, bodyless keyset page of a project's recorded verification executions (ADR-0069), newest first, 20 at a time. It
/// accepts only an optional <c>beforeExecutionNumber</c> (a positive Int32; anything else is a 400, never clamped) and no body, reads no
/// artifact, argument, executable, live status or source file, and attributes no execution to a run. An unknown project is one fixed 404
/// in the shared Problem Details contract that repeats no value.
/// </summary>
public sealed class GetProjectVerificationHistoryEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/verification-history")]
    [TypeFilter(typeof(ProjectVerificationHistoryGuardFilter))]
    [ProducesResponseType<GetProjectVerificationHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetProjectVerificationHistoryResponse>> GetProjectVerificationHistory(
        [FromRoute] Guid projectId,
        [FromQuery, ModelBinder(typeof(ProjectVerificationBeforeExecutionNumberModelBinder))] int? beforeExecutionNumber,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectVerificationHistoryQuery(projectId, beforeExecutionNumber), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetProjectVerificationHistoryResponse(
            value.ProjectId,
            value.Status.ToString(),
            value.Items
                .Select(item => new ProjectVerificationHistoryItemResponse(
                    item.ExecutionId,
                    item.ExecutionNumber,
                    item.CommandName,
                    item.ClaimedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray(),
            value.HasMore,
            value.NextBeforeExecutionNumber));
    }
}
