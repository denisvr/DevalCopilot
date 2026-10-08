using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalCommit;

/// <summary>
/// The expected worktree tip of a workspace after host commits (ADR-0029): the unique coherent chain of completed recorded
/// parent-to-commit edges rooted at the immutable source commit. Never a timestamp, a UUID order or an arbitrary descendant; a
/// broken, forked or cyclic chain has no tip, so further delivery and reconciliation fail closed.
/// </summary>
public static class LocalCommitHeadChain
{
    /// <returns>The tip, or null when the completed edges do not form one unbroken path from the source commit.</returns>
    public static string? ResolveTip(string sourceCommitSha, IReadOnlyCollection<LocalCommitOperation> completedOperations)
    {
        var edges = completedOperations.Where(operation => operation.Status == LocalCommitStatus.Completed).ToList();
        var byParent = edges.GroupBy(operation => operation.ParentCommitSha, StringComparer.Ordinal).ToDictionary(
            group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var children = edges.Select(operation => operation.CommitSha).ToHashSet(StringComparer.Ordinal);
        if (children.Count != edges.Count || edges.Any(operation => operation.CommitSha == operation.ParentCommitSha))
        {
            return null;
        }

        var tip = sourceCommitSha;
        var visited = new HashSet<string>(StringComparer.Ordinal) { tip };
        var used = 0;
        while (byParent.TryGetValue(tip, out var next))
        {
            if (next.Count != 1 || !visited.Add(next[0].CommitSha))
            {
                return null;
            }

            tip = next[0].CommitSha;
            used++;
        }

        return used == edges.Count ? tip : null;
    }
}
