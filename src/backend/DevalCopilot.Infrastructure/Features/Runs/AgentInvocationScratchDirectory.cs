namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// A fixed, application-owned scratch location for one Codex planning invocation's non-evidence
/// input file (the output-schema file Codex is constrained to) — never inside the repository or
/// the isolated workspace, and never itself durable evidence, unlike the sealed artifacts the
/// invocation produces. Deleted after each invocation attempt; safe to recreate if missing.
/// </summary>
internal static class AgentInvocationScratchDirectory
{
    public static string EnsureExists(Guid runId, Guid attemptId) => EnsureExists(ScratchPath(runId, attemptId));

    /// <summary>Path-based overload, extracted so a test can exercise the exact same algorithm
    /// against an isolated temporary root instead of the real shared
    /// <c>%LocalAppData%\DevalCopilot\agent-scratch</c> directory.</summary>
    internal static string EnsureExists(string path)
    {
        ValidateNoAncestorIsAReparsePoint(path);

        Directory.CreateDirectory(path);

        // Defensive: CreateDirectory only ever creates ordinary directories, but if the leaf
        // already existed (e.g. planted by something else, or a race) as a reparse point, this
        // call above was a silent no-op against it — never trust that path was actually created
        // as an ordinary directory without checking.
        if (HasReparsePointAttribute(path))
        {
            throw new IOException("The Agent invocation scratch directory unexpectedly resolved to a reparse point.");
        }

        return path;
    }

    /// <summary>
    /// Walks every ancestor of <paramref name="path"/> up to the filesystem root — never
    /// stopping just because a lower segment does not exist yet. A segment that does not exist
    /// is skipped over (it cannot be a reparse point, and <see cref="Directory.CreateDirectory"/>
    /// will make it a fresh, ordinary directory), but the walk still continues past it to inspect
    /// whatever DOES already exist further up the chain — e.g. the "agent-scratch" root, or a
    /// run-level directory left behind by an earlier attempt on the same run (that directory is
    /// never deleted at the run level, only at the attempt-id leaf). <see cref="Directory.CreateDirectory"/>
    /// happily creates the remaining segments underneath whatever an existing ancestor actually
    /// resolves to, so every EXISTING ancestor — however far up the chain it sits — must be
    /// validated before ever creating anything through it. Stopping the loop early at the first
    /// non-existent segment (the original bug this fixes) let a reparse point sitting ABOVE that
    /// segment go completely uninspected.
    /// </summary>
    private static void ValidateNoAncestorIsAReparsePoint(string path)
    {
        var ancestor = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(ancestor))
        {
            if (Directory.Exists(ancestor) && HasReparsePointAttribute(ancestor))
            {
                throw new IOException(
                    "Refusing to create the Agent invocation scratch directory through a reparse-point ancestor.");
            }

            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, ancestor, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            ancestor = parent;
        }
    }

    public static void TryDelete(Guid runId, Guid attemptId) => TryDelete(ScratchPath(runId, attemptId));

    /// <summary>Path-based overload, extracted for the same isolated-temp-root testing reason as
    /// <see cref="EnsureExists(string)"/>.</summary>
    internal static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            if (HasReparsePointAttribute(path))
            {
                // The leaf itself is a reparse point: never recurse through it (that would follow
                // the link and delete a physically different location's content this application
                // does not own — behavior .NET has not consistently guaranteed either way across
                // versions). A non-recursive delete removes only the link itself, never the
                // target's contents, which is enough to satisfy this method's best-effort,
                // never-data-loss cleanup philosophy.
                Directory.Delete(path, recursive: false);
                return;
            }

            if (AnyAncestorHasReparsePoint(path))
            {
                // The leaf is an ordinary directory, but it is only *reachable* through a
                // reparse-point ancestor (e.g. the run-level directory is itself a junction) — the
                // real, physical directory a recursive delete would touch lives wherever that
                // junction actually points, not at the logical path this method was asked to
                // clean up. Deleting nothing through this path is the only safe choice; the
                // scratch directory is left as harmless orphaned bytes, same as any other
                // best-effort cleanup failure here.
                return;
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed cleanup leaves harmless orphaned scratch bytes behind, never
            // data loss — nothing durable ever references this directory.
        }
    }

    private static string ScratchPath(Guid runId, Guid attemptId) => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "DevalCopilot",
        "agent-scratch",
        runId.ToString(),
        attemptId.ToString());

    /// <summary>Walks strictly upward from (not including) <paramref name="leafPath"/> to the
    /// filesystem root, true if any ancestor segment is a reparse point. Distinct from
    /// <see cref="PathOrAnyAncestorHasReparsePoint"/>, which also includes the leaf itself and is
    /// used where the leaf and its ancestors are equally disqualifying (pre-invocation launch and
    /// scratch-directory validation) — here the leaf's own reparse-point-ness is handled by a
    /// separate, deliberately different code path (a non-recursive delete of the link itself)
    /// immediately above this check's call site.</summary>
    private static bool AnyAncestorHasReparsePoint(string leafPath)
    {
        var current = Path.GetDirectoryName(leafPath);
        while (!string.IsNullOrEmpty(current))
        {
            if (HasReparsePointAttribute(current))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// Walks from <paramref name="leafPath"/> up through every ancestor directory to the
    /// filesystem root (e.g. <c>C:\</c>), rejecting if any segment on that chain — the leaf
    /// itself, an intermediate directory, or the root — carries the reparse-point attribute.
    /// Mirrors, at dispatch time, the identical segment-walking algorithm this application already
    /// established for capability discovery in
    /// <c>EnvironmentReadiness.PackageEntrypointResolver.PathOrAnyAncestorHasReparsePoint</c>:
    /// lexical containment (<see cref="Path.IsPathFullyQualified(string)"/>,
    /// <see cref="File.Exists"/>) proves nothing once a junction or symbolic link sits anywhere on
    /// the chain between the root and the file or directory — the OS can physically open a
    /// completely different location than the string implies. Fails closed (treats as a reparse
    /// point) if any segment cannot be inspected. Shared by both this class's own scratch
    /// directory and <see cref="CodexPlanningAdapter"/>'s launch-component revalidation so the
    /// walk is implemented once, not duplicated.
    /// </summary>
    internal static bool PathOrAnyAncestorHasReparsePoint(string leafPath)
    {
        var current = leafPath;
        while (true)
        {
            if (HasReparsePointAttribute(current))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                // Reached the filesystem root (e.g. "C:\") without any segment being a reparse
                // point.
                return false;
            }

            current = parent;
        }
    }

    private static bool HasReparsePointAttribute(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
