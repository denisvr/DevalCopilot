using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// The attestation of tracked changed paths for a new Agent-context capture (ADR-0024) and a human checkpoint inspection
/// (ADR-0027); the two differ only in whether the root instruction names are reserved. Every tracked changed path becomes exactly one
/// <see cref="GitWorkspaceTrackedFile"/>: either attested before/after text or a fixed omission. The CURRENT side is acquired only
/// through <see cref="TrackedFileSourceReader"/> (a held Windows handle whose exact final path beneath the resolved owned worktree,
/// regular/non-reparse attributes and single link are proven before any length or byte, rechecked after the bounded read, and
/// proven again around the independent identity operation); the BASELINE side is the exact blob of the captured HEAD, found with
/// literal paths and recorded size and type, read with replacement objects and promisor lazy fetching disabled, and accepted only
/// when its re-encoded bytes match both the recorded size and the object identity (a decoded process string is not raw-byte proof
/// otherwise). Git is never given a repository working path for any of this, never applies a filter, textconv or shell, and never
/// writes an object. The observation runs inside the capture bracket and again after it: any change between the two (a different
/// fact, or bytes that did not hold) discards the whole capture as <see cref="GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture"/>.
/// </summary>
public sealed partial class GitWorkspaceEvidenceReader
{
    private const string NoReplaceObjects = "--no-replace-objects";
    private const int TreeBatchSize = 32;

    private static readonly IReadOnlyDictionary<string, string> ObjectReadEnvironment = new Dictionary<string, string>(HardeningEnvironment)
    {
        ["GIT_NO_REPLACE_OBJECTS"] = "1",
        ["GIT_NO_LAZY_FETCH"] = "1",
    };

    private static readonly UTF8Encoding StrictUtf8Decoder = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private sealed record TreeEntry(string Mode, string Type, string Oid, long Size);

    internal sealed record BaselineRead(string? Text, GitWorkspaceTrackedOmission? Omission);

    private sealed record TrackedPlan(string Path, GitWorkspaceTrackedStatus.Expectation Expectation, GitWorkspaceTrackedOmission? Refusal);

    private readonly record struct TrackedStep(GitWorkspaceEvidenceOutcome? Failure, GitWorkspaceTrackedFile? File, bool Changed, long Retained);

    internal async Task<GitWorkspaceTrackedObservation> ObserveTrackedFilesAsync(
        string gitPath,
        string workspacePath,
        string headSha,
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        Dictionary<string, BaselineRead> baselineCache,
        bool reserveInstructionNames,
        CancellationToken cancellationToken)
    {
        var untrackedPaths = changedPaths
            .Where(path => path.IndexStatus == "?" && path.WorkTreeStatus == "?")
            .Select(path => path.Path)
            .ToHashSet(StringComparer.Ordinal);
        var plans = changedPaths
            .Where(path => !(path.IndexStatus == "?" && path.WorkTreeStatus == "?"))
            .GroupBy(path => path.Path, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Plan(group.Key, [.. group], untrackedPaths.Contains(group.Key), reserveInstructionNames))
            .ToArray();
        if (plans.Length == 0)
        {
            return GitWorkspaceTrackedObservation.Of([], changed: false);
        }

        var rootFinalPath = OperatingSystem.IsWindows() && physicalContainmentAvailable
            ? WindowsFinalPathResolver.TryResolveDirectoryFinalPath(workspacePath)?.TrimEnd('\\')
            : null;
        var candidates = plans.Where(plan => plan.Refusal is null).Select(plan => plan.Path).ToArray();
        var entries = new Dictionary<string, TreeEntry>(StringComparer.Ordinal);
        var treeFailed = new HashSet<string>(StringComparer.Ordinal);
        if (rootFinalPath is not null && candidates.Length > 0)
        {
            var listed = await ListBaselineEntriesAsync(gitPath, workspacePath, headSha, candidates, entries, treeFailed, cancellationToken);
            if (listed is not null)
            {
                return GitWorkspaceTrackedObservation.Failed(listed.Value);
            }
        }

        var files = new List<GitWorkspaceTrackedFile>(plans.Length);
        var changed = false;
        long retained = 0;
        foreach (var plan in plans)
        {
            if (plan.Refusal is { } refusal)
            {
                files.Add(GitWorkspaceTrackedFile.Omitted(plan.Path, refusal));
                continue;
            }

            if (rootFinalPath is null || !OperatingSystem.IsWindows())
            {
                // No physical-containment proof on this host: never fall back to lexical containment.
                files.Add(GitWorkspaceTrackedFile.Omitted(plan.Path, GitWorkspaceTrackedOmission.ContainmentUnproven));
                continue;
            }

            var step = await ObserveOneAsync(
                gitPath, workspacePath, rootFinalPath, plan, entries, treeFailed, baselineCache, retained, cancellationToken);
            if (step.Failure is { } failure)
            {
                return GitWorkspaceTrackedObservation.Failed(failure);
            }

            files.Add(step.File!);
            changed |= step.Changed;
            retained += step.Retained;
        }

        return GitWorkspaceTrackedObservation.Of(files, changed);
    }

    private static TrackedPlan Plan(
        string path, GitWorkspaceChangedPath[] states, bool alsoUntracked, bool reserveInstructionNames)
    {
        if (reserveInstructionNames && GitWorkspaceInstructionContext.IsReservedPath(path))
        {
            return new TrackedPlan(path, default, GitWorkspaceTrackedOmission.ReservedInstructionFile);
        }

        if (!GitWorkspaceTrackedStatus.IsEncodablePath(path))
        {
            return new TrackedPlan(path, default, GitWorkspaceTrackedOmission.UnencodablePath);
        }

        if (states.Length != 1)
        {
            return new TrackedPlan(path, default, GitWorkspaceTrackedOmission.UnsupportedStatus);
        }

        var expectation = GitWorkspaceTrackedStatus.Classify(states[0], alsoUntracked);
        return new TrackedPlan(path, expectation, expectation.Refusal);
    }

    /// <summary>Asks Git, with literal pathspecs and without replacement objects or lazy fetching, which of the paths the captured
    /// HEAD records, with mode, type, object identity and size. A path without an entry is simply not in HEAD; a path whose listing
    /// failed is recorded in <paramref name="failed"/> and later omitted as an unavailable baseline.</summary>
    private async Task<GitWorkspaceEvidenceOutcome?> ListBaselineEntriesAsync(
        string gitPath,
        string workspacePath,
        string headSha,
        IReadOnlyList<string> paths,
        Dictionary<string, TreeEntry> entries,
        HashSet<string> failed,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < paths.Count; offset += TreeBatchSize)
        {
            var batch = paths.Skip(offset).Take(TreeBatchSize).ToArray();
            var run = await ListAsync(gitPath, workspacePath, headSha, batch, cancellationToken);
            if (HardFailure(run) is { } hard)
            {
                return hard;
            }

            if (run.ExitCode == 0)
            {
                if (!TryParseTree(run.Output, entries))
                {
                    return GitWorkspaceEvidenceOutcome.InvalidGitState;
                }

                continue;
            }

            // The batch could not be listed as a whole (for example one name is not a valid object path): ask for each alone.
            foreach (var path in batch)
            {
                var single = await ListAsync(gitPath, workspacePath, headSha, [path], cancellationToken);
                if (HardFailure(single) is { } singleHard)
                {
                    return singleHard;
                }

                if (single.ExitCode != 0)
                {
                    failed.Add(path);
                }
                else if (!TryParseTree(single.Output, entries))
                {
                    return GitWorkspaceEvidenceOutcome.InvalidGitState;
                }
            }
        }

        return null;
    }

    private Task<GitCommandResult> ListAsync(
        string gitPath, string workspacePath, string headSha, IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        RunAsync(
            gitPath,
            workspacePath,
            [LiteralPathspecs, NoReplaceObjects, "ls-tree", "-z", "-l", headSha, "--", .. paths],
            cancellationToken,
            environment: ObjectReadEnvironment);

    /// <summary>A timeout, a launch failure or a truncated answer cannot be an omission: the capture itself failed.</summary>
    private static GitWorkspaceEvidenceOutcome? HardFailure(GitCommandResult result) =>
        result.Outcome == GitCommandOutcome.Exited && !result.Truncated ? null : MapFailure(result);

    private static bool TryParseTree(string output, Dictionary<string, TreeEntry> entries)
    {
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
            {
                return false;
            }

            var parts = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !IsFullSha(parts[2]) || !long.TryParse(parts[3], out var size))
            {
                // A tree or submodule entry has no size ("-"): it is still an entry, with no blob to read.
                if (parts.Length == 4 && IsFullSha(parts[2]) && parts[3] == "-")
                {
                    entries[record[(tab + 1)..]] = new TreeEntry(parts[0], parts[1], parts[2], -1);
                    continue;
                }

                return false;
            }

            entries[record[(tab + 1)..]] = new TreeEntry(parts[0], parts[1], parts[2], size);
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private async Task<TrackedStep> ObserveOneAsync(
        string gitPath,
        string workspacePath,
        string rootFinalPath,
        TrackedPlan plan,
        Dictionary<string, TreeEntry> entries,
        HashSet<string> treeFailed,
        Dictionary<string, BaselineRead> baselineCache,
        long retained,
        CancellationToken cancellationToken)
    {
        var path = plan.Path;
        var expectation = plan.Expectation;
        var facts = readHandleFacts ?? WindowsHandleFileFacts.TryGet;
        entries.TryGetValue(path, out var entry);

        if (treeFailed.Contains(path))
        {
            return Omit(path, GitWorkspaceTrackedOmission.BaselineUnavailable);
        }

        if (expectation.BaselineExpected)
        {
            if (entry is null)
            {
                return Omit(path, GitWorkspaceTrackedOmission.BaselineUnavailable);
            }

            if (ClassifyEntry(entry) is { } entryOmission)
            {
                return Omit(path, entryOmission);
            }
        }
        else if (entry is not null)
        {
            // The status says the path is new to HEAD while HEAD records it: nothing coherent to compare.
            return Omit(path, GitWorkspaceTrackedOmission.UnsupportedStatus);
        }

        TrackedFileSourceReader.Source? source = null;
        try
        {
            if (expectation.CurrentExpected)
            {
                source = TrackedFileSourceReader.Acquire(workspacePath, rootFinalPath, path, facts);
                switch (source.Kind)
                {
                    case TrackedFileSourceReader.SourceKind.Refused:
                        return Omit(path, source.Omission!.Value);
                    case TrackedFileSourceReader.SourceKind.NotFound:
                        // The status names a present file the system no longer opens: no proof, whatever the reason.
                        return Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven);
                    case TrackedFileSourceReader.SourceKind.Changed:
                        return Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven, changed: true);
                }
            }
            else
            {
                switch (TrackedFileSourceReader.ProveAbsent(workspacePath, rootFinalPath, path))
                {
                    case TrackedFileSourceReader.Absence.Proven:
                        break;
                    case TrackedFileSourceReader.Absence.Present:
                        // The status says deleted but a file stands there now: the repository is changing under the capture.
                        return Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven, changed: true);
                    case TrackedFileSourceReader.Absence.NotRegularFile:
                        return Omit(path, GitWorkspaceTrackedOmission.NotRegularFile);
                    case TrackedFileSourceReader.Absence.Unreadable:
                        return Omit(path, GitWorkspaceTrackedOmission.Unreadable);
                    default:
                        return Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven);
                }
            }

            var beforeSize = entry?.Size ?? 0;
            var currentSize = source?.Bytes!.Length ?? 0;
            if (retained + beforeSize + currentSize > GitWorkspaceTrackedFile.MaxRetainedBytes)
            {
                return Omit(path, GitWorkspaceTrackedOmission.AggregateLimit);
            }

            string? beforeText = null;
            if (expectation.BaselineExpected)
            {
                var baseline = await ReadBaselineAsync(gitPath, workspacePath, entry!, baselineCache, cancellationToken);
                if (baseline.Failure is { } failure)
                {
                    return new TrackedStep(failure, null, false, 0);
                }

                if (baseline.Read!.Omission is { } baselineOmission)
                {
                    return Omit(path, baselineOmission);
                }

                beforeText = baseline.Read.Text;
            }

            string? afterText = null;
            if (source is not null)
            {
                var hash = await RunAsync(
                    gitPath, workspacePath, ["hash-object", "--no-filters", "--stdin"], cancellationToken, source.Bytes);
                if (hash.Outcome != GitCommandOutcome.Exited || hash.Truncated)
                {
                    return new TrackedStep(MapFailure(hash), null, false, 0);
                }

                // A non-zero exit means Git could not hash the bytes: no identity, never text.
                var trimmed = hash.Output.Trim();
                var gitIdentity = hash.ExitCode == 0 && IsFullSha(trimmed) ? trimmed : null;
                var verification = TrackedFileSourceReader.Verify(source, rootFinalPath, path, facts);
                if (verification is not null)
                {
                    return verification.Kind == TrackedFileSourceReader.SourceKind.Changed
                        ? Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven, changed: true)
                        : Omit(path, verification.Omission!.Value);
                }

                if (gitIdentity is null
                    || !string.Equals(UntrackedFilePreviewReader.GitBlobSha1(source.Bytes!), gitIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    return Omit(path, GitWorkspaceTrackedOmission.ContainmentUnproven, changed: true);
                }

                if (Array.IndexOf(source.Bytes!, (byte)0) >= 0)
                {
                    return Omit(path, GitWorkspaceTrackedOmission.Binary);
                }

                try
                {
                    afterText = StrictUtf8Decoder.GetString(source.Bytes!);
                }
                catch (DecoderFallbackException)
                {
                    return Omit(path, GitWorkspaceTrackedOmission.InvalidUtf8);
                }
            }

            if (CountLines(beforeText) > GitWorkspaceTrackedFile.MaxSourceLines || CountLines(afterText) > GitWorkspaceTrackedFile.MaxSourceLines)
            {
                return Omit(path, GitWorkspaceTrackedOmission.TooManyLines);
            }

            if (beforeText is not null && afterText is not null && string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                return Omit(path, GitWorkspaceTrackedOmission.NoContentDifference);
            }

            if (beforeText is not null)
            {
                // The admitted file's facts already hold this very string, so the cache adds no source bytes of its own: it can
                // never exceed the observation's accounted facts, and a later file with the same blob reuses it.
                baselineCache[entry!.Oid] = new BaselineRead(beforeText, null);
            }

            return new TrackedStep(null, new GitWorkspaceTrackedFile(path, null, beforeText, afterText), false, beforeSize + currentSize);
        }
        finally
        {
            source?.Dispose();
        }
    }

    private static TrackedStep Omit(string path, GitWorkspaceTrackedOmission omission, bool changed = false) =>
        new(null, GitWorkspaceTrackedFile.Omitted(path, omission), changed, 0);

    /// <summary>Only a regular-file blob can be a baseline: a symbolic link, a submodule, a tree or any other mode is its own
    /// fixed omission, and an oversized blob is refused by its recorded size before any content is read.</summary>
    private static GitWorkspaceTrackedOmission? ClassifyEntry(TreeEntry entry)
    {
        if (entry.Mode == "120000")
        {
            return GitWorkspaceTrackedOmission.SymbolicLink;
        }

        if (entry.Mode == "160000" || entry.Type == "commit")
        {
            return GitWorkspaceTrackedOmission.Submodule;
        }

        if (entry.Type != "blob" || entry.Mode is not ("100644" or "100755"))
        {
            return GitWorkspaceTrackedOmission.UnsupportedMode;
        }

        return entry.Size > GitWorkspaceTrackedFile.MaxSourceBytes ? GitWorkspaceTrackedOmission.TooLarge : null;
    }

    [SupportedOSPlatform("windows")]
    private async Task<(GitWorkspaceEvidenceOutcome? Failure, BaselineRead? Read)> ReadBaselineAsync(
        string gitPath,
        string workspacePath,
        TreeEntry entry,
        Dictionary<string, BaselineRead> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(entry.Oid, out var cached))
        {
            return (null, cached);
        }

        var run = await RunAsync(
            gitPath, workspacePath, [NoReplaceObjects, "cat-file", "blob", entry.Oid], cancellationToken, environment: ObjectReadEnvironment);
        if (run.Outcome != GitCommandOutcome.Exited)
        {
            return (MapFailure(run), null);
        }

        BaselineRead read;
        if (run.ExitCode != 0)
        {
            read = new BaselineRead(null, GitWorkspaceTrackedOmission.BaselineUnavailable);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(run.Output);
            if (run.Output.Contains('\0'))
            {
                // A NUL means binary content whatever else the stream could or could not reproduce: no text is delivered either way.
                read = new BaselineRead(null, GitWorkspaceTrackedOmission.Binary);
            }
            else if (run.Truncated
                || bytes.Length != entry.Size
                || !string.Equals(UntrackedFilePreviewReader.GitBlobSha1(bytes), entry.Oid, StringComparison.OrdinalIgnoreCase))
            {
                read = new BaselineRead(null, GitWorkspaceTrackedOmission.BaselineUnverified);
            }
            else
            {
                read = new BaselineRead(run.Output, null);
            }
        }

        // Source text is retained only for an ADMITTED file (see ObserveOneAsync), where it is part of the accounted facts. A baseline
        // that is later omitted (no difference, invalid current bytes, line limit) must not stay alive in this cache outside the
        // retained-source budget, so only a result without text is remembered here.
        if (read.Text is null)
        {
            cache[entry.Oid] = read;
        }

        return (null, read);
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var lines = 0;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return text[^1] == '\n' ? lines : lines + 1;
    }

    internal sealed class GitWorkspaceTrackedObservation
    {
        private GitWorkspaceTrackedObservation(GitWorkspaceEvidenceOutcome? failure, IReadOnlyList<GitWorkspaceTrackedFile>? files, bool changed)
        {
            Failure = failure;
            Files = files;
            Changed = changed;
        }

        public GitWorkspaceEvidenceOutcome? Failure { get; }

        public IReadOnlyList<GitWorkspaceTrackedFile>? Files { get; }

        /// <summary>A source whose bytes did not hold while they were read and verified, or a deletion that turned out to be a
        /// file: that can only mean the repository changed under the capture, so it is never reported as an ordinary omission.</summary>
        public bool Changed { get; }

        public static GitWorkspaceTrackedObservation Failed(GitWorkspaceEvidenceOutcome outcome) => new(outcome, null, false);

        public static GitWorkspaceTrackedObservation Of(IReadOnlyList<GitWorkspaceTrackedFile> files, bool changed) => new(null, files, changed);

        public bool IsConsistentWith(GitWorkspaceTrackedObservation other) =>
            Files is not null && other.Files is not null && !Changed && !other.Changed && Files.SequenceEqual(other.Files);
    }
}
