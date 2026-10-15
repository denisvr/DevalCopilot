using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;

/// <summary>The admission rules of the repository-permission observation. A viewer permission is exactly one of the six case-sensitive
/// literals of GitHub's <c>RepositoryPermission</c> enum (ADMIN, MAINTAIN, READ, TRIAGE, TRIAGE_PLUS, WRITE) or explicitly absent; nothing
/// is normalized, translated, ranked or defaulted and an unknown value is never admitted. Pure.</summary>
public static class GitHubRepositoryPermissionPolicy
{
    public static bool IsViewerPermission(string? value) =>
        value is "READ" or "TRIAGE" or "TRIAGE_PLUS" or "WRITE" or "MAINTAIN" or "ADMIN";

    /// <summary>The whole port result is validated before anything is compared: a conservative opaque ID, supported names equal to the
    /// recorded scope ignoring only ASCII case, and a permission that is absent or one admitted literal. A malformed answer is never
    /// partially admitted as an identity change.</summary>
    internal static bool IsAcceptedProbe(GitHubRepositoryPermissionProbe? probe, GitHubObservationTargetView target) =>
        probe is not null
        && GitHubRepositoryNodeIdPolicy.IsNodeId(probe.NodeId)
        && GitHubRepositoryNamePolicy.IsOwner(probe.Owner)
        && GitHubRepositoryNamePolicy.IsRepository(probe.Repository)
        && string.Equals(probe.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(probe.Repository, target.Repository, StringComparison.OrdinalIgnoreCase)
        && (probe.ViewerPermission is null || IsViewerPermission(probe.ViewerPermission));
}
