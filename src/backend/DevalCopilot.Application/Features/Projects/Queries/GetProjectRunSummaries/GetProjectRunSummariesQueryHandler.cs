using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

public sealed class GetProjectRunSummariesQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetProjectRunSummariesQuery, IReadOnlyList<ProjectRunSummaryQueryResult>>
{
    public async Task<IReadOnlyList<ProjectRunSummaryQueryResult>> HandleAsync(
        GetProjectRunSummariesQuery query,
        CancellationToken cancellationToken)
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Select(project => new { project.Id, project.Name, project.CanonicalPath })
            .ToListAsync(cancellationToken);

        var runs = await dbContext.Runs
            .AsNoTracking()
            .Select(run => new { run.ProjectId, run.Id, run.ExecutionNumber, run.Lifecycle, run.Stage })
            .ToListAsync(cancellationToken);

        // Every project's baselines, narrowly projected; "current" is picked client-side below
        // as the greatest BaselineNumber — never the latest ObservedAtUtc, which is display
        // metadata only.
        var baselines = await dbContext.RepositoryBaselines
            .AsNoTracking()
            .Select(baseline => new
            {
                baseline.ProjectId,
                baseline.BaselineNumber,
                baseline.HeadState,
                baseline.BranchName,
                baseline.HeadCommitSha,
                baseline.IsDirty,
                baseline.ObservedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var hostCapabilitySnapshots = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Computed once, from the one shared host observation, and handed to every project
        // result below — never recomputed or re-probed per project. Registering any number of
        // projects can never change this list's size or invocation count.
        var capabilities = HostCapabilityReadinessProjector.Project(hostCapabilitySnapshots, timeProvider.GetUtcNow());

        var results = new List<ProjectRunSummaryQueryResult>(projects.Count);

        foreach (var project in projects)
        {
            var mostRelevantRun = runs
                .Where(run => run.ProjectId == project.Id)
                .OrderBy(run => run.Lifecycle == RunLifecycle.Completed ? 1 : 0)
                .ThenByDescending(run => run.ExecutionNumber)
                .FirstOrDefault();

            // Greatest BaselineNumber, not latest ObservedAtUtc — null for a project with no
            // baseline at all (a legacy or otherwise never-validated row), rendered as
            // "not yet validated" rather than fabricating clean/branch data for it.
            var currentBaseline = baselines
                .Where(baseline => baseline.ProjectId == project.Id)
                .OrderByDescending(baseline => baseline.BaselineNumber)
                .FirstOrDefault();

            results.Add(
                new ProjectRunSummaryQueryResult(
                    project.Id,
                    project.Name,
                    project.CanonicalPath,
                    mostRelevantRun?.Id,
                    mostRelevantRun?.ExecutionNumber,
                    mostRelevantRun?.Lifecycle,
                    mostRelevantRun?.Stage,
                    capabilities,
                    currentBaseline?.HeadState,
                    currentBaseline?.BranchName,
                    currentBaseline?.HeadCommitSha,
                    currentBaseline?.IsDirty ?? false,
                    currentBaseline?.ObservedAtUtc));
        }

        return results;
    }
}
