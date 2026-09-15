using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Computes the deterministic, application-owned workspace path under
/// <c>%LocalAppData%\DevalCopilot\workspaces\&lt;ProjectId&gt;\&lt;WorkspaceNumber&gt;</c> —
/// mirroring <c>FilesystemArtifactStore</c>'s ownership of its own artifact root. Pure path
/// composition only; existence/collision checks belong to <see cref="IGitWorktreeAdapter"/>.
/// </summary>
public sealed class WorkspaceRootPathProvider : IWorkspaceRootPathProvider
{
    private readonly string _root;

    public WorkspaceRootPathProvider()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "workspaces"))
    {
    }

    /// <summary>Test-only: an isolated root so tests never write real workspaces to a
    /// developer's actual machine state.</summary>
    public WorkspaceRootPathProvider(string root)
    {
        _root = root;
    }

    public string ComputeWorkspacePath(Guid projectId, int workspaceNumber) =>
        Path.Combine(_root, projectId.ToString("N"), workspaceNumber.ToString());
}
