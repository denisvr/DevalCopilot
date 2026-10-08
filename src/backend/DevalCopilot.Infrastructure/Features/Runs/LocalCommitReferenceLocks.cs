using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// A bounded, read-only proof about the reference-lock namespace of one local-commit transaction (ADR-0029 R7): the lock of the owned
/// <c>refs/heads/&lt;branch&gt;</c> reference in the proven common Git directory and <c>HEAD.lock</c> in the proven linked-worktree
/// administrative directory. These are the two locks <c>git update-ref</c> takes for this transaction. The probe reads attributes of
/// at most a fixed number of fixed-shape paths, never opens, reads, creates, renames or deletes any of them, and never interprets
/// their bytes, process ids or ages as ownership. Everything that is not a positive observation of absence is unproven: a path that
/// cannot be represented safely, a redirected or non-directory ancestor, or any read failure other than a definite not-found.
/// </summary>
internal static class LocalCommitReferenceLocks
{
    private const int MaxBranchNameLength = 256;
    private const int MaxBranchComponents = 16;

    /// <param name="readAttributes">The one filesystem read the probe performs, replaceable so that an unreadable path can be
    /// produced deterministically; the default is <see cref="File.GetAttributes(string)"/>.</param>
    internal static LocalCommitReferenceLockState Probe(
        string commonDirectory, string administrativeDirectory, string branchName, Func<string, FileAttributes>? readAttributes = null)
    {
        var read = readAttributes ?? File.GetAttributes;
        var reference = ProbeReferenceLock(commonDirectory, branchName, read);
        var head = ProbeHeadLock(administrativeDirectory, read);
        if (reference == LocalCommitReferenceLockState.Present || head == LocalCommitReferenceLockState.Present)
        {
            return LocalCommitReferenceLockState.Present;
        }

        return reference == LocalCommitReferenceLockState.Clear && head == LocalCommitReferenceLockState.Clear
            ? LocalCommitReferenceLockState.Clear
            : LocalCommitReferenceLockState.Unproven;
    }

    private static LocalCommitReferenceLockState ProbeHeadLock(string administrativeDirectory, Func<string, FileAttributes> read)
    {
        if (!IsRootedDirectoryPath(administrativeDirectory))
        {
            return LocalCommitReferenceLockState.Unproven;
        }

        return ProbeUnder(administrativeDirectory, [], "HEAD.lock", read);
    }

    private static LocalCommitReferenceLockState ProbeReferenceLock(string commonDirectory, string branchName, Func<string, FileAttributes> read)
    {
        if (!IsRootedDirectoryPath(commonDirectory) || !TrySplitBranch(branchName, out var components))
        {
            return LocalCommitReferenceLockState.Unproven;
        }

        var directories = new List<string> { "refs", "heads" };
        directories.AddRange(components.Take(components.Count - 1));
        return ProbeUnder(commonDirectory, directories, components[^1] + ".lock", read);
    }

    /// <summary>Walks the fixed ancestor chain below <paramref name="root"/> without following any redirection. A definitely absent
    /// ancestor means the lock cannot exist; an ancestor that is a reparse point or not a directory proves nothing.</summary>
    private static LocalCommitReferenceLockState ProbeUnder(
        string root, IReadOnlyList<string> directories, string lockName, Func<string, FileAttributes> read)
    {
        var rootState = Attributes(root, read);
        if (rootState.Fault || rootState.Attributes is not { } rootAttributes || !IsPlainDirectory(rootAttributes))
        {
            return LocalCommitReferenceLockState.Unproven;
        }

        var current = root;
        foreach (var directory in directories)
        {
            current = Path.Combine(current, directory);
            var state = Attributes(current, read);
            if (state.Fault)
            {
                return LocalCommitReferenceLockState.Unproven;
            }

            if (state.Attributes is not { } attributes)
            {
                return LocalCommitReferenceLockState.Clear;
            }

            if (!IsPlainDirectory(attributes))
            {
                return LocalCommitReferenceLockState.Unproven;
            }
        }

        var lockState = Attributes(Path.Combine(current, lockName), read);
        if (lockState.Fault)
        {
            return LocalCommitReferenceLockState.Unproven;
        }

        // Whatever exists under the lock name, a file or even a directory, blocks Git from taking the lock.
        return lockState.Attributes is null ? LocalCommitReferenceLockState.Clear : LocalCommitReferenceLockState.Present;
    }

    private readonly record struct AttributeRead(FileAttributes? Attributes, bool Fault);

    private static AttributeRead Attributes(string path, Func<string, FileAttributes> read)
    {
        try
        {
            return new AttributeRead(read(path), false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new AttributeRead(null, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return new AttributeRead(null, true);
        }
    }

    private static bool IsPlainDirectory(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint);

    private static bool IsRootedDirectoryPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    /// <summary>The owned workspace branch is a generated, fixed-shape name. Anything outside a conservative allowlist, including
    /// a component Git itself forbids or that could name a different path, cannot be mapped to its lock path and is unproven.</summary>
    private static bool TrySplitBranch(string branchName, out List<string> components)
    {
        components = [];
        if (string.IsNullOrEmpty(branchName) || branchName.Length > MaxBranchNameLength)
        {
            return false;
        }

        var parts = branchName.Split('/');
        if (parts.Length > MaxBranchComponents)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || part[0] == '.' || part[^1] == '.' || part.Contains("..", StringComparison.Ordinal)
                || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                || !part.All(character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.'))
            {
                return false;
            }
        }

        components = [.. parts];
        return true;
    }
}
