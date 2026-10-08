using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;

namespace DevalCopilot.Infrastructure.Features.Runs;

public sealed partial class LocalCommitGit
{
    private sealed record ParentEntry(string Mode, string Type, string ObjectId);

    private sealed record Change(string Path, bool IsDeletion, bool IsNew, string Mode, byte[]? Bytes, string? BlobId);

    public Task<LocalCommitPreparationResult> PrepareAsync(
        LocalCommitPreparationRequest request, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? PrepareOnWindowsAsync(request, cancellationToken)
            : Task.FromResult(Refuse(LocalCommitPreparationOutcome.HostUnsupported));

    private static LocalCommitPreparationResult Refuse(LocalCommitPreparationOutcome outcome) => new(outcome, null);

    [SupportedOSPlatform("windows")]
    private async Task<LocalCommitPreparationResult> PrepareOnWindowsAsync(
        LocalCommitPreparationRequest request, CancellationToken cancellationToken)
    {
        // Preparation runs before database admission, so several preparations of one operation (identical or competing requests)
        // can be in flight at once. Each invocation therefore owns a fresh scratch leaf and a fresh artifact leaf named by its own
        // identifier, never by the operation identifier, and it cleans up only those two leaves.
        var preparationId = Guid.NewGuid();
        var workDirectory = storage.ScratchDirectory(preparationId);
        var artifactDirectory = storage.ArtifactDirectory(preparationId);
        var sources = new List<TrackedFileSourceReader.Source>();
        var prepared = false;
        try
        {
            var result = await PrepareCoreAsync(request, preparationId, workDirectory, artifactDirectory, sources, cancellationToken);
            prepared = result.Outcome == LocalCommitPreparationOutcome.Prepared;
            return result;
        }
        finally
        {
            foreach (var source in sources)
            {
                source.Dispose();
            }

            storage.TryDeleteOwnedLeaf(workDirectory);
            if (!prepared)
            {
                storage.TryDeleteOwnedLeaf(artifactDirectory);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task<LocalCommitPreparationResult> PrepareCoreAsync(
        LocalCommitPreparationRequest request,
        Guid preparationId,
        string workDirectory,
        string artifactDirectory,
        List<TrackedFileSourceReader.Source> sources,
        CancellationToken cancellationToken)
    {
        var gitPath = ResolveGit();
        if (gitPath is null)
        {
            return Refuse(LocalCommitPreparationOutcome.GitUnavailable);
        }

        if (!storage.TryEnsureHooksDirectoryEmpty())
        {
            return Refuse(LocalCommitPreparationOutcome.HooksDirectoryNotEmpty);
        }

        var workspacePath = request.WorkspacePath;
        var changes = request.ChangedPaths;
        if (changes.Count == 0)
        {
            return Refuse(LocalCommitPreparationOutcome.UnsupportedChange);
        }

        if (changes.Count > LocalCommitOperation.MaximumChangedPaths)
        {
            return Refuse(LocalCommitPreparationOutcome.TooManyPaths);
        }

        var shape = ValidateShape(changes);
        if (shape is not null)
        {
            return Refuse(shape.Value);
        }

        var ownership = await ProveOwnershipAsync(request.MainRepositoryPath, workspacePath, request.Ownership, cancellationToken);
        if (!ownership.Proven || ownership.AdministrativeDirectory is not { } administrativeDirectory
            || ownership.CommonDirectory is not { } commonDirectory)
        {
            return Refuse(LocalCommitPreparationOutcome.OwnershipNotProven);
        }

        var binding = await VerifyBindingAsync(gitPath, workspacePath, request, administrativeDirectory, cancellationToken);
        if (binding is not null)
        {
            return Refuse(binding.Value);
        }

        var configuration = await VerifyConfigurationAsync(
            gitPath, workspacePath, commonDirectory, administrativeDirectory, cancellationToken);
        if (configuration is not null)
        {
            return Refuse(configuration.Value);
        }

        var indexPath = Path.Combine(administrativeDirectory, "index");
        var preimageSha256 = IndexSha256(indexPath);
        if (preimageSha256 is null)
        {
            return Refuse(LocalCommitPreparationOutcome.RepositoryNotClean);
        }

        var cleanliness = await VerifyIndexMatchesParentAsync(gitPath, workspacePath, changes, cancellationToken);
        if (cleanliness is not null)
        {
            return Refuse(cleanliness.Value);
        }

        var parentEntries = await ReadParentEntriesAsync(gitPath, workspacePath, changes, cancellationToken);
        if (parentEntries is null)
        {
            return Refuse(LocalCommitPreparationOutcome.GitFailed);
        }

        var rootFinalPath = WindowsFinalPathResolver.TryResolveDirectoryFinalPath(workspacePath)?.TrimEnd('\\');
        if (rootFinalPath is null)
        {
            return Refuse(LocalCommitPreparationOutcome.UnsafeSource);
        }

        var admission = AdmitSources(workspacePath, rootFinalPath, changes, parentEntries, sources, out var plan, out var totalBytes);
        if (admission is not null)
        {
            return Refuse(admission.Value);
        }

        Directory.CreateDirectory(workDirectory);
        var isolatedIndex = Path.Combine(workDirectory, "index");
        var indexEnvironment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = isolatedIndex };

        var tree = await BuildTreeAsync(gitPath, workspacePath, request.ExpectedParentCommitSha, plan, indexEnvironment, cancellationToken);
        if (tree.Outcome is not null || tree.TreeSha is null)
        {
            return Refuse(tree.Outcome ?? LocalCommitPreparationOutcome.GitFailed);
        }

        var treeSha = tree.TreeSha;
        var verification = await VerifyTreeDifferenceAsync(
            gitPath, workspacePath, request.ExpectedParentCommitSha, treeSha, plan, parentEntries, cancellationToken);
        if (verification is not null)
        {
            return Refuse(verification.Value);
        }

        var conversion = await VerifyAttributesAndConversionAsync(gitPath, workspacePath, treeSha, plan, cancellationToken);
        if (conversion is not null)
        {
            return Refuse(conversion.Value);
        }

        // Never use the general workspace reader here: status is itself an execution boundary. The controlled observer pins a
        // copy of the index, uses the immutable parent attributes, rejects every conversion authority, and brackets the view.
        var observation = await ObserveControlledAsync(
            gitPath, workspacePath, commonDirectory, request.ExpectedParentCommitSha, indexPath,
            workDirectory, cancellationToken);
        if (!observation.Proven)
        {
            return Refuse(LocalCommitPreparationOutcome.GitFailed);
        }

        if (!string.Equals(observation.FingerprintSha256, request.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            return Refuse(LocalCommitPreparationOutcome.CheckpointNotCurrent);
        }

        var stillProven = ReverifySources(workspacePath, rootFinalPath, plan, sources);
        if (stillProven is not null)
        {
            return Refuse(stillProven.Value);
        }

        var identity = await ReadIdentityAsync(gitPath, workspacePath, cancellationToken);
        if (identity is null)
        {
            return Refuse(LocalCommitPreparationOutcome.IdentityUnavailable);
        }

        var unixSeconds = request.NowUtc.ToUnixTimeSeconds();
        var message = LocalCommitMessagePolicy.BuildCommitMessage(request.NormalizedMessage, request.OperationId);
        var commit = await CreateCommitAsync(
            gitPath, workspacePath, treeSha, request.ExpectedParentCommitSha, identity.Value, unixSeconds, message, cancellationToken);
        if (commit is null)
        {
            return Refuse(LocalCommitPreparationOutcome.GitFailed);
        }

        Directory.CreateDirectory(artifactDirectory);
        var artifactRelativePath = storage.PreparedIndexRelativePath(preparationId);
        var artifactPath = storage.ResolveArtifact(artifactRelativePath);
        if (artifactPath is null)
        {
            return Refuse(LocalCommitPreparationOutcome.GitFailed);
        }

        // The index that will be promoted keeps the real index's stat data for every unchanged entry, so that a later status does not
        // have to re-hash (and therefore re-filter) files this commit never touched. It is built from a byte-exact copy of the
        // preimage, which equals the parent tree, and must write exactly the tree the isolated index produced.
        var promotion = await BuildPromotionIndexAsync(
            gitPath, workspacePath, indexPath, preimageSha256, Path.Combine(workDirectory, "promotion.index"), plan, treeSha, cancellationToken);
        if (promotion is not null)
        {
            return Refuse(promotion.Value);
        }

        // Exclusive creation: the artifact leaf belongs to this invocation alone, so an existing file is never replaced.
        try
        {
            File.Copy(Path.Combine(workDirectory, "promotion.index"), artifactPath, overwrite: false);
        }
        catch (IOException)
        {
            return Refuse(LocalCommitPreparationOutcome.GitFailed);
        }

        var preparedSha256 = FileSha256(artifactPath);
        if (preparedSha256 is null || !string.Equals(IndexSha256(indexPath), preimageSha256, StringComparison.Ordinal))
        {
            return Refuse(LocalCommitPreparationOutcome.RepositoryNotClean);
        }

        return new LocalCommitPreparationResult(
            LocalCommitPreparationOutcome.Prepared,
            new LocalCommitPreparedFacts(
                treeSha,
                commit,
                identity.Value.Name,
                identity.Value.Email,
                unixSeconds,
                preimageSha256,
                preparedSha256,
                artifactRelativePath,
                plan.Count,
                totalBytes));
    }

    /// <summary>Plain, bounded, collision-free repository-relative spellings and the only three porcelain shapes that mean an
    /// unstaged modification, an unstaged deletion or a new untracked file. Anything else refuses the whole commit.</summary>
    [SupportedOSPlatform("windows")]
    private static LocalCommitPreparationOutcome? ValidateShape(IReadOnlyList<LocalCommitChangedPath> changes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            if (!UntrackedFilePreviewReader.IsPlainRelativePath(change.Path) || change.Path.Length > 4096
                || change.Path.Split('/').Any(IsUnsupportedSegment) || !seen.Add(change.Path))
            {
                return LocalCommitPreparationOutcome.UnsupportedPath;
            }

            var shape = (change.IndexStatus, change.WorkTreeStatus);
            if (shape is not ((" ", "M") or (" ", "D") or ("?", "?")))
            {
                return LocalCommitPreparationOutcome.UnsupportedChange;
            }
        }

        return null;
    }

    private static bool IsUnsupportedSegment(string segment) =>
        segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || segment.EndsWith('.') || segment.EndsWith(' ')
        || segment.Any(character => character < 0x20 || "<>\"|?*".Contains(character))
        || segment.Contains('~');

    private async Task<LocalCommitPreparationOutcome?> VerifyBindingAsync(
        string gitPath,
        string workspacePath,
        LocalCommitPreparationRequest request,
        string administrativeDirectory,
        CancellationToken cancellationToken)
    {
        var bound = await HeadBoundToBranchAsync(gitPath, workspacePath, request.BranchName, cancellationToken, administrativeDirectory);
        var head = await ReadValueAsync(
            gitPath, workspacePath, ["rev-parse", "--verify", "-q", "HEAD"], cancellationToken, administrativeDirectory);
        var tip = await ReadBranchTipAsync(gitPath, workspacePath, request.BranchName, cancellationToken, administrativeDirectory);
        if (bound is null || !head.Ok || !tip.Ok)
        {
            return LocalCommitPreparationOutcome.GitFailed;
        }

        return bound == true
            && string.Equals(head.Value, request.ExpectedParentCommitSha, StringComparison.Ordinal)
            && string.Equals(tip.Value, request.ExpectedParentCommitSha, StringComparison.Ordinal)
                ? null
                : LocalCommitPreparationOutcome.ParentMismatch;
    }

    private async Task<LocalCommitPreparationOutcome?> VerifyConfigurationAsync(
        string gitPath, string workspacePath, string commonDirectory, string administrativeDirectory, CancellationToken cancellationToken)
    {
        foreach (var key in new[] { "core.sparseCheckout", "index.sparse", "core.splitIndex" })
        {
            var setting = await runner.RunAsync(gitPath, workspacePath, ["config", "--bool", "--get", key], cancellationToken);
            if (setting.Outcome != LocalCommitGitOutcome.Exited || setting.ExitCode > 1)
            {
                return LocalCommitPreparationOutcome.GitFailed;
            }

            if (setting.ExitCode == 0 && setting.Output.Trim() == "true")
            {
                return LocalCommitPreparationOutcome.ConfigurationUnsupported;
            }
        }

        if (File.Exists(Path.Combine(commonDirectory, "info", "attributes")))
        {
            return LocalCommitPreparationOutcome.AttributeSourceUnrepresented;
        }

        return File.Exists(Path.Combine(administrativeDirectory, "index.lock"))
            ? LocalCommitPreparationOutcome.RepositoryNotClean
            : null;
    }

    /// <summary>The real index must initially equal the parent: no staged change, nothing unmerged, and no path of this commit
    /// carrying a skip-worktree or assume-unchanged flag.</summary>
    private async Task<LocalCommitPreparationOutcome?> VerifyIndexMatchesParentAsync(
        string gitPath, string workspacePath, IReadOnlyList<LocalCommitChangedPath> changes, CancellationToken cancellationToken)
    {
        var staged = await runner.RunAsync(gitPath, workspacePath, ["diff-index", "--cached", "--raw", "-z", "HEAD"], cancellationToken);
        var unmerged = await runner.RunAsync(gitPath, workspacePath, ["ls-files", "-u", "-z"], cancellationToken);
        if (!staged.Succeeded || !unmerged.Succeeded)
        {
            return LocalCommitPreparationOutcome.GitFailed;
        }

        if (staged.Output.Length != 0 || unmerged.Output.Length != 0)
        {
            return LocalCommitPreparationOutcome.RepositoryNotClean;
        }

        var tracked = changes.Where(change => change.WorkTreeStatus != "?").Select(change => change.Path).ToList();
        foreach (var chunk in Chunks(tracked))
        {
            var flags = await runner.RunAsync(
                gitPath, workspacePath, ["ls-files", "-v", "-z", "--", .. chunk], cancellationToken);
            if (!flags.Succeeded)
            {
                return LocalCommitPreparationOutcome.GitFailed;
            }

            if (flags.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(record => record.Length < 2 || record[0] != 'H'))
            {
                return LocalCommitPreparationOutcome.UnsupportedChange;
            }
        }

        return null;
    }

    private async Task<Dictionary<string, ParentEntry>?> ReadParentEntriesAsync(
        string gitPath, string workspacePath, IReadOnlyList<LocalCommitChangedPath> changes, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, ParentEntry>(StringComparer.Ordinal);
        foreach (var chunk in Chunks(changes.Select(change => change.Path).ToList()))
        {
            var listing = await runner.RunAsync(gitPath, workspacePath, ["ls-tree", "-z", "HEAD", "--", .. chunk], cancellationToken);
            if (!listing.Succeeded)
            {
                return null;
            }

            foreach (var record in listing.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = record.IndexOf('\t');
                var fields = tab > 0 ? record[..tab].Split(' ') : [];
                if (fields.Length != 3)
                {
                    return null;
                }

                entries[record[(tab + 1)..]] = new ParentEntry(fields[0], fields[1], fields[2]);
            }
        }

        return entries;
    }

    /// <summary>Reads every added or modified file through a held, physically proven handle and proves every deletion absent.</summary>
    [SupportedOSPlatform("windows")]
    private static LocalCommitPreparationOutcome? AdmitSources(
        string workspacePath,
        string rootFinalPath,
        IReadOnlyList<LocalCommitChangedPath> changes,
        Dictionary<string, ParentEntry> parentEntries,
        List<TrackedFileSourceReader.Source> sources,
        out List<Change> plan,
        out long totalBytes)
    {
        plan = [];
        totalBytes = 0;
        foreach (var change in changes.OrderBy(candidate => candidate.Path, StringComparer.Ordinal))
        {
            var isNew = change.WorkTreeStatus == "?";
            parentEntries.TryGetValue(change.Path, out var parent);
            if (isNew == (parent is not null))
            {
                return LocalCommitPreparationOutcome.UnsupportedChange;
            }

            if (!isNew && (parent!.Type != "blob" || parent.Mode is not ("100644" or "100755")))
            {
                return LocalCommitPreparationOutcome.UnsupportedChange;
            }

            if (change.WorkTreeStatus == "D")
            {
                if (TrackedFileSourceReader.ProveAbsent(workspacePath, rootFinalPath, change.Path) != TrackedFileSourceReader.Absence.Proven)
                {
                    return LocalCommitPreparationOutcome.UnsafeSource;
                }

                plan.Add(new Change(change.Path, true, false, parent!.Mode, null, null));
                continue;
            }

            var source = TrackedFileSourceReader.Acquire(workspacePath, rootFinalPath, change.Path, WindowsHandleFileFacts.TryGet);
            switch (source.Kind)
            {
                case TrackedFileSourceReader.SourceKind.Proven:
                    sources.Add(source);
                    break;
                case TrackedFileSourceReader.SourceKind.Changed or TrackedFileSourceReader.SourceKind.NotFound:
                    source.Dispose();
                    return LocalCommitPreparationOutcome.SourceChanged;
                default:
                    source.Dispose();
                    return source.Omission == GitWorkspaceTrackedOmission.TooLarge
                        ? LocalCommitPreparationOutcome.SourceTooLarge
                        : LocalCommitPreparationOutcome.UnsafeSource;
            }

            var bytes = source.Bytes!;
            totalBytes += bytes.Length;
            if (bytes.Length > LocalCommitOperation.MaximumBytesPerFile)
            {
                return LocalCommitPreparationOutcome.SourceTooLarge;
            }

            if (totalBytes > LocalCommitOperation.MaximumTotalBytes)
            {
                return LocalCommitPreparationOutcome.TotalTooLarge;
            }

            plan.Add(new Change(change.Path, false, isNew, isNew ? "100644" : parent!.Mode, bytes, null));
        }

        return null;
    }

    /// <summary>After the independent fingerprint observation the same handles are read again and proven once more, so the bytes
    /// that will be committed are the bytes the fingerprint saw; each deletion is proven absent again.</summary>
    [SupportedOSPlatform("windows")]
    private static LocalCommitPreparationOutcome? ReverifySources(
        string workspacePath, string rootFinalPath, List<Change> plan, List<TrackedFileSourceReader.Source> sources)
    {
        var readable = plan.Where(change => !change.IsDeletion).ToList();
        for (var index = 0; index < sources.Count; index++)
        {
            var refused = TrackedFileSourceReader.Verify(sources[index], rootFinalPath, readable[index].Path, WindowsHandleFileFacts.TryGet);
            if (refused is not null)
            {
                var outcome = refused.Kind == TrackedFileSourceReader.SourceKind.Changed
                    ? LocalCommitPreparationOutcome.SourceChanged
                    : LocalCommitPreparationOutcome.UnsafeSource;
                refused.Dispose();
                return outcome;
            }
        }

        return plan.Where(change => change.IsDeletion).Any(change =>
            TrackedFileSourceReader.ProveAbsent(workspacePath, rootFinalPath, change.Path) != TrackedFileSourceReader.Absence.Proven)
            ? LocalCommitPreparationOutcome.SourceChanged
            : null;
    }

    private async Task<LocalCommitPreparationOutcome?> BuildPromotionIndexAsync(
        string gitPath,
        string workspacePath,
        string realIndexPath,
        string preimageSha256,
        string promotionIndexPath,
        List<Change> plan,
        string expectedTreeSha,
        CancellationToken cancellationToken)
    {
        try
        {
            using (var source = new FileStream(realIndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = new FileStream(promotionIndexPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LocalCommitPreparationOutcome.RepositoryNotClean;
        }

        if (!string.Equals(FileSha256(promotionIndexPath), preimageSha256, StringComparison.Ordinal))
        {
            return LocalCommitPreparationOutcome.RepositoryNotClean;
        }

        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = promotionIndexPath };
        var updated = await runner.RunAsync(
            gitPath, workspacePath, ["update-index", "--add", "--replace", "-z", "--index-info"], cancellationToken,
            standardInput: Encoding.UTF8.GetBytes(BuildIndexInfo(plan)), extraEnvironment: environment, mutation: true);
        var tree = await runner.RunAsync(
            gitPath, workspacePath, ["write-tree"], cancellationToken, extraEnvironment: environment, mutation: true);
        return updated.Succeeded && tree.Succeeded && string.Equals(tree.Output.Trim(), expectedTreeSha, StringComparison.Ordinal)
            ? null
            : LocalCommitPreparationOutcome.GitFailed;
    }

    private static string BuildIndexInfo(List<Change> plan)
    {
        var indexInfo = new StringBuilder();
        foreach (var change in plan.Where(change => change.IsDeletion))
        {
            indexInfo.Append("0 ").Append(ZeroObjectId).Append('\t').Append(change.Path).Append('\0');
        }

        foreach (var change in plan.Where(change => !change.IsDeletion))
        {
            indexInfo.Append(change.Mode).Append(' ').Append(change.BlobId).Append('\t').Append(change.Path).Append('\0');
        }

        return indexInfo.ToString();
    }

    private sealed record TreeBuild(LocalCommitPreparationOutcome? Outcome, string? TreeSha);

    /// <summary>The isolated index starts from the exact parent tree; raw blobs go in through standard input; explicit entries and
    /// deletions are applied; the tree is written. The real index is never named.</summary>
    [SupportedOSPlatform("windows")]
    private async Task<TreeBuild> BuildTreeAsync(
        string gitPath,
        string workspacePath,
        string parentSha,
        List<Change> plan,
        IReadOnlyDictionary<string, string> indexEnvironment,
        CancellationToken cancellationToken)
    {
        var read = await runner.RunAsync(
            gitPath, workspacePath, ["read-tree", parentSha], cancellationToken, extraEnvironment: indexEnvironment, mutation: true);
        if (!read.Succeeded)
        {
            return new TreeBuild(LocalCommitPreparationOutcome.GitFailed, null);
        }

        var entries = new List<Change>(plan.Count);
        foreach (var change in plan)
        {
            if (change.IsDeletion)
            {
                entries.Add(change);
                continue;
            }

            var written = await runner.RunAsync(
                gitPath, workspacePath, ["hash-object", "-w", "--no-filters", "--stdin"], cancellationToken,
                standardInput: change.Bytes, extraEnvironment: indexEnvironment, mutation: true);
            var blobId = written.Output.Trim();
            if (!written.Succeeded || !string.Equals(blobId, UntrackedFilePreviewReader.GitBlobSha1(change.Bytes!), StringComparison.Ordinal))
            {
                return new TreeBuild(LocalCommitPreparationOutcome.GitFailed, null);
            }

            entries.Add(change with { BlobId = blobId });
        }

        plan.Clear();
        plan.AddRange(entries);

        var updated = await runner.RunAsync(
            gitPath, workspacePath, ["update-index", "--add", "--replace", "-z", "--index-info"], cancellationToken,
            standardInput: Encoding.UTF8.GetBytes(BuildIndexInfo(plan)), extraEnvironment: indexEnvironment, mutation: true);
        var tree = await runner.RunAsync(
            gitPath, workspacePath, ["write-tree"], cancellationToken, extraEnvironment: indexEnvironment, mutation: true);
        var treeSha = tree.Output.Trim();
        return updated.Succeeded && tree.Succeeded && LocalCommitOperation.IsObjectId(treeSha)
            ? new TreeBuild(null, treeSha)
            : new TreeBuild(LocalCommitPreparationOutcome.GitFailed, null);
    }

    /// <summary>The tree must differ from the parent in exactly the approved paths, with the intended modes and blobs, and nowhere
    /// else; a path whose bytes equal the parent's is not a change and refuses.</summary>
    private async Task<LocalCommitPreparationOutcome?> VerifyTreeDifferenceAsync(
        string gitPath,
        string workspacePath,
        string parentSha,
        string treeSha,
        List<Change> plan,
        Dictionary<string, ParentEntry> parentEntries,
        CancellationToken cancellationToken)
    {
        var diff = await runner.RunAsync(
            gitPath, workspacePath, ["diff-tree", "-r", "--raw", "-z", "--no-renames", parentSha, treeSha], cancellationToken);
        if (!diff.Succeeded)
        {
            return LocalCommitPreparationOutcome.GitFailed;
        }

        var tokens = diff.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < tokens.Length; index += 2)
        {
            observed[tokens[index + 1]] = tokens[index];
        }

        if (tokens.Length % 2 != 0 || observed.Count != plan.Count)
        {
            return LocalCommitPreparationOutcome.SourceChanged;
        }

        foreach (var change in plan)
        {
            if (!observed.TryGetValue(change.Path, out var record))
            {
                return LocalCommitPreparationOutcome.SourceChanged;
            }

            var fields = record.TrimStart(':').Split(' ');
            var expected = change.IsDeletion
                ? new[] { parentEntries[change.Path].Mode, "000000", parentEntries[change.Path].ObjectId, ZeroObjectId, "D" }
                : new[]
                {
                    change.IsNew ? "000000" : parentEntries[change.Path].Mode, change.Mode,
                    change.IsNew ? ZeroObjectId : parentEntries[change.Path].ObjectId, change.BlobId!, change.IsNew ? "A" : "M",
                };
            if (fields.Length != 5 || !fields.SequenceEqual(expected, StringComparer.Ordinal))
            {
                return LocalCommitPreparationOutcome.UnsupportedChange;
            }
        }

        return null;
    }

    /// <summary>Attributes come from the immutable proposed tree alone. Any filter, ident or working-tree-encoding attribute
    /// refuses before a converter could run; built-in text and EOL handling is accepted only when converting the admitted bytes is an
    /// identity; every on-disk attribute file along a changed path must be represented in the tree.</summary>
    private async Task<LocalCommitPreparationOutcome?> VerifyAttributesAndConversionAsync(
        string gitPath, string workspacePath, string treeSha, List<Change> plan, CancellationToken cancellationToken)
    {
        var present = plan.Where(change => !change.IsDeletion).ToList();
        if (present.Count == 0)
        {
            return null;
        }

        var directories = new SortedSet<string>(StringComparer.Ordinal) { string.Empty };
        foreach (var change in plan)
        {
            var segments = change.Path.Split('/');
            for (var depth = 1; depth < segments.Length; depth++)
            {
                directories.Add(string.Join('/', segments.Take(depth)));
            }
        }

        var represented = new StringBuilder();
        foreach (var directory in directories)
        {
            var relative = directory.Length == 0 ? ".gitattributes" : directory + "/.gitattributes";
            if (File.Exists(Path.Combine(workspacePath, relative.Replace('/', '\\'))))
            {
                represented.Append(treeSha).Append(':').Append(relative).Append('\n');
            }
        }

        if (represented.Length > 0)
        {
            var batch = await runner.RunAsync(
                gitPath, workspacePath, ["cat-file", "--batch-check"], cancellationToken,
                standardInput: Encoding.UTF8.GetBytes(represented.ToString()));
            if (!batch.Succeeded)
            {
                return LocalCommitPreparationOutcome.GitFailed;
            }

            if (batch.Output.Contains(" missing", StringComparison.Ordinal))
            {
                return LocalCommitPreparationOutcome.AttributeSourceUnrepresented;
            }
        }

        var input = string.Concat(present.Select(change => change.Path + "\0"));
        var attributes = await runner.RunAsync(
            gitPath, workspacePath, ["check-attr", "-z", "-a", "--stdin"], cancellationToken,
            standardInput: Encoding.UTF8.GetBytes(input), attributeSourceTree: treeSha);
        if (!attributes.Succeeded)
        {
            return LocalCommitPreparationOutcome.GitFailed;
        }

        var tokens = attributes.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < tokens.Length; index += 3)
        {
            if (tokens[index + 1] is "filter" or "ident" or "working-tree-encoding" && tokens[index + 2] != "unspecified")
            {
                return LocalCommitPreparationOutcome.ConversionRefused;
            }
        }

        foreach (var change in present)
        {
            var converted = await runner.RunAsync(
                gitPath, workspacePath, ["hash-object", "--path=" + change.Path, "--stdin"], cancellationToken,
                standardInput: change.Bytes, attributeSourceTree: treeSha);
            if (converted.Outcome != LocalCommitGitOutcome.Exited)
            {
                return LocalCommitPreparationOutcome.GitFailed;
            }

            if (converted.ExitCode != 0 || !string.Equals(converted.Output.Trim(), change.BlobId, StringComparison.Ordinal))
            {
                return LocalCommitPreparationOutcome.ConversionRefused;
            }
        }

        return null;
    }

    private readonly record struct CommitIdentity(string Name, string Email);

    private async Task<CommitIdentity?> ReadIdentityAsync(string gitPath, string workspacePath, CancellationToken cancellationToken)
    {
        var name = await runner.RunAsync(
            gitPath, workspacePath, ["config", "--get", "user.name"], cancellationToken, keepUserConfiguration: true);
        var email = await runner.RunAsync(
            gitPath, workspacePath, ["config", "--get", "user.email"], cancellationToken, keepUserConfiguration: true);
        if (!name.Succeeded || !email.Succeeded)
        {
            return null;
        }

        var configuredName = name.Output.Trim('\r', '\n');
        var configuredEmail = email.Output.Trim('\r', '\n');
        return IsValidIdentityPart(configuredName, allowSpaces: true) && IsValidIdentityPart(configuredEmail, allowSpaces: false)
            && configuredEmail.Contains('@')
                ? new CommitIdentity(configuredName, configuredEmail)
                : null;
    }

    private static bool IsValidIdentityPart(string value, bool allowSpaces) =>
        value.Length is > 0 and <= 256
        && value == value.Trim()
        && !value.Any(character => character is '<' or '>' or '\n' or '\r' or '\0' or '\t' || (!allowSpaces && character == ' '));

    private async Task<string?> CreateCommitAsync(
        string gitPath,
        string workspacePath,
        string treeSha,
        string parentSha,
        CommitIdentity identity,
        long unixSeconds,
        string message,
        CancellationToken cancellationToken)
    {
        var date = $"{unixSeconds} +0000";
        var environment = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = identity.Name,
            ["GIT_AUTHOR_EMAIL"] = identity.Email,
            ["GIT_AUTHOR_DATE"] = date,
            ["GIT_COMMITTER_NAME"] = identity.Name,
            ["GIT_COMMITTER_EMAIL"] = identity.Email,
            ["GIT_COMMITTER_DATE"] = date,
        };
        var created = await runner.RunAsync(
            gitPath, workspacePath, ["commit-tree", treeSha, "-p", parentSha], cancellationToken,
            standardInput: Encoding.UTF8.GetBytes(message), extraEnvironment: environment, mutation: true);
        var commitSha = created.Output.Trim();
        var expected = CommitObjectId(BuildCommitObjectContent(treeSha, parentSha, identity.Name, identity.Email, unixSeconds, message));
        return created.Succeeded && string.Equals(commitSha, expected, StringComparison.Ordinal) ? commitSha : null;
    }
}
