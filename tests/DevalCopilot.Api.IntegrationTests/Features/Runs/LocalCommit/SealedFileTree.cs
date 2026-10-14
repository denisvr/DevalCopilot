using System.Security.Cryptography;
using System.Text;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>A content hash of a directory tree: every directory and file path with each file's length and SHA-256, so a sealed scene or
/// database file can be proven unchanged and not merely still present.</summary>
internal static class SealedFileTree
{
    public static string Fingerprint(string root)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path);
            if (Directory.Exists(path))
            {
                builder.Append(relative).Append("/\n");
                continue;
            }

            builder.Append(relative).Append(':').Append(new FileInfo(path).Length).Append(':').Append(FileHash(path)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static string FileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Removes a tree this process created. Read-only attributes (sealed files, Git objects) are cleared first.</summary>
    public static void DeleteOwned(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }
}
