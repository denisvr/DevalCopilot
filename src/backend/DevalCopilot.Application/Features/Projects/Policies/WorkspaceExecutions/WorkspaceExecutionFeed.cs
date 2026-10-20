using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;

/// <summary>
/// The advisory use of the exclusion by the Agent eligibility feeds (ADR-0057): a claimed, undispatched attempt whose workspace something
/// else owns is not proposed to its supervisor, so the supervisor neither captures Git nor reaches the dispatch gate for it on every poll.
/// It is only a proposal filter; the single-use dispatch marker re-decides under its own write lock, and nothing here terminalizes,
/// cancels or chooses between owners.
/// </summary>
internal static class WorkspaceExecutionFeed
{
    public static async Task<IReadOnlyList<TCandidate>> WithoutKnownWorkspaceConflictsAsync<TCandidate>(
        IDevalCopilotDbContext dbContext,
        IReadOnlyList<TCandidate> candidates,
        Func<TCandidate, (Guid RunId, Guid WorkspaceId, Guid AttemptId)> identify,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var kept = new List<TCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var (runId, workspaceId, attemptId) = identify(candidate);
            var projectId = await dbContext.Runs.AsNoTracking()
                .Where(run => run.Id == runId)
                .Select(run => (Guid?)run.ProjectId)
                .SingleOrDefaultAsync(cancellationToken);
            if (projectId is { } project
                && await WorkspaceExecutionFence.CheckAgentDispatchAsync(dbContext, project, workspaceId, attemptId, cancellationToken) is null)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }
}
