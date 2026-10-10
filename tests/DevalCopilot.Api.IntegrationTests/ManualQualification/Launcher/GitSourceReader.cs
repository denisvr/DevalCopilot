using System.Security.Cryptography;
using System.Text;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public static class GitSourceReader
{
    private static readonly EnumerationOptions EveryEntry = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    public static SourceSnapshot Read(string directory)
    {
        var head = GitProcess.Run(directory, "rev-parse", "HEAD").Trim();
        var headReference = GitProcess.Run(directory, "rev-parse", "--symbolic-full-name", "HEAD").Trim();
        var references = GitProcess.Run(directory, "for-each-ref", "--format=%(refname) %(objectname)");
        var index = GitProcess.Run(directory, "-c", "core.quotepath=false", "ls-files", "--stage");
        return new SourceSnapshot(
            head,
            headReference,
            references.Replace("\r\n", "\n"),
            index.Replace("\r\n", "\n"),
            HashFiles(directory));
    }

    private static string HashFiles(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var lines = Directory.EnumerateFiles(root, "*", EveryEntry)
            .Select(path => (Relative: RelativeName(root, path), Path: path))
            .Where(file => file.Relative != ".git" && !file.Relative.StartsWith(".git/", StringComparison.Ordinal))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => $"{file.Relative}\0{ContentHash(file.Path)}")
            .ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static string RelativeName(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string ContentHash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
