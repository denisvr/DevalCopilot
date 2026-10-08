using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The host-owned directories of the explicit local commit (ADR-0029): the prepared-index artifacts, the isolated-index scratch
/// space, the proven-empty hooks directory and the empty global-configuration file. All of them sit in one root beside the
/// workspace root, derived from <see cref="IWorkspaceRootPathProvider"/> so a test composition that isolates the workspace root
/// isolates this one too. Nothing here is ever inside a project or worktree.
/// </summary>
public sealed class LocalCommitStorage
{
    private const string OperationsFolder = "operations";
    private const string WorkFolder = "work";
    private const string HooksFolder = "hooks-empty";
    private const string EmptyConfigFile = "empty.gitconfig";

    private readonly string _root;

    public LocalCommitStorage(IWorkspaceRootPathProvider workspaceRoots)
    {
        var sample = Path.GetFullPath(workspaceRoots.ComputeWorkspacePath(Guid.Empty, 1));
        var workspaceRoot = Path.GetDirectoryName(Path.GetDirectoryName(sample))!;
        _root = Path.Combine(Path.GetDirectoryName(workspaceRoot)!, "local-commit");
    }

    public LocalCommitStorage(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string Root => _root;

    public string HooksDirectory => Path.Combine(_root, HooksFolder);

    public string EmptyConfigPath => Path.Combine(_root, EmptyConfigFile);

    public string OperationDirectory(Guid operationId) => Path.Combine(_root, OperationsFolder, operationId.ToString("N"));

    public string WorkDirectory(Guid operationId) => Path.Combine(_root, WorkFolder, operationId.ToString("N"));

    public string PreparedIndexRelativePath(Guid operationId) =>
        Path.Combine(OperationsFolder, operationId.ToString("N"), "prepared.index");

    /// <summary>Resolves a persisted relative artifact path back under the root; anything that escapes the root is refused.</summary>
    public string? ResolveArtifact(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Creates the owned directories and files on first use and proves the hooks directory is empty. False when anything
    /// is present in it, in which case no Git command may run.</summary>
    public bool TryEnsureHooksDirectoryEmpty()
    {
        try
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(HooksDirectory);
            if (!File.Exists(EmptyConfigPath))
            {
                File.WriteAllBytes(EmptyConfigPath, []);
            }

            return !Directory.EnumerateFileSystemEntries(HooksDirectory).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
