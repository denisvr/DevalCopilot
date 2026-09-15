using System.Runtime.InteropServices;
using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Resolves a directory's Windows physical identity — volume serial number plus 128-bit file
/// ID — via <c>CreateFileW</c> (with <c>FILE_FLAG_BACKUP_SEMANTICS</c>, required to open a
/// directory handle at all) followed by <c>GetFileInformationByHandleEx</c>'s
/// <c>FileIdInfo</c> class. No BCL API exposes this for directories, so this is the one place
/// in the codebase that P/Invokes it — deliberately isolated here, exactly the seam ADR-0007
/// reserved. <c>CreateFileW</c> resolves through the real NT object manager, so a <c>subst</c>
/// drive mapping or an ancestor junction/symlink in <paramref name="candidate"/>'s path is
/// followed transparently: two lexically different paths reaching the same physical directory
/// resolve to the identical tuple. Only NTFS and ReFS support <c>FileIdInfo</c>; any other
/// filesystem is reported as <see cref="RepositoryPhysicalIdentityInspectionOutcome.UnsupportedFilesystem"/>,
/// never a fabricated identity. No exception, Win32 error code, or path ever escapes this type —
/// only the closed outcome enum.
/// </summary>
public sealed class RepositoryPhysicalIdentityInspector : IRepositoryPhysicalIdentityInspector
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x0001;
    private const uint FileShareWrite = 0x0002;
    private const uint FileShareDelete = 0x0004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x0200_0000;

    /// <summary>The value <c>FILE_INFO_BY_HANDLE_CLASS.FileIdInfo</c> takes in the Windows SDK
    /// (<c>winbase.h</c>) — fixed and documented, never derived at runtime.</summary>
    private const int FileIdInfoClass = 18;

    /// <summary><c>GetFileInformationByHandleEx</c>'s documented <c>ERROR_INVALID_FUNCTION</c>
    /// result when the underlying filesystem does not support the requested information
    /// class — the empirical signal a non-NTFS/ReFS volume produces here.</summary>
    private const int ErrorInvalidFunction = 1;

    public RepositoryPhysicalIdentityInspectionResult Resolve(RepositoryRootCandidate candidate)
    {
        using var handle = CreateFileW(
            candidate.CanonicalPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return Unavailable(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible);
        }

        var bufferSize = Marshal.SizeOf<FileIdInfo>();
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var succeeded = GetFileInformationByHandleEx(handle, FileIdInfoClass, buffer, (uint)bufferSize);
            if (!succeeded)
            {
                var outcome = Marshal.GetLastWin32Error() == ErrorInvalidFunction
                    ? RepositoryPhysicalIdentityInspectionOutcome.UnsupportedFilesystem
                    : RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible;
                return Unavailable(outcome);
            }

            var info = Marshal.PtrToStructure<FileIdInfo>(buffer);
            return new RepositoryPhysicalIdentityInspectionResult(
                RepositoryPhysicalIdentityInspectionOutcome.Resolved, info.VolumeSerialNumber, info.FileId);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static RepositoryPhysicalIdentityInspectionResult Unavailable(RepositoryPhysicalIdentityInspectionOutcome outcome) =>
        new(outcome, null, null);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] FileId;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile, int fileInformationClass, IntPtr lpFileInformation, uint dwBufferSize);
}
