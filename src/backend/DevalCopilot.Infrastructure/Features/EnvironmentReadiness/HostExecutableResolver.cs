namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Resolves a fixed candidate executable name to an absolute path using only normalized,
/// validated host <c>PATH</c> entries plus a fixed list of fallback directories, checked with
/// <see cref="File.Exists"/> only. Never spawns a shell, never invokes <c>where</c> or
/// <c>which</c>, never consults <c>PATHEXT</c> — the candidate names already carry their exact
/// extension, and this never falls back to searching the current working directory.
/// </summary>
internal static class HostExecutableResolver
{
    /// <param name="hostPathVariable">The raw <c>PATH</c> string to search. Defaults to this
    /// process's own ambient <c>PATH</c> — never project- or attacker-influenced content. Tests
    /// pass an explicit value here instead of mutating the process-wide environment variable,
    /// which would not be safe under parallel test execution.</param>
    public static string? TryResolve(
        IReadOnlyList<string> candidateExecutableNames,
        IReadOnlyList<string> fallbackDirectories,
        string? hostPathVariable = null)
    {
        var pathVariable = hostPathVariable ?? System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in BuildOrderedUniqueSearchDirectories(pathVariable, fallbackDirectories))
        {
            foreach (var candidateName in candidateExecutableNames)
            {
                var candidatePath = Path.Combine(directory, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Validates and canonicalizes every PATH entry and every fallback directory by the same
    /// rule, then deduplicates the combined, ordered list — a directory reachable both via PATH
    /// and as a fixed fallback is searched only once, at its first (PATH) position. Internal
    /// rather than private so tests can assert on the built list directly, independent of
    /// which files happen to exist on disk.
    /// </summary>
    internal static IReadOnlyList<string> BuildOrderedUniqueSearchDirectories(
        string pathVariable, IReadOnlyList<string> fallbackDirectories)
    {
        // Windows path comparison is case-insensitive; this is also what makes the same
        // directory spelled differently in PATH and in a fallback entry collapse to one.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var rawEntry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            AddIfValidAndNew(rawEntry, seen, ordered);
        }

        foreach (var rawEntry in fallbackDirectories)
        {
            AddIfValidAndNew(rawEntry, seen, ordered);
        }

        return ordered;
    }

    private static void AddIfValidAndNew(string rawEntry, HashSet<string> seen, List<string> ordered)
    {
        var normalized = NormalizeHostPathEntry(rawEntry);
        if (normalized is not null && seen.Add(normalized))
        {
            ordered.Add(normalized);
        }
    }

    /// <summary>
    /// Accepts only a non-empty, fully qualified directory entry — never a relative segment,
    /// a drive-relative path, current-directory (<c>.</c>) semantics, or a malformed string —
    /// and returns its canonical absolute form. A raw PATH segment is never combined with a
    /// candidate name directly; every entry passes through here first.
    /// </summary>
    private static string? NormalizeHostPathEntry(string rawEntry)
    {
        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return null;
        }

        var trimmed = rawEntry.Trim();

        // Rejects relative segments (including a bare "." or ".."), drive-relative paths (such
        // as "C:tools"), and anything else that is not already rooted and fully specified. A
        // path that only becomes absolute by resolving against the current working directory
        // never reaches Path.GetFullPath below.
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return null;
        }

        try
        {
            // Canonicalizes any embedded "." or ".." segment and normalizes separators, so
            // "C:\Tools\.\Git\cmd" and "C:\Tools\Git\cmd" compare and deduplicate identically.
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
