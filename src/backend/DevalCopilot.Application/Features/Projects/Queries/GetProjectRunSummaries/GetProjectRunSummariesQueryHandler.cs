using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

public sealed class GetProjectRunSummariesQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetProjectRunSummariesQuery, IReadOnlyList<ProjectRunSummaryQueryResult>>
{
    public async Task<IReadOnlyList<ProjectRunSummaryQueryResult>> HandleAsync(
        GetProjectRunSummariesQuery query,
        CancellationToken cancellationToken)
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Select(project => new { project.Id, project.Name })
            .ToListAsync(cancellationToken);

        var runs = await dbContext.Runs
            .AsNoTracking()
            .Select(run => new { run.ProjectId, run.Id, run.ExecutionNumber, run.Lifecycle, run.Stage })
            .ToListAsync(cancellationToken);

        var results = new List<ProjectRunSummaryQueryResult>(projects.Count);

        foreach (var project in projects)
        {
            var mostRelevantRun = runs
                .Where(run => run.ProjectId == project.Id)
                .OrderBy(run => run.Lifecycle == RunLifecycle.Completed ? 1 : 0)
                .ThenByDescending(run => run.ExecutionNumber)
                .FirstOrDefault();

            results.Add(
                new ProjectRunSummaryQueryResult(
                    project.Id,
                    project.Name,
                    mostRelevantRun?.Id,
                    mostRelevantRun?.ExecutionNumber,
                    mostRelevantRun?.Lifecycle,
                    mostRelevantRun?.Stage));
        }

        return results;
    }
}
