using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.LocalExecutionQueue.GetLocalExecutionQueueInspection;

/// <summary>
/// Protected, read-only, bodyless and queryless recorded outstanding admissions of every queued local execution cycle in the database
/// (ADR-0064), proved as a whole from one database statement. It accepts no query key and no body (the shared inspection guard), invokes
/// no admission, ownership or mutation handler, reads no artifact, and observes no lease, workspace, Git, process, provider or account
/// state. A defect anywhere makes the closed <c>Unavailable</c> answer with no entries.
/// </summary>
public sealed class GetLocalExecutionQueueInspectionEndpoint(IApplicationMediator mediator) : LocalExecutionQueueBaseEndpoint
{
    [HttpGet("inspection")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionQueueInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetLocalExecutionQueueInspectionResponse>> GetLocalExecutionQueueInspection(
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionQueueInspectionQuery(), cancellationToken);
        return Ok(new GetLocalExecutionQueueInspectionResponse(
            result.Status.ToString(),
            result.Entries.Select(entry => new LocalExecutionQueueInspectionEntryResponse(
                entry.ProjectId,
                entry.ProjectName,
                entry.RunId,
                GetLocalExecutionAdmissionHistoryEndpoint.Map(entry.Sequence),
                GetLocalExecutionAdmissionHistoryEndpoint.Map(entry.Admission))).ToArray()));
    }
}
