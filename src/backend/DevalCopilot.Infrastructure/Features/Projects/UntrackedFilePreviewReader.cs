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
/// racing a separate path check), the same handle is a regular, non-reparse, non-device file with exactly ONE name (a file that
/// any other name reaches, inside the worktree or not, is refused: a hard link passes the path check because the name that was
/// opened is the exact reported one), and the bytes read hash to the same raw-content identity
/// (<c>git hash-object --no-filters</c>) that the checkpoint fingerprint uses. Those facts are proven on the held handle before
/// any byte or length is read and asked again on that handle after the bounded read, before a preview is accepted. Nothing is
/// read from a handle whose containment is not proven. Windows is the only host with that proof; elsewhere every
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
        string workspacePath, IEnumerable<(string Path, string Hash)> untrackedFiles) =>
        ReadAll(workspacePath, untrackedFiles, readHandleFacts: null);

    /// <param name="readHandleFacts">Test seam at the one real boundary: the operating system's answer about an open handle,
    /// asked once before any byte is read and once after the bounded read. Production passes none and gets
    /// <see cref="WindowsHandleFileFacts.TryGet"/>.</param>
    internal static IReadOnlyList<GitWorkspaceUntrackedFile> ReadAll(
        string workspacePath,
        IEnumerable<(string Path, string Hash)> untrackedFiles,
        Func<SafeFileHandle, WindowsHandleFileFacts.Facts?>? readHandleFacts)
    {
        readHandleFacts ??= WindowsHandleFileFacts.TryGet;
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

            var file = ReadOne(workspacePath, rootFinalPath, path, hash, MaxPreviewBytesTotal - usedBytes, readHandleFacts);
            results.Add(file);
            usedBytes += file.Text is null ? 0 : Encoding.UTF8.GetByteCount(file.Text);
        }

        return results;
    }

    private static GitWorkspaceUntrackedFile ReadOne(
        string workspacePath,
        string rootFinalPath,
        string relativePath,
        string expectedGitHash,
        int remainingBudget,
        Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> readHandleFacts)
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

            // Before any byte or length is read: a regular, non-reparse, non-device file with exactly one name.
            if (RefuseUnlessSingleNameRegularFile(handle, relativePath, readHandleFacts) is { } refused)
            {
                return refused;
            }

            try
            {
                var preview = ReadVerified(handle, relativePath, expectedGitHash, remainingBudget);
                // The same held handle, asked again after the bounded read: a second name that appeared meanwhile, or facts the
                // system can no longer give, discard the preview (text and size included) whatever the read concluded.
                return RefuseUnlessSingleNameRegularFile(handle, relativePath, readHandleFacts) ?? preview;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException)
            {
                return Omitted(relativePath, GitWorkspaceUntrackedOmission.Unreadable);
            }
        }
    }

    /// <summary>The handle's own attributes and link count, never a pathname's. Unavailable facts, another name for the file or a
    /// reparse point leave containment unproven (nothing is read or kept); a directory or device is not a regular file.</summary>
    private static GitWorkspaceUntrackedFile? RefuseUnlessSingleNameRegularFile(
        SafeFileHandle handle, string relativePath, Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> readHandleFacts)
    {
        var facts = readHandleFacts(handle);
        if (facts is null || facts.Value.LinkCount != 1 || facts.Value.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return Omitted(relativePath, GitWorkspaceUntrackedOmission.ContainmentUnproven);
        }

        return facts.Value.Attributes.HasFlag(FileAttributes.Directory) || facts.Value.Attributes.HasFlag(FileAttributes.Device)
            ? Omitted(relativePath, GitWorkspaceUntrackedOmission.NotRegularFile)
            : null;
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
    internal static string GitBlobSha1(byte[] content)
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
