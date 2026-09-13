using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
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
                summary.Stage?.ToString(),
                summary.Capabilities.Select(MapCapability).ToArray()))
            .ToArray();

        return Ok(response);
    }

    private static CapabilityReadinessResponse MapCapability(CapabilityReadinessQueryResult capability) => new(
        capability.Capability.ToString(),
        capability.IsRequired,
        capability.DisplayStatus?.ToString(),
        capability.ReasonCode.ToString(),
        capability.ResolvedExecutablePath,
        capability.Version,
        capability.LastCheckedUtc,
        capability.IsStale);
}
