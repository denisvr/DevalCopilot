using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

/// <summary>
/// The C# counterpart of the browser harness's ownership check: the host writes only beneath a directory that sits directly
/// under the OS temp directory, carries the harness prefix, and holds a marker whose content is exactly this run's token. An
/// inherited path, a name prefix, or a matching name alone never establishes ownership.
/// </summary>
public sealed class OwnedRootGuard
{
    public const string RootPrefix = "devalcopilot-e2e-";
    public const string MarkerFile = ".devalcopilot-e2e-owner";

    private OwnedRootGuard(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string Bin => Path.Combine(Root, "bin");

    public string Workspaces => Path.Combine(Root, "workspaces");

    public string Artifacts => Path.Combine(Root, "artifacts");

    public string Database => Path.Combine(Root, "db", "journey.db");

    public string FixtureState => Path.Combine(Root, "fixture");

    public static OwnedRootGuard Verify(string? root, string? token)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(token) || !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("The journey root or its ownership token is missing or not absolute.");
        }

        var info = new DirectoryInfo(root);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The journey root is not an existing real directory.");
        }

        var real = Path.TrimEndingDirectorySeparator(info.FullName);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(real), temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(real).StartsWith(RootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The journey root is not a harness directory directly under the temp directory.");
        }

        var marker = new FileInfo(Path.Combine(real, MarkerFile));
        if (!marker.Exists || marker.Attributes.HasFlag(FileAttributes.ReparsePoint) || File.ReadAllText(marker.FullName) != token)
        {
            throw new InvalidOperationException("The journey root does not carry this run's ownership marker.");
        }

        return new OwnedRootGuard(real);
    }

    /// <summary>Refuses unless <paramref name="path"/> is strictly inside the owned root and neither it nor any existing ancestor
    /// beneath the root is an alias (a junction or symbolic link). Nothing is created or opened.</summary>
    public void RequirePlainDestination(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OwnedLocation.IsInside(Root, full) || OwnedLocation.HasReparsePoint(full, Root))
        {
            throw new InvalidOperationException("A writable destination is outside the owned root or reached through an alias.");
        }
    }

    public async Task WriteFixtureStateFileAsync(string name, string content, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(FixtureState, name);
        RequirePlainDestination(destination);
        if (Directory.Exists(destination))
        {
            throw new InvalidOperationException("A writable destination is a directory.");
        }

        await File.WriteAllTextAsync(destination, content, cancellationToken);
    }

    public void CreateLayout()
    {
        var directories = new[] { Bin, Workspaces, Artifacts, Path.GetDirectoryName(Database)!, FixtureState };
        foreach (var directory in directories)
        {
            RequirePlainDestination(directory);
        }

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }
    }
}
