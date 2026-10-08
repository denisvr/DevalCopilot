using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The narrow Windows-only physical effect boundary for ADR-0029.  It retains the exact handles it proved, uses no-replace
/// rename and disposition by handle, and never tries to recover an ownership capability after those handles are lost.  The
/// caller persists observations between phases; this type performs no database I/O and disposal only closes handles.
/// </summary>
internal sealed class WindowsIndexEffectHandles : IDisposable
{
    internal const long MaximumIndexBytes = 16L * 1024 * 1024;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint Delete = 0x00010000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint CreateNew = 1;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileRenameInfo = 3;
    private const int FileDispositionInfo = 4;

    internal const long MaximumHeadBytes = 1024;

    private readonly SafeFileHandle directory;
    private readonly HeldFile head;
    private readonly HeldFile preimage;
    private readonly HeldFile preparedArtifact;
    private readonly HeldFile indexLock;
    private readonly string administrativeDirectory;
    private bool disposed;

    /// <summary>Invoked once between the quarantine rename and the publish rename of <see cref="TryPromote"/>; test-only.</summary>
    internal Action? BetweenEffects { get; set; }

    internal string LastFailure { get; private set; } = "unattempted";
    internal int LastNativeError { get; private set; }

    internal sealed record Receipt(
        string AdministrativeDirectoryIdentity,
        string PreimageIdentity,
        long PreimageLength,
        string PreparedArtifactIdentity,
        long PreparedArtifactLength,
        string LockIdentity,
        long LockLength,
        string LockName);

    /// <summary>The exact ABI payload passed to <c>SetFileInformationByHandle(FileRenameInfo)</c>. The filename byte count
    /// deliberately excludes the trailing UTF-16 NUL, while <see cref="Bytes"/> includes it.</summary>
    internal sealed record RenameBuffer(byte[] Bytes, uint FileNameLength);

    private sealed record HeldFile(SafeFileHandle Handle, string Identity, long Length, string Sha256);

    private WindowsIndexEffectHandles(
        SafeFileHandle directory,
        HeldFile head,
        HeldFile preimage,
        HeldFile preparedArtifact,
        HeldFile indexLock,
        string administrativeDirectory)
    {
        this.directory = directory;
        this.head = head;
        this.preimage = preimage;
        this.preparedArtifact = preparedArtifact;
        this.indexLock = indexLock;
        this.administrativeDirectory = administrativeDirectory;
    }

    /// <summary>The exact bytes Git writes for a symbolic HEAD that names <paramref name="branchName"/>.</summary>
    internal static byte[] ExpectedHeadBytes(string branchName) => Encoding.UTF8.GetBytes($"ref: refs/heads/{branchName}\n");

    internal static bool TryAcquire(
        string administrativeDirectory,
        string artifactPath,
        string expectedPreimageSha256,
        string expectedPreparedSha256,
        string branchName,
        out WindowsIndexEffectHandles? acquired,
        out Receipt? receipt)
    {
        acquired = null;
        receipt = null;
        SafeFileHandle? directory = null;
        SafeFileHandle? headHandle = null;
        SafeFileHandle? preimage = null;
        SafeFileHandle? artifact = null;
        SafeFileHandle? indexLock = null;
        try
        {
            var normalizedDirectory = Path.GetFullPath(administrativeDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var indexPath = Path.Combine(normalizedDirectory, "index");
            var lockPath = indexPath + ".lock";
            // Match the qualified sharing mode: the directory handle proves its physical identity and prevents a namespace
            // replacement without requesting DELETE access that can itself conflict with a same-directory rename.
            directory = Open(normalizedDirectory, GenericRead, ShareRead | ShareWrite, OpenExisting, FileFlagBackupSemantics);
            var directoryFacts = WindowsHandleFileFacts.TryGet(directory);
            if (directoryFacts is null || !directoryFacts.Value.Attributes.HasFlag(FileAttributes.Directory))
            {
                return false;
            }

            // HEAD is held read-only with write/delete denied to every other opener, so Git's own lock-and-rename rewrite of a
            // symbolic HEAD (and any raw write) fails while the operation is live. Its exact literal binding is proven here and
            // from the same handle again around each effect; it is a live protection, never a restart ownership claim.
            headHandle = Open(Path.Combine(normalizedDirectory, "HEAD"), GenericRead, ShareRead, OpenExisting, FileFlagOpenReparsePoint);
            var heldHead = ReadHeld(headHandle, MaximumHeadBytes);
            if (heldHead is null || !heldHead.Bytes.AsSpan().SequenceEqual(ExpectedHeadBytes(branchName)))
            {
                return false;
            }

            preimage = Open(indexPath, GenericRead | Delete, ShareRead, OpenExisting, FileFlagOpenReparsePoint);
            artifact = Open(artifactPath, GenericRead, ShareRead, OpenExisting, FileFlagOpenReparsePoint);
            var heldPreimage = ReadHeld(preimage);
            var heldArtifact = ReadHeld(artifact);
            if (heldPreimage is null || heldArtifact is null
                || !string.Equals(heldPreimage.Sha256, expectedPreimageSha256, StringComparison.Ordinal)
                || !string.Equals(heldArtifact.Sha256, expectedPreparedSha256, StringComparison.Ordinal))
            {
                return false;
            }

            indexLock = Open(lockPath, GenericRead | GenericWrite | Delete, ShareRead, CreateNew, FileFlagOpenReparsePoint);
            if (!WriteAll(indexLock, heldArtifact.Bytes) || !FlushFileBuffers(indexLock))
            {
                return false;
            }

            var heldLock = ReadHeld(indexLock);
            if (heldLock is null || !string.Equals(heldLock.Sha256, expectedPreparedSha256, StringComparison.Ordinal))
            {
                return false;
            }

            var value = new WindowsIndexEffectHandles(
                directory,
                new HeldFile(headHandle, heldHead.Identity, heldHead.Length, heldHead.Sha256),
                new HeldFile(preimage, heldPreimage.Identity, heldPreimage.Length, heldPreimage.Sha256),
                new HeldFile(artifact, heldArtifact.Identity, heldArtifact.Length, heldArtifact.Sha256),
                new HeldFile(indexLock, heldLock.Identity, heldLock.Length, heldLock.Sha256),
                normalizedDirectory);
            directory = null;
            headHandle = null;
            preimage = null;
            artifact = null;
            indexLock = null;
            acquired = value;
            receipt = new Receipt(
                directoryFacts.Value.PhysicalIdentity,
                heldPreimage.Identity,
                heldPreimage.Length,
                heldArtifact.Identity,
                heldArtifact.Length,
                heldLock.Identity,
                heldLock.Length,
                "index.lock");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
        finally
        {
            if (indexLock is not null)
            {
                // A partially acquired lock exists only because THIS call just created it exclusively through this live handle.
                // Remove exactly that file by handle before releasing it; a pathname is never involved.
                SetDeleteDisposition(indexLock);
            }

            directory?.Dispose();
            headHandle?.Dispose();
            preimage?.Dispose();
            artifact?.Dispose();
            indexLock?.Dispose();
        }
    }

    /// <summary>Performs the two independently durable, no-replace effects. A collision or a foreign index appearing between
    /// them returns false with the foreign destination unchanged. The held preimage may be deleted only after the lock became
    /// the index, and only through that same still-live handle.</summary>
    internal bool TryPromote(string quarantineName)
    {
        if (disposed || !IsPlainName(quarantineName)
            || !StillProven() || !IsHeldAt(preimage, "index") || !IsHeldAt(indexLock, "index.lock"))
        {
            LastFailure = "precondition";
            return false;
        }

        if (!RenameNoReplace(preimage.Handle, quarantineName))
        {
            LastFailure = "quarantine_rename";
            return false;
        }

        if (!MatchesNamedFile(preimage, quarantineName))
        {
            LastFailure = "quarantine_namespace";
            return false;
        }

        // Native success and a retained source handle do not establish the intended destination. The freshly opened named
        // handle below is required to prove that quarantine names this exact bounded preimage before the second effect.
        if (!StillProven())
        {
            LastFailure = "intermediate_proof";
            return false;
        }

        // The pair is not one atomic transaction: between the two renames the real index name is vacant. This observation seam
        // lets a test place a foreign index there; production never sets it and it grants no authority.
        BetweenEffects?.Invoke();
        if (!RenameNoReplace(indexLock.Handle, "index"))
        {
            LastFailure = "publish_rename";
            return false;
        }

        if (!StillProven() || !MatchesNamedFile(preimage, quarantineName) || !MatchesNamedFile(indexLock, "index")
            || !SetDeleteDisposition(preimage.Handle))
        {
            LastFailure = "preimage_disposition";
            return false;
        }

        LastFailure = "none";
        return true;
    }

    internal bool IsReceipt(Receipt receipt) =>
        !disposed
        && DirectoryIdentity() == receipt.AdministrativeDirectoryIdentity
        && preimage.Identity == receipt.PreimageIdentity
        && preimage.Length == receipt.PreimageLength
        && preparedArtifact.Identity == receipt.PreparedArtifactIdentity
        && preparedArtifact.Length == receipt.PreparedArtifactLength
        && indexLock.Identity == receipt.LockIdentity
        && indexLock.Length == receipt.LockLength
        && receipt.LockName == "index.lock";

    /// <summary>Fresh proof that every retained handle (HEAD, directory, preimage, prepared artifact and lock) still names the
    /// exact bounded file it was acquired as.</summary>
    internal bool Verify() => !disposed && StillProven();

    internal bool TryDeleteHeldLock() => !disposed && StillProven() && IsHeldAt(indexLock, "index.lock")
        && SetDeleteDisposition(indexLock.Handle);

    /// <summary>Copies the post-promotion index only from the still-held lock handle. This is an observation capability, not a
    /// new effect: callers use it to pin a read-only Git view before the handle is released.</summary>
    internal bool TryReadPromotedIndex(out byte[]? bytes)
    {
        bytes = null;
        // The preimage is intentionally delete-pending after a successful promotion, so only the still-live promoted-index
        // handle and administrative directory are relevant to this read-only snapshot.
        if (disposed || DirectoryIdentity() is null || !Recheck(indexLock))
        {
            return false;
        }

        var current = ReadHeld(indexLock.Handle);
        if (current is null || current.Identity != indexLock.Identity || current.Length != indexLock.Length
            || !string.Equals(current.Sha256, indexLock.Sha256, StringComparison.Ordinal))
        {
            return false;
        }

        bytes = current.Bytes;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        indexLock.Handle.Dispose();
        preparedArtifact.Handle.Dispose();
        preimage.Handle.Dispose();
        head.Handle.Dispose();
        directory.Dispose();
    }

    /// <summary>Proves from the still-live HEAD handle that the administrative HEAD is the same regular, single-name, bounded
    /// file at its namespace, byte-for-byte the literal symbolic binding to <paramref name="branchName"/>.</summary>
    internal bool IsHeadBoundTo(string branchName)
    {
        if (disposed || !IsHeldAt(head, "HEAD") || DirectoryIdentity() is null)
        {
            return false;
        }

        var current = ReadHeld(head.Handle, MaximumHeadBytes);
        return current is not null && current.Identity == head.Identity && current.Length == head.Length
            && current.Bytes.AsSpan().SequenceEqual(ExpectedHeadBytes(branchName));
    }

    private bool StillProven() =>
        DirectoryIdentity() is not null
        && Recheck(head)
        && Recheck(preimage)
        && Recheck(preparedArtifact)
        && Recheck(indexLock);

    private string? DirectoryIdentity() => WindowsHandleFileFacts.TryGet(directory)?.PhysicalIdentity;

    private bool IsHeldAt(HeldFile file, string name)
    {
        var current = FinalPath(file.Handle);
        return current is not null && string.Equals(
            NormalizeFinalPath(current), Path.Combine(administrativeDirectory, name), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Freshly opens the expected administrative pathname and compares the bounded bytes and physical identity to the
    /// retained acquisition handle. This proves the real namespace result; a successful native call alone is insufficient.</summary>
    private bool MatchesNamedFile(HeldFile expected, string name)
    {
        try
        {
            using var named = Open(
                Path.Combine(administrativeDirectory, name), GenericRead, ShareRead | ShareWrite | ShareDelete,
                OpenExisting, FileFlagOpenReparsePoint);
            var current = ReadHeld(named);
            return current is not null && current.Identity == expected.Identity && current.Length == expected.Length
                && string.Equals(current.Sha256, expected.Sha256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    private static bool Recheck(HeldFile file)
    {
        var current = ReadHeld(file.Handle);
        return current is not null && current.Identity == file.Identity && current.Length == file.Length
            && string.Equals(current.Sha256, file.Sha256, StringComparison.Ordinal);
    }

    private bool RenameNoReplace(SafeFileHandle source, string destinationName)
    {
        var directoryPath = FinalPath(directory);
        if (directoryPath is null || !string.Equals(
                NormalizeFinalPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar), administrativeDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RenameBuffer buffer;
        try
        {
            buffer = CreateNoReplaceRenameBuffer(Path.Combine(directoryPath, destinationName));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var error = 0;
        var renamed = TrySetRenameInformation(source, buffer, out error);
        LastNativeError = error;
        return renamed;
    }

    internal static RenameBuffer CreateNoReplaceRenameBuffer(string absoluteDestination)
    {
        if (string.IsNullOrWhiteSpace(absoluteDestination) || !Path.IsPathFullyQualified(absoluteDestination)
            || absoluteDestination.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("A fully qualified, NUL-free destination is required.", nameof(absoluteDestination));
        }

        var encoded = Encoding.Unicode.GetBytes(absoluteDestination);
        var rootOffset = IntPtr.Size;
        var lengthOffset = checked(rootOffset + IntPtr.Size);
        var nameOffset = checked(lengthOffset + sizeof(uint));
        var total = checked(nameOffset + encoded.Length + sizeof(char));
        if (encoded.Length == 0 || encoded.Length > ushort.MaxValue * 2 || total > 32768)
        {
            throw new ArgumentException("The rename destination is outside the bounded native ABI envelope.", nameof(absoluteDestination));
        }

        var bytes = new byte[total]; // Explicitly initialized, including the mandatory trailing UTF-16 NUL.
        bytes[0] = 0; // ReplaceIfExists=false.
        BitConverter.GetBytes(encoded.Length).CopyTo(bytes, lengthOffset);
        encoded.CopyTo(bytes, nameOffset);
        return new RenameBuffer(bytes, checked((uint)encoded.Length));
    }

    /// <summary>Test-visible production seam. It invokes the same native API and exact payload used by <see cref="RenameNoReplace"/>.
    /// Tests may poison the trailing bytes of a buffer built by <see cref="CreateNoReplaceRenameBuffer"/> to prove termination.</summary>
    internal static bool TrySetRenameInformation(SafeFileHandle source, RenameBuffer buffer, out int error)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var nameOffset = checked((IntPtr.Size * 2) + sizeof(uint));
        if (buffer.Bytes.Length < nameOffset + sizeof(char) || buffer.FileNameLength > buffer.Bytes.Length - nameOffset - sizeof(char)
            || buffer.FileNameLength % sizeof(char) != 0 || buffer.Bytes[^1] != 0 || buffer.Bytes[^2] != 0)
        {
            error = 87;
            return false;
        }

        var memory = Marshal.AllocHGlobal(buffer.Bytes.Length);
        try
        {
            Marshal.Copy(buffer.Bytes, 0, memory, buffer.Bytes.Length);
            var renamed = SetFileInformationByHandle(source, FileRenameInfo, memory, checked((uint)buffer.Bytes.Length));
            error = renamed ? 0 : Marshal.GetLastWin32Error();
            return renamed;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>Test-visible acquisition of the same source-handle rights used by the production no-replace rename.</summary>
    internal static SafeFileHandle OpenRenameSourceForTest(string path) =>
        Open(path, GenericRead | Delete, ShareRead, OpenExisting, FileFlagOpenReparsePoint);

    private static bool SetDeleteDisposition(SafeFileHandle source)
    {
        var memory = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(memory, 0, 1);
            return SetFileInformationByHandle(source, FileDispositionInfo, memory, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    internal static bool IsPlainName(string name) => name.Length is > 0 and <= 240
        && name == Path.GetFileName(name) && !name.Contains(Path.DirectorySeparatorChar) && !name.Contains(Path.AltDirectorySeparatorChar);

    private static SafeFileHandle Open(string path, uint access, uint share, uint creation, uint flags)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, creation, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }

    private sealed record HeldRead(string Identity, long Length, string Sha256, byte[] Bytes);

    private static HeldRead? ReadHeld(SafeFileHandle handle, long maximumBytes = MaximumIndexBytes)
    {
        var facts = WindowsHandleFileFacts.TryGet(handle);
        if (facts is null || facts.Value.Attributes.HasFlag(FileAttributes.Directory)
            || facts.Value.Attributes.HasFlag(FileAttributes.ReparsePoint) || facts.Value.LinkCount != 1
            || facts.Value.Length > (ulong)maximumBytes || !SetFilePointerEx(handle, 0, out _, 0))
        {
            return null;
        }

        return ReadHeldBytes(handle, facts.Value);
    }

    private static HeldRead? ReadHeldBytes(SafeFileHandle handle, WindowsHandleFileFacts.Facts facts)
    {
        if (!SetFilePointerEx(handle, 0, out _, 0))
        {
            return null;
        }

        var bytes = new byte[(int)facts.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var chunk = new byte[Math.Min(64 * 1024, bytes.Length - offset)];
            if (!ReadFile(handle, chunk, (uint)chunk.Length, out var read, IntPtr.Zero) || read == 0)
            {
                return null;
            }

            Buffer.BlockCopy(chunk, 0, bytes, offset, (int)read);
            offset += (int)read;
        }

        return new HeldRead(
            facts.PhysicalIdentity,
            (long)facts.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bytes);
    }

    private static bool WriteAll(SafeFileHandle handle, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(64 * 1024, bytes.Length - offset)).ToArray();
            if (!WriteFile(handle, chunk, (uint)chunk.Length, out var written, IntPtr.Zero) || written == 0)
            {
                return false;
            }

            offset += (int)written;
        }

        return true;
    }

    private static string? FinalPath(SafeFileHandle handle)
    {
        var builder = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, 0);
        return length is > 0 and < 32768 ? builder.ToString() : null;
    }

    private static string NormalizeFinalPath(string path) =>
        Path.GetFullPath(path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path[4..] : path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, uint bytesToRead, out uint bytesRead, IntPtr overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(SafeFileHandle handle, long distance, out long newFilePointer, uint moveMethod);
}
