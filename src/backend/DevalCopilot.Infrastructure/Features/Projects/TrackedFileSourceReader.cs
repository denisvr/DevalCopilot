using System.Runtime.Versioning;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Reads the CURRENT bytes of one tracked changed path for new Agent delivery (ADR-0024), in phases on ONE held handle.
/// <see cref="Acquire"/> opens the path and, before any length or byte is read, proves that the open handle's final path is
/// exactly (case-sensitively) the resolved worktree root plus the Git-reported relative path and that the handle is a regular,
/// non-device, non-reparse file with exactly ONE name (the facts come from the handle, never from a pathname); only then is the
/// length bounded and the complete bytes read, and the same proof is asked again on the handle after the read. The caller hashes
/// exactly those bytes through Git's standard input and then calls <see cref="Verify"/>, which rereads the same handle and proves
/// it once more. A deletion is claimed only by <see cref="ProveAbsent"/>: the file's immediate parent must be proven physically
/// the owned directory before and after the failed open, and an access failure is never absence. Nothing is read from a handle
/// whose containment is not proven, no repository pathname is ever given to Git for these bytes, and Windows is the only host with
/// the proof. No repository text, path or exception detail leaves this class except as the returned bytes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TrackedFileSourceReader
{
    internal enum SourceKind
    {
        /// <summary>Proven, bounded bytes with the handle held open.</summary>
        Proven,

        /// <summary>Nothing may be delivered; <see cref="Source.Omission"/> says why.</summary>
        Refused,

        /// <summary>The path could not be opened because it does not exist (not a proof of absence).</summary>
        NotFound,

        /// <summary>The bytes changed while they were being read or verified.</summary>
        Changed,
    }

    internal sealed class Source : IDisposable
    {
        private readonly SafeFileHandle? _handle;

        private Source(SourceKind kind, GitWorkspaceTrackedOmission? omission, SafeFileHandle? handle, byte[]? bytes)
        {
            Kind = kind;
            Omission = omission;
            _handle = handle;
            Bytes = bytes;
        }

        public SourceKind Kind { get; }

        public GitWorkspaceTrackedOmission? Omission { get; }

        /// <summary>The complete bytes of a proven source; null unless <see cref="Kind"/> is <see cref="SourceKind.Proven"/>.</summary>
        public byte[]? Bytes { get; }

        internal SafeFileHandle Handle => _handle!;

        internal static Source Refused(GitWorkspaceTrackedOmission omission) => new(SourceKind.Refused, omission, null, null);

        internal static Source NotFound() => new(SourceKind.NotFound, null, null, null);

        internal static Source Changed() => new(SourceKind.Changed, null, null, null);

        internal static Source Proven(SafeFileHandle handle, byte[] bytes) => new(SourceKind.Proven, null, handle, bytes);

        public void Dispose() => _handle?.Dispose();
    }

    internal enum Absence
    {
        Proven,
        Present,
        NotRegularFile,
        Unproven,
        Unreadable,
    }

    internal static Source Acquire(
        string workspacePath,
        string rootFinalPath,
        string relativePath,
        Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> readHandleFacts)
    {
        if (!UntrackedFilePreviewReader.IsPlainRelativePath(relativePath))
        {
            return Source.Refused(GitWorkspaceTrackedOmission.ContainmentUnproven);
        }

        var fullPath = Path.Combine(workspacePath, relativePath.Replace('/', '\\'));
        if (Directory.Exists(fullPath))
        {
            return Source.Refused(GitWorkspaceTrackedOmission.NotRegularFile);
        }

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Source.NotFound();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return Source.Refused(GitWorkspaceTrackedOmission.Unreadable);
        }

        try
        {
            // Before any byte or length is read.
            if (Prove(handle, rootFinalPath, relativePath, readHandleFacts) is { } refusal)
            {
                handle.Dispose();
                return Source.Refused(refusal);
            }

            var length = RandomAccess.GetLength(handle);
            if (length > GitWorkspaceTrackedFile.MaxSourceBytes)
            {
                handle.Dispose();
                return Source.Refused(GitWorkspaceTrackedOmission.TooLarge);
            }

            var bytes = ReadAll(handle, length);
            if (bytes is null)
            {
                handle.Dispose();
                return Source.Changed();
            }

            // The same held handle, asked again after the bounded read: a second name that appeared meanwhile, or facts the
            // system can no longer give, discard the source whatever the read concluded.
            if (Prove(handle, rootFinalPath, relativePath, readHandleFacts) is { } lateRefusal)
            {
                handle.Dispose();
                return Source.Refused(lateRefusal);
            }

            return Source.Proven(handle, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            handle.Dispose();
            return Source.Refused(GitWorkspaceTrackedOmission.Unreadable);
        }
    }

    /// <summary>Completes a proven source after the independent identity operation: the same handle is read again (a rewrite or
    /// substitution between the proof and the identity is a change) and proven once more. Null means the source holds.</summary>
    internal static Source? Verify(
        Source source,
        string rootFinalPath,
        string relativePath,
        Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> readHandleFacts)
    {
        try
        {
            var again = ReadAll(source.Handle, source.Bytes!.Length);
            if (again is null || !again.AsSpan().SequenceEqual(source.Bytes))
            {
                return Source.Changed();
            }

            return Prove(source.Handle, rootFinalPath, relativePath, readHandleFacts) is { } refusal ? Source.Refused(refusal) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Source.Refused(GitWorkspaceTrackedOmission.Unreadable);
        }
    }

    /// <summary>Proves a deletion: the path does not open, no directory or file stands there, and its immediate parent is
    /// physically the owned directory both before and after that failed open. An access failure is never absence.</summary>
    internal static Absence ProveAbsent(string workspacePath, string rootFinalPath, string relativePath)
    {
        if (!UntrackedFilePreviewReader.IsPlainRelativePath(relativePath))
        {
            return Absence.Unproven;
        }

        var segments = relativePath.Split('/');
        var parentRelative = string.Join('\\', segments.Take(segments.Length - 1));
        var expectedParent = parentRelative.Length == 0 ? rootFinalPath : rootFinalPath + "\\" + parentRelative;
        var parentPath = parentRelative.Length == 0 ? workspacePath : Path.Combine(workspacePath, parentRelative);
        var fullPath = Path.Combine(workspacePath, relativePath.Replace('/', '\\'));

        if (!ParentIsOwned(parentPath, expectedParent))
        {
            return Absence.Unproven;
        }

        if (Directory.Exists(fullPath))
        {
            return Absence.NotRegularFile;
        }

        try
        {
            using var handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // The exact reported name opens: the file appeared under the capture. Any other spelling (a case variant or a link)
            // is a stable state this host cannot prove either way.
            return string.Equals(
                WindowsFinalPathResolver.TryResolveFinalPath(handle),
                rootFinalPath + "\\" + relativePath.Replace('/', '\\'),
                StringComparison.Ordinal)
                ? Absence.Present
                : Absence.Unproven;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return Absence.Unreadable;
        }

        return ParentIsOwned(parentPath, expectedParent) ? Absence.Proven : Absence.Unproven;
    }

    private static bool ParentIsOwned(string parentPath, string expectedParentFinalPath) =>
        string.Equals(
            WindowsFinalPathResolver.TryResolveDirectoryFinalPath(parentPath)?.TrimEnd('\\'),
            expectedParentFinalPath,
            StringComparison.Ordinal);

    /// <summary>The open handle must be exactly the reported name below the resolved root and a plain single-name regular file.
    /// Unavailable facts, a second name or a reparse point leave containment unproven; a directory or device is not regular.</summary>
    private static GitWorkspaceTrackedOmission? Prove(
        SafeFileHandle handle,
        string rootFinalPath,
        string relativePath,
        Func<SafeFileHandle, WindowsHandleFileFacts.Facts?> readHandleFacts)
    {
        // Ordinal, not ignore-case: the root and the handle are resolved by the same operating-system call, so the same
        // directory always spells identically, while a case-sensitive parent can hold a distinct sibling that differs only by
        // case, and a differently named link target is exactly the substitution this check exists to refuse.
        var finalPath = WindowsFinalPathResolver.TryResolveFinalPath(handle);
        var expectedRelative = relativePath.Replace('/', '\\');
        if (finalPath is null
            || !finalPath.StartsWith(rootFinalPath + "\\", StringComparison.Ordinal)
            || !string.Equals(finalPath[(rootFinalPath.Length + 1)..], expectedRelative, StringComparison.Ordinal))
        {
            return GitWorkspaceTrackedOmission.ContainmentUnproven;
        }

        var facts = readHandleFacts(handle);
        if (facts is null || facts.Value.LinkCount != 1 || facts.Value.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return GitWorkspaceTrackedOmission.ContainmentUnproven;
        }

        return facts.Value.Attributes.HasFlag(FileAttributes.Directory) || facts.Value.Attributes.HasFlag(FileAttributes.Device)
            ? GitWorkspaceTrackedOmission.NotRegularFile
            : null;
    }

    /// <summary>Exactly <paramref name="length"/> bytes from offset zero, or null when the file is shorter or longer now.</summary>
    private static byte[]? ReadAll(SafeFileHandle handle, long length)
    {
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

        return total == buffer.Length && RandomAccess.GetLength(handle) == length ? buffer : null;
    }
}
