using System.Text;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The fixture's only sources of configuration are paths derived from its own executable location, never the environment
/// (the host clears it) and never an argument. The executable must sit in <c>bin</c> directly under a disposable root that
/// carries the harness ownership marker; candidate edits are allowed only in a real worktree beneath that root's
/// <c>workspaces</c> directory, with no reparse point on any path segment.
/// </summary>
public sealed class OwnedLocation
{
    public const string MarkerFile = ".devalcopilot-e2e-owner";
    public const string CandidateRelativePath = "src/Feature.cs";

    private OwnedLocation(string root)
    {
        Root = root;
        WorkspaceRoot = Path.Combine(root, "workspaces");
        ArtifactRoot = Path.Combine(root, "artifacts");
        LogPath = Path.Combine(root, "fixture", "invocations.jsonl");
        UsageScriptPath = Path.Combine(root, "fixture", "account-usage.json");
        UsageCounterPath = Path.Combine(root, "fixture", "account-usage.reads");
    }

    public string Root { get; }

    public string WorkspaceRoot { get; }

    public string ArtifactRoot { get; }

    public string LogPath { get; }

    /// <summary>The scripted account-usage answers the journey writes for the closed App Server mode.</summary>
    public string UsageScriptPath { get; }

    /// <summary>The read counter the closed App Server mode keeps, so each launch answers its own scripted read.</summary>
    public string UsageCounterPath { get; }

    public static OwnedLocation ResolveFromExecutable(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The executable location is not an absolute path.");
        }

        var binDirectory = Path.GetDirectoryName(processPath)!;
        var root = Path.GetDirectoryName(binDirectory);
        if (root is null
            || !string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(root, MarkerFile))
            || HasReparsePoint(processPath, Path.GetDirectoryName(root) ?? root))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The executable is not inside an owned disposable root.");
        }

        return new OwnedLocation(root);
    }

    /// <summary>The current directory must be a real worktree this root's host created: inside the workspace tree, carrying a
    /// Git worktree link, with no reparse point from the root to it.</summary>
    public string RequireWorktree(string currentDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        if (!IsInside(WorkspaceRoot, full)
            || HasReparsePoint(full, Root)
            || !(File.Exists(Path.Combine(full, ".git")) || Directory.Exists(Path.Combine(full, ".git"))))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The working directory is not an owned candidate worktree.");
        }

        return full;
    }

    /// <summary>The fixed candidate file inside a verified worktree; every segment is checked for aliases.</summary>
    public string RequireCandidateFile(string worktree)
    {
        var path = Path.GetFullPath(Path.Combine(worktree, CandidateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(worktree, path) || HasReparsePoint(path, Root) || !File.Exists(path))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The candidate file is missing or is not a plain file inside the worktree.");
        }

        return path;
    }

    /// <summary>A sink path must be a file location inside the root's artifact tree, with no alias on the leaf or any existing
    /// ancestor, and not an existing directory.</summary>
    public string RequireArtifactSink(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The output path is not absolute.");
        }

        var full = Path.GetFullPath(path);
        if (!IsInside(ArtifactRoot, full) || !IsPlainDestination(full))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The output path is outside the owned artifact tree.");
        }

        return full;
    }

    /// <summary>The invocation log's destination, checked as every writable destination is: inside the root, a plain file or
    /// not yet existing, and reached through no alias.</summary>
    public string RequireLogDestination()
    {
        if (!IsPlainDestination(LogPath))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The invocation log destination is not a plain location inside the owned root.");
        }

        return LogPath;
    }

    /// <summary>The scripted usage answers, which must be an existing plain file inside the root reached through no alias.</summary>
    public string RequireUsageScript()
    {
        if (!IsPlainDestination(UsageScriptPath) || !File.Exists(UsageScriptPath) || !IsPlainDestination(UsageCounterPath))
        {
            throw new FixtureRefusal(FixtureRefusal.OwnershipRefused, "The usage script is missing or is not a plain file inside the owned root.");
        }

        return UsageScriptPath;
    }

    /// <summary>True when <paramref name="path"/> is strictly inside the root, neither it nor any existing ancestor beneath the root
    /// is a reparse point, and it is not an existing directory. Nothing is created or opened.</summary>
    private bool IsPlainDestination(string path)
    {
        var full = Path.GetFullPath(path);
        return IsInside(Root, full) && !HasReparsePoint(full, Root) && !Directory.Exists(full);
    }

    public static bool IsInside(string root, string candidate)
    {
        var relation = Path.GetRelativePath(root, candidate);
        return relation != "." && !relation.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relation);
    }

    /// <summary>True when the path, or any existing ancestor beneath <paramref name="stopAt"/>, is a reparse point.</summary>
    public static bool HasReparsePoint(string path, string stopAt)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopAt));
        while (current.Length > stop.Length && IsInside(stop, current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            current = Path.GetDirectoryName(current)!;
        }

        return false;
    }

    public static string ReadAllStandardInput()
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
