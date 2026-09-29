using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Reads bounded text previews of untracked files for one Git capture. A preview is admitted only when
/// the file is opened as a regular file, the operating system reports that the OPEN HANDLE's final path
/// is exactly (case-sensitively) the resolved worktree root plus the Git-reported relative path (so no link, junction, or
/// swapped component can have redirected the open, and the check describes what is open rather than
/// racing a separate path check), and the bytes read hash to the same raw-content identity
/// (<c>git hash-object --no-filters</c>) that the checkpoint fingerprint uses. Nothing is read from a
/// handle whose containment is not proven. Windows is the only host with that proof; elsewhere every
/// file is omitted as <see cref="GitWorkspaceUntrackedOmission.ContainmentUnproven"/> instead of relying
/// on lexical containment. No repository text, path, or exception detail leaves this class except as the
/// returned preview.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UntrackedFilePreviewReader
{
    /// <summary>Largest file whose full bytes are read to verify its identity.</summary>
    internal const int MaxVerifiedFileBytes = 64 * 1024;

    internal const int MaxPreviewBytesPerFile = 4 * 1024;
    internal const int MaxPreviewBytesTotal = 16 * 1024;

    /// <summary>A file is not previewed once fewer than this many aggregate bytes remain.</summary>
    private const int MinRemainingPreviewBytes = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static IReadOnlyList<GitWorkspaceUntrackedFile> ReadAll(
        string workspacePath, IEnumerable<(string Path, string Hash)> untrackedFiles)
    {
        var ordered = untrackedFiles.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var rootFinalPath = WindowsFinalPathResolver.TryResolveDirectoryFinalPath(workspacePath)?.TrimEnd('\\');
        var results = new List<GitWorkspaceUntrackedFile>(ordered.Length);
        var usedBytes = 0;

        foreach (var (path, hash) in ordered)
        {
            if (rootFinalPath is null)
            {
                results.Add(Omitted(path, GitWorkspaceUntrackedOmission.ContainmentUnproven));
                continue;
            }

            if (MaxPreviewBytesTotal - usedBytes < MinRemainingPreviewBytes)
            {
                results.Add(Omitted(path, GitWorkspaceUntrackedOmission.AggregateLimit));
                continue;
            }

            var file = ReadOne(workspacePath, rootFinalPath, path, hash, MaxPreviewBytesTotal - usedBytes);
            results.Add(file);
            usedBytes += file.Text is null ? 0 : Encoding.UTF8.GetByteCount(file.Text);
        }

        return results;
    }

    private static GitWorkspaceUntrackedFile ReadOne(
        string workspacePath, string rootFinalPath, string relativePath, string expectedGitHash, int remainingBudget)
    {
        if (relativePath.EndsWith('/'))
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.NotRegularFile);
        }

        if (!IsPlainRelativePath(relativePath))
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.ContainmentUnproven);
        }

        var fullPath = Path.Combine(workspacePath, relativePath.Replace('/', '\\'));
        if (Directory.Exists(fullPath))
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.NotRegularFile);
        }

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.Unreadable);
        }

        using (handle)
        {
            var finalPath = WindowsFinalPathResolver.TryResolveFinalPath(handle);
            var expectedRelative = relativePath.Replace('/', '\\');
            if (finalPath is null
                // Ordinal, not ignore-case: the root and the handle are resolved by the same operating-system
                // call, so the same directory always spells identically, while a case-sensitive parent can
                // hold a distinct sibling that differs only by case.
                || !finalPath.StartsWith(rootFinalPath + "\\", StringComparison.Ordinal)
                || !string.Equals(finalPath[(rootFinalPath.Length + 1)..], expectedRelative, StringComparison.Ordinal))
            {
                return Omitted(relativePath, GitWorkspaceUntrackedOmission.ContainmentUnproven);
            }

            try
            {
                return ReadVerified(handle, relativePath, expectedGitHash, remainingBudget);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException)
            {
                return Omitted(relativePath, GitWorkspaceUntrackedOmission.Unreadable);
            }
        }
    }

    private static GitWorkspaceUntrackedFile ReadVerified(
        SafeFileHandle handle, string relativePath, string expectedGitHash, int remainingBudget)
    {
        var length = RandomAccess.GetLength(handle);
        if (length > MaxVerifiedFileBytes)
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.TooLarge, length);
        }

        var buffer = new byte[length];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = RandomAccess.Read(handle, buffer.AsSpan(total), total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total != buffer.Length
            || RandomAccess.GetLength(handle) != length
            || !string.Equals(GitBlobSha1(buffer), expectedGitHash, StringComparison.OrdinalIgnoreCase))
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.ContentIdentityMismatch, length);
        }

        if (Array.IndexOf(buffer, (byte)0) >= 0)
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.Binary, length);
        }

        try
        {
            _ = StrictUtf8.GetCharCount(buffer);
        }
        catch (DecoderFallbackException)
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.InvalidUtf8, length);
        }

        var limit = Math.Min(MaxPreviewBytesPerFile, remainingBudget);
        var cut = buffer.Length;
        if (cut > limit)
        {
            cut = limit;
            while (cut > 0 && (buffer[cut] & 0xC0) == 0x80)
            {
                cut--;
            }
        }

        return new GitWorkspaceUntrackedFile(
            relativePath, null, length, StrictUtf8.GetString(buffer, 0, cut), cut == buffer.Length);
    }

    /// <summary>Git's blob object id of <paramref name="content"/> in a SHA-1 repository, which is what
    /// <c>hash-object --no-filters</c> prints and the reader already requires to be 40 hex characters.</summary>
    private static string GitBlobSha1(byte[] content)
    {
#pragma warning disable CA5350 // Git's SHA-1 object identity; used to match it, never for security.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
        hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Git reports forward-slash paths relative to the worktree root. Anything that could name
    /// something else (rooted, drive or stream syntax, dot segments, empty segments) is refused unopened.</summary>
    private static bool IsPlainRelativePath(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\') || path.Contains(':') || path.Contains('\0'))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static GitWorkspaceUntrackedFile Omitted(
        string path, GitWorkspaceUntrackedOmission omission, long? sizeBytes = null) =>
        new(path, omission, sizeBytes, null, false);
}
