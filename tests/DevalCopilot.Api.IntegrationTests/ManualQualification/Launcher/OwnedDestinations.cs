using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The locations a reviewed class may use. The one disposable root this session created (with its workspaces and artifacts
/// directories) and the one run it created are pinned once by the environment that made them, never from a discovered path. The
/// production scratch boundaries are the exact, fixed host-owned locations the unchanged production code uses and no other directory:
/// the tool-discovery scratch directory (the working directory of every version probe) and, per attempt, the Codex output-schema
/// directory <c>agent-scratch\&lt;run&gt;\&lt;attempt&gt;</c>. Until the root and the run are pinned nothing is inside them, so a class
/// that requires a destination matches nothing.
/// </summary>
public sealed partial class OwnedDestinations(string? localApplicationData = null)
{
    private readonly string _localApplicationData =
        Path.GetFullPath(localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private volatile string? _root;
    private volatile string? _workspaces;
    private volatile string? _artifacts;
    private readonly object _runGate = new();
    private Guid? _runId;

    /// <summary>The fixed host-owned working directory of every version probe, exactly as production names it.</summary>
    public string DiscoveryScratch => Path.Combine(_localApplicationData, "DevalCopilot", "tool-discovery-scratch");

    private string AgentScratchRoot => Path.Combine(_localApplicationData, "DevalCopilot", "agent-scratch");

    /// <summary>Pins the owned root and its two production-shaped directories; a later call changes nothing.</summary>
    public void Pin(string ownedRoot, string? workspaces = null, string? artifacts = null)
    {
        if (!Path.IsPathFullyQualified(ownedRoot))
        {
            throw new ArgumentException("The owned root must be an absolute path.", nameof(ownedRoot));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRoot));
        _root = root;
        _workspaces = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaces ?? Path.Combine(root, "workspaces")));
        _artifacts = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifacts ?? Path.Combine(root, "artifacts")));
    }

    /// <summary>Pins the one run this session created. Attempt-bound destinations must belong to it.</summary>
    public void PinRun(Guid runId)
    {
        lock (_runGate)
        {
            _runId ??= runId;
        }
    }

    private Guid? Run
    {
        get
        {
            lock (_runGate)
            {
                return _runId;
            }
        }
    }

    /// <summary>True when <paramref name="path"/> is an absolute path that, normalized, lies strictly beneath the pinned root.</summary>
    public bool Contains(string? path) => Beneath(_root, path);

    /// <summary>The same test, also accepting the root itself (a working directory may be the root of a workspace).</summary>
    public bool ContainsOrIs(string? path) => Contains(path) || Is(_root, path);

    /// <summary>True when <paramref name="path"/> lies inside the owned workspaces directory (or is the directory itself).</summary>
    public bool InWorkspaces(string? path) => Beneath(_workspaces, path) || Is(_workspaces, path);

    /// <summary>True when <paramref name="path"/> is exactly <paramref name="directory"/>, the discovery scratch directory.</summary>
    public bool IsDiscoveryScratch(string? directory) => Is(DiscoveryScratch, directory);

    /// <summary>The attempt-bound artifact file <c>&lt;artifacts&gt;\runs\&lt;run&gt;\attempts\&lt;attempt&gt;\&lt;stem&gt;.partial</c>
    /// of this session's run. Returns the attempt id, or null when the path is not exactly such a file.</summary>
    public Guid? AttemptOfArtifact(string? path, string stem)
    {
        if (_artifacts is not { } artifacts || Run is not { } pinnedRun || path is null || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        var match = ArtifactFile().Match(Path.GetFullPath(path).Replace('\\', '/'));
        var prefix = artifacts.Replace('\\', '/') + "/runs/";
        if (!match.Success || !match.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(match.Groups["stem"].Value, stem, StringComparison.Ordinal)
            || !Guid.TryParseExact(match.Groups["run"].Value, "D", out var run) || run != pinnedRun
            || !Guid.TryParseExact(match.Groups["attempt"].Value, "D", out var attempt))
        {
            return null;
        }

        return attempt;
    }

    /// <summary>The Codex schema file of exactly this run and this attempt in the fixed production scratch location,
    /// <c>agent-scratch\&lt;run&gt;\&lt;attempt&gt;\schema.json</c>.</summary>
    public bool IsAttemptSchema(string? path, Guid attempt) =>
        Run is { } pinnedRun && path is not null && Path.IsPathFullyQualified(path)
        && string.Equals(
            Path.GetFullPath(path),
            Path.Combine(AgentScratchRoot, pinnedRun.ToString(), attempt.ToString(), "schema.json"),
            StringComparison.OrdinalIgnoreCase);

    private static bool Beneath(string? root, string? path)
    {
        if (root is null || path is null || !Path.IsPathFullyQualified(path) || path.Contains('\0'))
        {
            return false;
        }

        return Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Is(string? root, string? path) =>
        root is not null && path is not null && Path.IsPathFullyQualified(path) && !path.Contains('\0')
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^.*/runs/(?<run>[0-9a-fA-F-]{36})/attempts/(?<attempt>[0-9a-fA-F-]{36})/(?<stem>[a-z-]+)\\.partial$")]
    private static partial Regex ArtifactFile();
}
