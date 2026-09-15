using System.Globalization;

namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// Computes the deterministic, never-reused branch name for a tool-owned workspace from a
/// project's identity and a reserved <see cref="Project.ReserveWorkspaceNumber"/> value alone —
/// pure string composition, no I/O, no filesystem or Git concept. See ADR-0008.
/// </summary>
public static class WorkspaceBranchNamePolicy
{
    public static string Compute(Guid projectId, int workspaceNumber)
    {
        if (workspaceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(workspaceNumber));
        }

        return $"devalcopilot/workspace/{projectId:N}/{workspaceNumber.ToString(CultureInfo.InvariantCulture)}";
    }
}
