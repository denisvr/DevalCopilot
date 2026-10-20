using DevalCopilot.Application.Data;

namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>
/// The advisory use of the global ceiling by the eligibility feeds of potentially mutating work (ADR-0058): a pending owner whose dispatch
/// the ceiling would refuse is not proposed to its supervisor, so the supervisor neither captures Git nor reaches the dispatch gate for it
/// on every poll. It is only a proposal filter; the single-use dispatch marker re-decides under its own write lock, and nothing here
/// terminalizes, cancels, retries or chooses between owners.
/// </summary>
internal static class ExecutionCapacityFeed
{
    public static async Task<IReadOnlyList<TCandidate>> WithoutKnownCapacityRefusalsAsync<TCandidate>(
        IDevalCopilotDbContext dbContext,
        IReadOnlyList<TCandidate> candidates,
        Func<TCandidate, ExecutionOwner> identify,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var kept = new List<TCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await ExecutionCapacityFence.CheckDispatchAsync(dbContext, identify(candidate), cancellationToken) is null)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }
}
