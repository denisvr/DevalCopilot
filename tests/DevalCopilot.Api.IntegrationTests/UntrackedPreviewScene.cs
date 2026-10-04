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
