using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one shared set of boundary-value checks both <c>ImplementationResponseParser</c> (parsing
/// Claude's raw final-response JSON) and <c>RecordImplementationResultCommandHandler</c>
/// (independently re-validating a <see cref="Commands.RecordImplementationResult.ValidatedImplementationReport"/>
/// it did not itself construct, plus the supervisor-supplied observed Git evidence) call — never
/// two independently maintained copies that could silently drift apart. Every method here fails
/// closed to <see langword="false"/>/<see langword="null"/>; none of them ever throw on malformed
/// input, since both callers must turn a boundary violation into a safe, zero-mutation result,
/// never an unhandled exception.
/// </summary>
internal static class ImplementationEvidenceValidation
{
    public const int MaximumRelativePathLength = 512;

    /// <summary>Git porcelain's literal single-character index/work-tree status columns this
    /// slice ever expects to observe: modified, added, deleted, renamed, copied, unmerged,
    /// type-changed, untracked, ignored, or the literal space meaning "no change in this
    /// column". Any other character is never trusted, even though <c>GitChangedFile.Observe</c>
    /// itself only enforces length — this is Application-layer defense in depth over what the
    /// evidence reader is expected to ever actually produce.</summary>
    private static readonly IReadOnlySet<char> ValidGitStatusCharacters = new HashSet<char>
    {
        ' ', 'M', 'A', 'D', 'R', 'C', 'U', 'T', '?', '!',
    };

    public static bool IsValidCommitSha(string value) =>
        value.Length == 40 && value.All(char.IsAsciiHexDigit);

    public static bool IsValidFingerprintSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    public static bool IsValidGitStatusValue(string value) =>
        value.Length == 1 && ValidGitStatusCharacters.Contains(value[0]);

    /// <summary>Never absolute, never drive-rooted, never containing a parent-directory
    /// traversal segment, never an empty or root-only path, and never longer than
    /// <see cref="MaximumRelativePathLength"/>. This is a defensive shape check only — it says
    /// nothing about whether the path actually changed; that is the exact-set comparison against
    /// independently observed Git evidence in <c>RecordImplementationResultCommandHandler</c>.</summary>
    public static bool IsSafeRepositoryRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumRelativePathLength)
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || Path.IsPathRooted(path))
        {
            return false;
        }

        var segments = normalized.Split('/');
        return segments.All(segment => segment.Length > 0 && segment != ".." && segment != ".");
    }

    /// <summary>A list of changed-relative-paths is valid only when every entry is itself safe,
    /// the list stays within <paramref name="maximumCount"/>, and no entry repeats — duplicates
    /// collapse silently under a naive set comparison elsewhere, so uniqueness is checked
    /// explicitly here, once, for both callers.</summary>
    public static bool AreValidChangedRelativePaths(IReadOnlyList<string> paths, int maximumCount)
    {
        if (paths.Count > maximumCount)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!IsSafeRepositoryRelativePath(path) || !seen.Add(path))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Independently re-validates the supervisor-supplied, independently observed Git
    /// changed-path evidence before it is ever used to construct a <c>GitChangedFile</c> row —
    /// this is evidence about the repository, not Claude's own report, but it is never trusted
    /// unchecked either: a malformed or malicious <see cref="IGitWorkspaceEvidenceReader"/>
    /// implementation, or a corrupted value in transit, must never reach a Domain factory that
    /// would throw on it. Every path (and, when present, its rename source) must be safe and
    /// repository-relative, no path may repeat, and both status columns must be one of the
    /// closed set of Git porcelain characters this slice ever expects.</summary>
    public static bool AreValidObservedChangedPaths(IReadOnlyList<GitWorkspaceChangedPath> paths, int maximumCount)
    {
        if (paths.Count > maximumCount)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!IsSafeRepositoryRelativePath(path.Path) || !seen.Add(path.Path))
            {
                return false;
            }

            if (path.PreviousPath is not null && !IsSafeRepositoryRelativePath(path.PreviousPath))
            {
                return false;
            }

            if (!IsValidGitStatusValue(path.IndexStatus) || !IsValidGitStatusValue(path.WorkTreeStatus))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The one shared construction of the ExecutionReport structured-content JSON this
    /// slice ever produces, and the one shared call into
    /// <see cref="CollaborationMessageContentPolicy"/> that validates it — never independently
    /// re-implemented by the parser and the handler.</summary>
    public static bool IsValidExecutionReportContent(string implementationNotes, string recommendedVerification)
    {
        var structuredContentJson = JsonSerializer.Serialize(new
        {
            completedWork = implementationNotes,
            verification = recommendedVerification,
        });

        try
        {
            CollaborationMessageContentPolicy.Validate(CollaborationMessageType.ExecutionReport, structuredContentJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
