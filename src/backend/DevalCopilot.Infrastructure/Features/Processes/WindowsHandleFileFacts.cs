using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// The attributes and hard-link count of an ALREADY-OPEN file handle (<c>GetFileInformationByHandle</c>), asked of the
/// handle itself rather than of a path so a later swap cannot change the answer. A reader that has proven a handle's
/// final path uses this to also refuse anything that is not a plain single-name file: a directory, a reparse point,
/// a device, or a file that another name (possibly outside the approved root) also reaches.
/// </summary>
internal static class WindowsHandleFileFacts
{
    /// <summary>A physical identity is evidence about this already-open handle. It is deliberately not an ownership capability
    /// after the handle is closed: a later process must not adopt a pathname merely because this value was persisted.</summary>
    internal readonly record struct Facts(
        FileAttributes Attributes,
        uint LinkCount,
        ulong VolumeSerialNumber,
        string FileId128,
        ulong Length)
    {
        internal Facts(FileAttributes attributes, uint linkCount)
            : this(attributes, linkCount, 0, new string('0', 32), 0)
        {
        }

        /// <summary>The full FILE_ID_INFO tuple, not the legacy 64-bit file-index subset. It remains only an observation once
        /// the handle closes and never grants restart-time ownership.</summary>
        public string PhysicalIdentity => $"{VolumeSerialNumber:x16}:{FileId128}";
    }

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
        public uint LegacyVolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint LegacyFileIndexHigh;
        public uint LegacyFileIndexLow;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile, int fileInformationClass, byte[] fileInformation, uint bufferSize);

    /// <summary>Returns <see langword="null"/> when the operating system cannot describe the handle; callers must
    /// treat that as "not proven a plain file".</summary>
    internal static Facts? TryGet(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            return null;
        }

        // FileIdInfo is FILE_ID_INFO: an unsigned 64-bit volume serial followed by a 128-bit file identifier.
        var fileIdInfo = new byte[24];
        if (!GetFileInformationByHandleEx(handle, 18, fileIdInfo, (uint)fileIdInfo.Length))
        {
            return null;
        }

        return new Facts(
            (FileAttributes)information.FileAttributes,
            information.NumberOfLinks,
            BitConverter.ToUInt64(fileIdInfo, 0),
            Convert.ToHexString(fileIdInfo.AsSpan(8, 16)).ToLowerInvariant(),
            ((ulong)information.FileSizeHigh << 32) | information.FileSizeLow);
    }
}
