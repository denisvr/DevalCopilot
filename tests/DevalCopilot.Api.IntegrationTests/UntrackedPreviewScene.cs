using System.Diagnostics;
using System.Text;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests;

/// <summary>
/// A real Git repository on the real Windows filesystem for the physically-proven untracked-preview tests (ADR-0022): a worktree
/// whose untracked paths are REAL NTFS hard links (<c>mklink /H</c>) to a file outside it, next to ordinary single-name files.
/// Expected bytes are independent literals. Nothing is mocked: the capture, the manifest builders, the artifact store and the
/// provider adapters that use this scene are the production ones.
/// </summary>
internal sealed class UntrackedPreviewScene : IDisposable
{
    internal const string Sentinel = "OUTSIDE-PREVIEW-SENTINEL-5d2e bytes that live outside the worktree";
    internal const string SafeText = "SAFE-SIBLING-PREVIEW-9b41 an unrelated untracked file";
    internal const string ControlText = "SINGLE-NAME-CONTROL-77c0 an ordinary nested file";

    /// <summary>Untracked paths that are hard links to the outside file, at a root and a nested position.</summary>
    internal static readonly string[] LinkedPaths = ["linked.txt", "deep/nested/linked.txt"];

    /// <summary>Tracked paths that are committed with ordinary text and then replaced by hard links to the outside file.</summary>
    internal static readonly string[] TrackedLinkedPaths = ["tracked-linked.txt", "deep/tracked-nested-linked.txt"];

    internal const string TrackedSafeBefore = "TRACKED-SAFE-BEFORE-3f0a stays as context\nold safe line\ntrailing context\n";
    internal const string TrackedSafeAfter = "TRACKED-SAFE-BEFORE-3f0a stays as context\nTRACKED-SAFE-AFTER-8d12 the attested change\ntrailing context\n";
    internal const string TrackedControlBefore = "control before\n";
    internal const string TrackedControlAfter = "TRACKED-CONTROL-AFTER-56be nested single-name file\n";

    private UntrackedPreviewScene(string root, string repository, string outsideFile)
    {
        Root = root;
        Repository = repository;
        OutsideFile = outsideFile;
    }

    internal string Root { get; }

    internal string Repository { get; }

    internal string OutsideFile { get; }

    internal static UntrackedPreviewScene Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devalcopilot-preview-scene-{Guid.NewGuid():N}");
        var repository = Path.Combine(root, "repo");
        Directory.CreateDirectory(repository);
        var outsideFile = Path.Combine(root, "outside-secret.txt");
        File.WriteAllText(outsideFile, Sentinel);
        Git(repository, "init", "-q");
        Git(repository, "config", "user.email", "test@example.com");
        Git(repository, "config", "user.name", "Test");
        Git(repository, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(repository, "tracked.txt"), "original");
        Git(repository, "add", "tracked.txt");
        Git(repository, "commit", "-q", "-m", "initial");
        return new UntrackedPreviewScene(root, repository, outsideFile);
    }

    /// <summary>The unsafe untracked paths: each is the outside file under a second name inside the worktree.</summary>
    internal void AddOutsideHardLinks()
    {
        foreach (var relativePath in LinkedPaths)
        {
            HardLink(relativePath, OutsideFile);
        }
    }

    /// <summary>Commits the tracked fixtures: the two paths that will become outside hard links, a safe sibling and a nested control.</summary>
    internal void CommitTrackedFixtures()
    {
        foreach (var relativePath in TrackedLinkedPaths)
        {
            Write(relativePath, "committed text of " + relativePath + "\n");
        }

        Write("tracked-safe.txt", TrackedSafeBefore);
        Write("deep/tracked-control.txt", TrackedControlBefore);
        Git(Repository, "add", "-A");
        Git(Repository, "commit", "-q", "-m", "tracked fixtures");
    }

    /// <summary>Replaces each committed linked path by a second name for the outside file, and edits the safe siblings.</summary>
    internal void MakeTrackedChanges()
    {
        foreach (var relativePath in TrackedLinkedPaths)
        {
            File.Delete(Path.Combine(Repository, relativePath.Replace('/', '\\')));
            HardLink(relativePath, OutsideFile);
        }

        Write("tracked-safe.txt", TrackedSafeAfter);
        Write("deep/tracked-control.txt", TrackedControlAfter);
    }

    /// <summary>The healthy siblings that must stay available: an unrelated top-level file and an ordinary nested file.</summary>
    internal void AddSafeSiblings()
    {
        Write("safe-sibling.txt", SafeText);
        Write("deep/control.txt", ControlText);
    }

    internal void Write(string relativePath, string content)
    {
        var path = Path.Combine(Repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
    }

    /// <summary>Gives <paramref name="existingFile"/> a second name at <paramref name="relativePath"/> inside the worktree.</summary>
    internal void HardLink(string relativePath, string existingFile)
    {
        var linkPath = Path.Combine(Repository, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        Run("cmd.exe", ["/c", "mklink", "/H", linkPath, existingFile], null);
    }

    /// <summary>Gives a worktree file a second name OUTSIDE it, without changing its bytes, identity, or status.</summary>
    internal void AddOutsideAliasOf(string relativePath, string aliasName)
    {
        var existing = Path.Combine(Repository, relativePath.Replace('/', '\\'));
        Run("cmd.exe", ["/c", "mklink", "/H", Path.Combine(Root, aliasName), existing], null);
    }

    internal static void Git(string workingDirectory, params string[] arguments) => Run("git", arguments, workingDirectory);

    private static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} failed: {output}");
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
