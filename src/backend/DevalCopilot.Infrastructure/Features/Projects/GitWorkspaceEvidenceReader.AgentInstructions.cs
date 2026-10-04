using System.Runtime.Versioning;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// The Agent-context observation of the exact root <c>AGENTS.md</c> and <c>CLAUDE.md</c>. Git is asked only fixed,
/// hardened questions with literal pathspecs naming those two files: whether each is tracked (and with which index
/// flags) and whether an untracked one is ignored. Bytes are acquired only through <see cref="AgentInstructionFileReader"/>,
/// which proves containment on the open handle and bounds the read; only those proven bytes are then given to Git on
/// standard input (<c>hash-object --no-filters --stdin</c>) for the independent raw identity, and the same handle is read
/// again to verify them. No repository pathname is ever given to Git for this identity. An untracked file's identity must
/// also equal the one the checkpoint fingerprint itself covers (that fingerprint observation is the unchanged, ordinary
/// one and carries no such containment guarantee). A host without the physical proof omits both files.
/// </summary>
public sealed partial class GitWorkspaceEvidenceReader
{
    private const string LiteralPathspecs = "--literal-pathspecs";

    private async Task<GitWorkspaceInstructionObservation> ObserveInstructionContextAsync(
        string gitPath,
        string workspacePath,
        IReadOnlyList<(string Path, string Hash)> untrackedHashes,
        CancellationToken cancellationToken)
    {
        var names = GitWorkspaceInstructionContext.FileNames;
        if (!OperatingSystem.IsWindows() || !physicalContainmentAvailable)
        {
            // No physical-containment proof on this host: never fall back to lexical containment.
            return AllOmitted(names);
        }

        var rootFinalPath = WindowsFinalPathResolver.TryResolveDirectoryFinalPath(workspacePath)?.TrimEnd('\\');
        if (rootFinalPath is null)
        {
            return AllOmitted(names);
        }

        var tracked = await RunAsync(
            gitPath, workspacePath, [LiteralPathspecs, "ls-files", "-z", "-v", "--", .. names], cancellationToken);
        var ignored = await RunAsync(
            gitPath, workspacePath,
            [LiteralPathspecs, "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", .. names],
            cancellationToken);
        var failure = MapFailure(tracked, ignored);
        if (failure is not null)
        {
            return GitWorkspaceInstructionObservation.Failed(failure.Value);
        }

        var trackedTags = ParseTrackedTags(tracked.Output);
        if (trackedTags is null)
        {
            return GitWorkspaceInstructionObservation.Failed(GitWorkspaceEvidenceOutcome.InvalidGitState);
        }

        var ignoredPaths = ignored.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var fingerprintIdentity = untrackedHashes.ToDictionary(file => file.Path, file => file.Hash, StringComparer.Ordinal);

        var files = new List<GitWorkspaceInstructionFile>(names.Count);
        foreach (var name in names)
        {
            if (trackedTags.TryGetValue(name, out var tags))
            {
                if (tags.Contains('M'))
                {
                    files.Add(AgentInstructionFileReader.Omitted(name, GitWorkspaceInstructionOmission.Unmerged));
                }
                else if (tags.Any(tag => tag != 'H'))
                {
                    files.Add(AgentInstructionFileReader.Omitted(name, GitWorkspaceInstructionOmission.IndexFlag));
                }
                else
                {
                    var trackedRead = await ReadSourceAsync(gitPath, workspacePath, rootFinalPath, name, null, cancellationToken);
                    if (trackedRead.Failure is not null)
                    {
                        return GitWorkspaceInstructionObservation.Failed(trackedRead.Failure.Value);
                    }

                    files.Add(trackedRead.File!);
                }
            }
            else if (ignoredPaths.Contains(name))
            {
                files.Add(AgentInstructionFileReader.Omitted(name, GitWorkspaceInstructionOmission.Ignored));
            }
            else
            {
                // A path with no fingerprint identity is either absent or appeared after the status snapshot, and no
                // identity can admit text from it.
                fingerprintIdentity.TryGetValue(name, out var untrackedIdentity);
                var untracked = await ReadSourceAsync(
                    gitPath, workspacePath, rootFinalPath, name, new UntrackedIdentity(untrackedIdentity), cancellationToken);
                if (untracked.Failure is not null)
                {
                    return GitWorkspaceInstructionObservation.Failed(untracked.Failure.Value);
                }

                files.Add(untracked.File!);
            }
        }

        return GitWorkspaceInstructionObservation.Of(files);
    }

    /// <summary>The identity the checkpoint fingerprint captured for an untracked file, or none when it captured none.</summary>
    private readonly record struct UntrackedIdentity(string? Value);

    /// <summary>One source, end to end: prove and bound it through its handle, hash exactly those bytes through Git's standard
    /// input, then verify on the same handle. Git is never reached for a file whose containment is not proven or whose size
    /// is beyond the source bound, and is never given a repository path.</summary>
    [SupportedOSPlatform("windows")]
    private async Task<(GitWorkspaceEvidenceOutcome? Failure, GitWorkspaceInstructionFile? File)> ReadSourceAsync(
        string gitPath,
        string workspacePath,
        string rootFinalPath,
        string name,
        UntrackedIdentity? untracked,
        CancellationToken cancellationToken)
    {
        using var acquisition = AgentInstructionFileReader.Acquire(
            workspacePath, rootFinalPath, name, identityCapturedAtCheckpoint: untracked?.Value is not null);
        if (acquisition.Final is not null)
        {
            return (null, acquisition.Final);
        }

        var hash = await RunAsync(
            gitPath, workspacePath, ["hash-object", "--no-filters", "--stdin"], cancellationToken, acquisition.Bytes);
        if (hash.Outcome != GitCommandOutcome.Exited || hash.Truncated)
        {
            return (MapFailure(hash), null);
        }

        // A non-zero exit means Git could not hash the bytes: no identity, never text.
        var trimmed = hash.Output.Trim();
        var gitIdentity = hash.ExitCode == 0 && IsFullSha(trimmed) ? trimmed : null;
        return (null, AgentInstructionFileReader.Verify(acquisition, name, gitIdentity, untracked is not null, untracked?.Value));
    }

    private static GitWorkspaceInstructionObservation AllOmitted(IEnumerable<string> names) =>
        GitWorkspaceInstructionObservation.Of(names.Select(name =>
            new GitWorkspaceInstructionFile(
                name, GitWorkspaceInstructionStatus.Omitted, GitWorkspaceInstructionOmission.ContainmentUnproven, null, null, null)));

    /// <summary><c>ls-files -v -z</c> prints <c>&lt;tag&gt; &lt;path&gt;</c> per entry: <c>H</c> for a normal cached
    /// entry, a lowercase letter for assume-unchanged, <c>S</c> for skip-worktree, <c>M</c> for unmerged.</summary>
    private static Dictionary<string, List<char>>? ParseTrackedTags(string output)
    {
        var result = new Dictionary<string, List<char>>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.Length < 3 || record[1] != ' ')
            {
                return null;
            }

            var path = record[2..];
            if (!result.TryGetValue(path, out var tags))
            {
                result[path] = tags = [];
            }

            tags.Add(record[0]);
        }

        return result;
    }

    private sealed class GitWorkspaceInstructionObservation
    {
        private GitWorkspaceInstructionObservation(GitWorkspaceEvidenceOutcome? failure, GitWorkspaceInstructionContext? context)
        {
            Failure = failure;
            Context = context;
        }

        public GitWorkspaceEvidenceOutcome? Failure { get; }

        public GitWorkspaceInstructionContext? Context { get; }

        public static GitWorkspaceInstructionObservation Failed(GitWorkspaceEvidenceOutcome outcome) => new(outcome, null);

        public static GitWorkspaceInstructionObservation Of(IEnumerable<GitWorkspaceInstructionFile> files) =>
            new(null, new GitWorkspaceInstructionContext(files.ToArray()));

        /// <summary>Nothing changed between two observations: equal presence, classification, identities, and text,
        /// and no file whose bytes failed their independently captured identity. That failure can only mean the file
        /// changed between its identity and its read, so it is never reported as an ordinary omission.</summary>
        public bool IsConsistentWith(GitWorkspaceInstructionObservation other) =>
            Context is not null
            && other.Context is not null
            && Context.Files.SequenceEqual(other.Context.Files)
            && Context.Files.All(file => file.Omission != GitWorkspaceInstructionOmission.ContentIdentityMismatch);
    }
}
