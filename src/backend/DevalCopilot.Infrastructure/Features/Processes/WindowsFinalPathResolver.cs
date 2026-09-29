using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Narrow Windows-only interop helper used by <see cref="FilesystemArtifactStore"/> and the untracked-file preview reader to
/// prove the real, fully reparse-point-resolved identity an ALREADY-OPEN file or directory handle
/// refers to — the same canonicalization Windows itself performs when following a symlink or
/// junction chain (<c>GetFinalPathNameByHandleW</c>). This exists because lexical path comparison
/// (<see cref="Path.GetFullPath"/>) proves only what a path STRING implies, never what the
/// operating system will physically open: a junction or symbolic link anywhere on the chain — the
/// artifact root itself, an intermediate directory, or the sealed file's own final component — can
/// make <c>CreateFile</c> silently follow it to a completely different, uncontrolled location.
/// <c>GetFinalPathNameByHandleW</c> is queried against a handle the caller already opened (or, for
/// the artifact root, a short-lived directory handle opened here only to ask this one question),
/// never used to decide how to open something — it can only describe what is already open, which
/// is what makes it safe against a swap happening in between a check and a later, separate open.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsFinalPathResolver
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>Comfortably larger than any realistic resolved path; a longer result triggers one
    /// bounded retry with a buffer sized exactly to what the OS itself reports needing.</summary>
    private const int InitialBufferLength = 1024;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    /// <summary>
    /// Resolves the fully reparse-resolved real path an already-open file (or directory) handle
    /// refers to. Returns <see langword="null"/> if the OS call fails for any reason — callers
    /// must treat that as "this handle's real identity cannot be proven," never as an empty or
    /// root path that would trivially satisfy a later prefix comparison.
    /// </summary>
    internal static string? TryResolveFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[InitialBufferLength];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0)
        {
            return null;
        }

        if (length >= buffer.Length)
        {
            // The OS reports the exact required length (including the null terminator) when the
            // supplied buffer is too small; retried exactly once with a buffer sized to that.
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length)
            {
                return null;
            }
        }

        return new string(buffer, 0, (int)length);
    }

    /// <summary>
    /// Resolves the fully reparse-resolved real path of an existing directory — opening a
    /// short-lived handle with the backup-semantics flag Windows requires to open a directory
    /// through <c>CreateFile</c> at all (a plain file-open call fails for a directory path), and
    /// releasing it immediately afterward; this handle is never used for anything but asking this
    /// one question. Returns <see langword="null"/> if the directory cannot be opened or resolved,
    /// including because it does not exist.
    /// </summary>
    internal static string? TryResolveDirectoryFinalPath(string directoryPath)
    {
        using var handle = CreateFileW(
            directoryPath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        return handle.IsInvalid ? null : TryResolveFinalPath(handle);
    }
}
