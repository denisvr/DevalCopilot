using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;

public sealed class GetProjectRunSummariesEndpoint(IApplicationMediator mediator) : ProjectsBaseEndpoint
{
    [HttpGet("run-summaries")]
    public async Task<ActionResult<IReadOnlyList<ProjectRunSummaryResponse>>> GetProjectRunSummaries(
        CancellationToken cancellationToken)
    {
        var summaries = await mediator.SendAsync(new GetProjectRunSummariesQuery(), cancellationToken);

        var response = summaries
            .Select(summary => new ProjectRunSummaryResponse(
                summary.ProjectId,
                summary.ProjectName,
                summary.RunId,
                summary.ExecutionNumber,
                summary.Lifecycle?.ToString(),
                summary.Stage?.ToString()))
            .ToArray();

        return Ok(response);
    }
}
