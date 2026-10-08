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
    private const string PreparedIndexFileName = "prepared.index";

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

    // Layout. A preparation invocation owns one scratch leaf (work/<preparation>) and one artifact leaf (operations/<preparation>),
    // both named by a fresh identifier that is independent of the caller's operation identifier: several preparations of the same
    // operation (identical or competing requests run before database admission) never share a mutable path, and the admitted one is
    // recorded by its exact relative artifact path. The operation-named layout below is the legacy form that earlier operations
    // recorded; those paths stay resolvable and cleanable, but no new preparation is ever placed in it.

    /// <summary>The scratch leaf of one preparation invocation; only that invocation creates, uses and removes it.</summary>
    public string ScratchDirectory(Guid preparationId) => Path.Combine(_root, WorkFolder, preparationId.ToString("N"));

    /// <summary>The prepared-artifact leaf of one preparation invocation.</summary>
    public string ArtifactDirectory(Guid preparationId) => Path.Combine(_root, OperationsFolder, preparationId.ToString("N"));

    /// <summary>The relative path the operation records for the artifact of one preparation invocation.</summary>
    public string PreparedIndexRelativePath(Guid preparationId) =>
        Path.Combine(OperationsFolder, preparationId.ToString("N"), PreparedIndexFileName);

    /// <summary>The operation-named artifact directory recorded before preparations had their own leaves (legacy layout).</summary>
    public string LegacyOperationDirectory(Guid operationId) => Path.Combine(_root, OperationsFolder, operationId.ToString("N"));

    /// <summary>The operation-named relative artifact path recorded before preparations had their own leaves (legacy layout).</summary>
    public string LegacyPreparedIndexRelativePath(Guid operationId) =>
        Path.Combine(OperationsFolder, operationId.ToString("N"), PreparedIndexFileName);

    /// <summary>Removes one leaf the caller created (a scratch or artifact leaf of its own preparation or observation). Only a direct
    /// child of the work or operations folder inside the owned root qualifies; the root, those folders and anything else are
    /// refused, and a failure to delete leaves inert bytes behind.</summary>
    public bool TryDeleteOwnedLeaf(string leafPath)
    {
        var full = Path.GetFullPath(leafPath);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !IsOwnedFolder(parent))
        {
            return false;
        }

        return TryDelete(() =>
        {
            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        });
    }

    /// <summary>Removes the artifact an operation recorded, and nothing else: the one file and then its directory only when that
    /// directory is empty. The directory is never removed recursively, so a sibling preparation (or anything else a legacy
    /// operation-named directory holds) is left alone. A recorded path that does not resolve to a file strictly inside a leaf of
    /// the operations folder is ignored.</summary>
    public bool TryRemoveRecordedArtifact(string relativePath)
    {
        var file = ResolveArtifact(relativePath);
        var leaf = file is null ? null : Path.GetDirectoryName(file);
        var parent = leaf is null ? null : Path.GetDirectoryName(leaf);
        if (file is null || leaf is null || parent is null || !string.Equals(Path.GetFileName(parent), OperationsFolder, StringComparison.OrdinalIgnoreCase)
            || !IsOwnedFolder(parent))
        {
            return false;
        }

        return TryDelete(() =>
        {
            File.Delete(file);
            if (Directory.Exists(leaf) && !Directory.EnumerateFileSystemEntries(leaf).Any())
            {
                Directory.Delete(leaf, recursive: false);
            }
        });
    }

    private bool IsOwnedFolder(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(full, Path.Combine(_root, WorkFolder), StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, Path.Combine(_root, OperationsFolder), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDelete(Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Scratch and artifacts are inert without a database row that names them; a later cleanup may retry.
            return false;
        }
    }

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
