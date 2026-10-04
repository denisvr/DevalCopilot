using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// The attributes and hard-link count of an ALREADY-OPEN file handle (<c>GetFileInformationByHandle</c>), asked of the
/// handle itself rather than of a path so a later swap cannot change the answer. A reader that has proven a handle's
/// final path uses this to also refuse anything that is not a plain single-name file: a directory, a reparse point,
/// a device, or a file that another name (possibly outside the approved root) also reaches.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsHandleFileFacts
{
    internal readonly record struct Facts(FileAttributes Attributes, uint LinkCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    /// <summary>Returns <see langword="null"/> when the operating system cannot describe the handle; callers must
    /// treat that as "not proven a plain file".</summary>
    internal static Facts? TryGet(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var information)
            ? new Facts((FileAttributes)information.FileAttributes, information.NumberOfLinks)
            : null;
}
