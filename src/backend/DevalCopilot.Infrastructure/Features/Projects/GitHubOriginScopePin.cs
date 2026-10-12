namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Holds the project's own repository-local configuration file open, without delete sharing, for the whole observation. An
/// open file cannot be renamed, replaced or deleted, and neither can any directory that contains it, so the root's own
/// <c>.git</c> directory cannot be moved away, replaced by a gitfile, or removed to let Git fall back to a parent repository
/// while the two Git invocations run. The pin is acquired only for a real, non-reparse <c>&lt;root&gt;\.git</c> directory
/// holding a real, non-reparse regular <c>config</c> file (never a gitfile, junction, symlink or directory), and the same
/// facts are checked again after the file is open (so a redirection that raced the open is refused) and again by
/// <see cref="IsIntact"/> after the observation. Read-only: nothing is created, written or locked exclusively, and Git's own
/// read of the file is unaffected. A lexical path alone is never the proof: the hold is.
/// </summary>
internal sealed class GitHubOriginScopePin : IGitHubOriginScope
{
    private readonly FileStream _held;
    private readonly string _gitDirectory;
    private readonly string _configPath;

    private GitHubOriginScopePin(FileStream held, string gitDirectory, string configPath)
    {
        _held = held;
        _gitDirectory = gitDirectory;
        _configPath = configPath;
    }

    public static string ConfigPathOf(string root) => Path.Combine(root, ".git", "config");

    /// <summary>The pin, or null when the root does not have an intact, own, non-redirected administrative scope.</summary>
    public static IGitHubOriginScope? TryAcquire(string root)
    {
        var gitDirectory = Path.Combine(root, ".git");
        var configPath = ConfigPathOf(root);
        if (!IsPlain(gitDirectory, configPath))
        {
            return null;
        }

        FileStream held;
        try
        {
            held = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var pin = new GitHubOriginScopePin(held, gitDirectory, configPath);
        if (pin.IsIntact())
        {
            return pin;
        }

        pin.Dispose();
        return null;
    }

    /// <summary>True while the directory and the held file are still the plain, non-redirected scope.</summary>
    public bool IsIntact() => IsPlain(_gitDirectory, _configPath);

    public void Dispose() => _held.Dispose();

    private static bool IsPlain(string gitDirectory, string configPath)
    {
        try
        {
            var directory = new DirectoryInfo(gitDirectory);
            var config = new FileInfo(configPath);

            return directory.Exists
                && (directory.Attributes & FileAttributes.ReparsePoint) == 0
                && config.Exists
                && (config.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
