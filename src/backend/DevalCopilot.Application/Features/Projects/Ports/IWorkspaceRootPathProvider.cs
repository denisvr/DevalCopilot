namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// Computes the deterministic, application-owned absolute path for a tool-owned workspace —
/// the one place this feature depends on an environment special-folder location, mirroring
/// <c>FilesystemArtifactStore</c>'s ownership of its own artifact root. Pure path composition:
/// implementations must not touch the filesystem here (existence/collision checks belong to
/// <see cref="IGitWorktreeAdapter.CreateAsync"/>, which alone decides whether the path is safe
/// to use).
/// </summary>
public interface IWorkspaceRootPathProvider
{
    string ComputeWorkspacePath(Guid projectId, int workspaceNumber);
}
