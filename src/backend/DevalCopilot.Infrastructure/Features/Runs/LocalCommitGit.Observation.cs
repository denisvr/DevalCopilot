using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

public sealed partial class LocalCommitGit
{
    private sealed record ControlledObservation(bool Proven, string? FingerprintSha256, bool Clean, string Reason);

    /// <summary>
    /// A local-commit-only, converter-free observation view. It never asks Git to refresh the real index: a bounded private copy
    /// is pinned through GIT_INDEX_FILE, the git-dir/work-tree are explicit, attributes come only from the immutable tree, and
    /// every tracked path that status/diff could consult is checked for a conversion authority first. The returned fingerprint
    /// intentionally uses the ordinary reader's existing byte-for-byte algorithm.
    /// </summary>
    private async Task<ControlledObservation> ObserveControlledAsync(
        string gitPath,
        string workspacePath,
        string commonDirectory,
        string treeSha,
        string realIndexPath,
        string? scratchDirectory,
        CancellationToken cancellationToken,
        byte[]? pinnedIndexBytes = null)
    {
        // The observation's private files live in a leaf this call creates and removes. A caller that already owns a scratch
        // leaf (the preparation) passes it and the observation leaf nests inside it; any other caller gets a fresh leaf of its own,
        // so no observation's cleanup can reach a sibling's files.
        var ownedScratch = scratchDirectory is null ? storage.ScratchDirectory(Guid.NewGuid()) : null;
        var controlledIndex = string.Empty;
        var controlledGitDirectory = string.Empty;
        if (!LocalCommitOperation.IsObjectId(treeSha)
            || !TryCopyBoundedIndex(realIndexPath, scratchDirectory ?? ownedScratch!, pinnedIndexBytes, out controlledIndex)
            || !TryCreateControlledGitDirectory(commonDirectory, treeSha, Path.GetDirectoryName(controlledIndex)!, out controlledGitDirectory))
        {
            DeleteObservationLeaf(controlledIndex, ownedScratch);
            return new ControlledObservation(false, null, false, "index_copy_unproven");
        }

        try
        {
            var environment = new Dictionary<string, string>
            {
                ["GIT_INDEX_FILE"] = controlledIndex,
                ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = Path.Combine(commonDirectory, "objects"),
            };
            var prefix = BuildPrivateObservationProfile(controlledGitDirectory, workspacePath);
            async Task<LocalCommitGitResult> ReadAsync(IReadOnlyList<string> arguments, byte[]? input = null) =>
                await runner.RunAsync(gitPath, workspacePath, [.. prefix, .. arguments], cancellationToken,
                    standardInput: input, extraEnvironment: environment, attributeSourceTree: treeSha);

            var tree = await ReadAsync(["ls-tree", "-r", "-z", treeSha]);
            var paths = ParseTreePaths(tree);
            if (paths is null || !await IsConversionFreeAsync(ReadAsync, paths))
            {
                return new ControlledObservation(false, null, false, "attributes_unproven");
            }

            var first = await ReadSnapshotAsync(ReadAsync, treeSha);
            if (first is null)
            {
                return new ControlledObservation(false, null, false, "snapshot_unproven");
            }

            var untracked = ParseUntracked(first.Value.Status);
            if (untracked is null)
            {
                return new ControlledObservation(false, null, false, "porcelain_unproven");
            }

            var hashes = new List<(string Path, string Hash)>();
            foreach (var path in untracked)
            {
                var hash = await ReadAsync(["hash-object", "--no-filters", "--", path]);
                var value = hash.Output.Trim();
                if (!hash.Succeeded || !LocalCommitOperation.IsObjectId(value))
                {
                    return new ControlledObservation(false, null, false, "untracked_hash_unproven");
                }

                hashes.Add((path, value));
            }

            var second = await ReadSnapshotAsync(ReadAsync, treeSha);
            if (second is null || first.Value != second.Value)
            {
                return new ControlledObservation(false, null, false, "snapshot_changed");
            }

            // A no-lock status may conservatively report a stat-cache mismatch without rereading or refreshing the real
            // index. Content equality is instead proven by the converter-free binary diff; untracked entries remain a
            // real source difference even when no tracked diff exists.
            var clean = second.Value.Diff.Length == 0 && untracked.Count == 0;
            var fingerprint = ComputeFingerprint(treeSha, second.Value.Status, second.Value.Diff, hashes);
            return new ControlledObservation(true, fingerprint, clean, "none");
        }
        finally
        {
            DeleteObservationLeaf(controlledIndex, ownedScratch);
        }
    }

    /// <summary>Removes the observation's own leaf (and the scratch directory it allocated for itself, if any); never an ancestor
    /// that a caller or another preparation may also use.</summary>
    private void DeleteObservationLeaf(string controlledIndex, string? ownedScratch)
    {
        if (controlledIndex.Length > 0)
        {
            try
            {
                var leaf = Path.GetDirectoryName(controlledIndex)!;
                if (Directory.Exists(leaf))
                {
                    Directory.Delete(leaf, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Inert scratch; removing the owning scratch leaf retries it.
            }
        }

        if (ownedScratch is not null)
        {
            storage.TryDeleteOwnedLeaf(ownedScratch);
        }
    }

    /// <summary>The complete, deliberately fixed Windows presentation profile.  It is kept as one production boundary so
    /// regression tests can make removal of either compatibility setting fail against an ordinary raw checkpoint rather than
    /// silently accepting a different observation.</summary>
    internal static IReadOnlyList<string> BuildPrivateObservationProfile(string controlledGitDirectory, string workspacePath) =>
    [
        "--git-dir=" + controlledGitDirectory.Replace('\\', '/'),
        "--work-tree=" + workspacePath.Replace('\\', '/'),
        "-c", "core.autocrlf=false",
        "-c", "core.eol=lf",
        // Preserve the ordinary checkpoint reader's byte presentation without importing mutable repository settings.
        // Placing quotePath last overrides the runner's general false value only in this isolated Windows view.
        "-c", "core.filemode=false",
        "-c", "core.quotePath=true",
    ];

    /// <summary>Creates the owned, fixed Git administrative view used only for the converter-free observation. It has no
    /// repository-local config, hooks or info/attributes from the live worktree; objects are read through an explicit alternate.
    /// The caller has already proved the common directory through ownership before invoking this boundary.</summary>
    private static bool TryCreateControlledGitDirectory(
        string commonDirectory, string head, string observationDirectory, out string controlledGitDirectory)
    {
        controlledGitDirectory = Path.Combine(observationDirectory, "git");
        try
        {
            var objects = Path.Combine(commonDirectory, "objects");
            if (!Directory.Exists(objects))
            {
                return false;
            }

            Directory.CreateDirectory(Path.Combine(controlledGitDirectory, "objects"));
            Directory.CreateDirectory(Path.Combine(controlledGitDirectory, "refs"));
            Directory.CreateDirectory(Path.Combine(controlledGitDirectory, "info"));
            File.WriteAllText(Path.Combine(controlledGitDirectory, "HEAD"), head + "\n", new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(controlledGitDirectory, "config"),
                "[core]\n\trepositoryformatversion = 0\n\tbare = false\n",
                new UTF8Encoding(false));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<(string Status, string Diff)?> ReadSnapshotAsync(
        Func<IReadOnlyList<string>, byte[]?, Task<LocalCommitGitResult>> readAsync,
        string expectedHead)
    {
        var head = await readAsync(["rev-parse", "--verify", "-q", "HEAD"], null);
        var status = await readAsync(["status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all"], null);
        var diff = await readAsync(["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", expectedHead], null);
        return head.Succeeded && status.Succeeded && diff.Succeeded
            && string.Equals(head.Output.Trim(), expectedHead, StringComparison.Ordinal)
            ? (status.Output, diff.Output)
            : null;
    }

    private static bool TryCopyBoundedIndex(string indexPath, string scratchDirectory, byte[]? pinnedIndexBytes, out string controlledIndex)
    {
        // Each immutable observation owns one scratch leaf. A completed Git child may briefly retain the preceding private
        // index on Windows; reusing its pathname would turn harmless delayed cleanup into an observation failure.
        controlledIndex = Path.Combine(scratchDirectory, "controlled-observation", Guid.NewGuid().ToString("N"), "index");
        try
        {
            if (pinnedIndexBytes is not null && pinnedIndexBytes.LongLength > WindowsIndexEffectHandles.MaximumIndexBytes)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(controlledIndex)!);
            if (pinnedIndexBytes is not null)
            {
                File.WriteAllBytes(controlledIndex, pinnedIndexBytes);
                return new FileInfo(controlledIndex).Length == pinnedIndexBytes.Length;
            }

            var source = new FileInfo(indexPath);
            if (!source.Exists || source.Length is < 0 or > WindowsIndexEffectHandles.MaximumIndexBytes)
            {
                return false;
            }

            File.Copy(indexPath, controlledIndex, overwrite: false);
            return new FileInfo(controlledIndex).Length == source.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string>? ParseTreePaths(LocalCommitGitResult tree)
    {
        if (!tree.Succeeded)
        {
            return null;
        }

        var paths = new List<string>();
        foreach (var entry in tree.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\t');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                return null;
            }

            paths.Add(entry[(separator + 1)..]);
        }

        return paths;
    }

    private static async Task<bool> IsConversionFreeAsync(
        Func<IReadOnlyList<string>, byte[]?, Task<LocalCommitGitResult>> readAsync,
        IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        var input = Encoding.UTF8.GetBytes(string.Concat(paths.Select(path => path + "\0")));
        var attributes = await readAsync(["check-attr", "-z", "-a", "--stdin"], input);
        if (!attributes.Succeeded)
        {
            return false;
        }

        var values = attributes.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return values.Length % 3 == 0 && values.Where((_, index) => index % 3 == 1)
            .Zip(values.Where((_, index) => index % 3 == 2), (name, value) => (name, value))
            .All(pair => pair.name is not ("filter" or "ident" or "working-tree-encoding") || pair.value == "unspecified");
    }

    private static IReadOnlyList<string>? ParseUntracked(string porcelain)
    {
        var paths = new List<string>();
        foreach (var entry in porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.Length < 4 || entry[2] != ' ')
            {
                return null;
            }

            if (entry[..2] == "??")
            {
                paths.Add(entry[3..]);
            }
        }

        return paths;
    }

    private static string ComputeFingerprint(
        string head,
        string status,
        string diff,
        IReadOnlyCollection<(string Path, string Hash)> untracked)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprint(hash, "head", head);
        AppendFingerprint(hash, "status", status);
        AppendFingerprint(hash, "diff", diff);
        foreach (var (path, value) in untracked.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            AppendFingerprint(hash, "untracked-path", path);
            AppendFingerprint(hash, "untracked-hash", value);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFingerprint(IncrementalHash hash, string label, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(label));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
