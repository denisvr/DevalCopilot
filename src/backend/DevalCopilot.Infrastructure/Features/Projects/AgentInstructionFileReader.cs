using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using Microsoft.Win32.SafeHandles;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Reads one fixed root instruction file (<c>AGENTS.md</c> or <c>CLAUDE.md</c>) for Agent context in two phases on ONE held
/// handle. <see cref="Acquire"/> admits a file only when it is opened as a regular, single-name, non-reparse file, the
/// operating system reports that the OPEN HANDLE's final path is exactly (case-sensitively) the resolved worktree root plus
/// the fixed name, and its length is within the source bound; only then are its complete bytes read. The independent identity
/// operation consumes those bytes alone (Git hashes them on standard input; no repository pathname is ever given to Git for
/// this purpose), and <see cref="Verify"/> then re-reads the same handle to prove the file did not change meanwhile and that
/// Git's raw identity, this host's own blob computation, and (for an untracked file) the checkpoint fingerprint's identity
/// agree. Nothing is read, hashed or sent anywhere from a handle whose containment is not proven, and nothing but the two
/// fixed root names is ever opened: no import, reference, link, parent, nested, home, or settings path is followed. Windows is
/// the only host with that proof; elsewhere the caller omits the file rather than relying on lexical containment. No
/// repository text, path, or exception detail leaves this class except as the returned complete text.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AgentInstructionFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The outcome of <see cref="Acquire"/>: either a final result that needs no identity operation, or a proven,
    /// bounded source whose handle is held open until <see cref="Dispose"/>.</summary>
    internal sealed class Acquisition : IDisposable
    {
        private readonly SafeFileHandle? _handle;

        private Acquisition(GitWorkspaceInstructionFile? final, SafeFileHandle? handle, byte[]? bytes)
        {
            Final = final;
            _handle = handle;
            Bytes = bytes;
        }

        /// <summary>Set when the file is already classified (absent, omitted, too large) and no bytes were read.</summary>
        public GitWorkspaceInstructionFile? Final { get; }

        /// <summary>The complete bytes of a proven source of at most the source bound; null exactly when <see cref="Final"/> is set.</summary>
        public byte[]? Bytes { get; }

        internal SafeFileHandle Handle => _handle!;

        internal static Acquisition Done(GitWorkspaceInstructionFile final) => new(final, null, null);

        internal static Acquisition Proven(SafeFileHandle handle, byte[] bytes) => new(null, handle, bytes);

        public void Dispose() => _handle?.Dispose();
    }

    /// <param name="identityCapturedAtCheckpoint">True for an untracked file: its checkpoint fingerprint captured an identity, so a
    /// path that can no longer be opened was changing underneath the capture rather than absent.</param>
    internal static Acquisition Acquire(string workspacePath, string rootFinalPath, string fileName, bool identityCapturedAtCheckpoint)
    {
        var fullPath = Path.Combine(workspacePath, fileName);
        if (Directory.Exists(fullPath))
        {
            return Acquisition.Done(Omitted(fileName, GitWorkspaceInstructionOmission.NotRegularFile));
        }

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Acquisition.Done(identityCapturedAtCheckpoint
                ? Omitted(fileName, GitWorkspaceInstructionOmission.ContentIdentityMismatch)
                : new GitWorkspaceInstructionFile(fileName, GitWorkspaceInstructionStatus.Absent, null, null, null, null));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return Acquisition.Done(Omitted(fileName, GitWorkspaceInstructionOmission.Unreadable));
        }

        try
        {
            var refusal = Prove(handle, rootFinalPath, fileName);
            if (refusal is not null)
            {
                handle.Dispose();
                return Acquisition.Done(refusal);
            }

            var length = RandomAccess.GetLength(handle);
            if (length > GitWorkspaceInstructionContext.MaxSourceBytes)
            {
                handle.Dispose();
                return Acquisition.Done(new GitWorkspaceInstructionFile(
                    fileName, GitWorkspaceInstructionStatus.Omitted, GitWorkspaceInstructionOmission.TooLarge, length, null, null));
            }

            var bytes = ReadAll(handle, length);
            if (bytes is null)
            {
                handle.Dispose();
                return Acquisition.Done(Omitted(fileName, GitWorkspaceInstructionOmission.ContentIdentityMismatch));
            }

            return Acquisition.Proven(handle, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            handle.Dispose();
            return Acquisition.Done(Omitted(fileName, GitWorkspaceInstructionOmission.Unreadable));
        }
    }

    /// <summary>Completes a proven source after the independent identity operation. <paramref name="gitIdentity"/> is what Git
    /// printed for exactly <see cref="Acquisition.Bytes"/> on standard input (null when it could not); the checkpoint identity
    /// is required for an untracked file and ignored for a tracked one.</summary>
    internal static GitWorkspaceInstructionFile Verify(
        Acquisition source, string fileName, string? gitIdentity, bool untracked, string? checkpointIdentity)
    {
        var bytes = source.Bytes!;
        try
        {
            // The same handle, read again: a file substituted or rewritten between the proof and the identity is refused.
            var again = ReadAll(source.Handle, bytes.Length);
            if (again is null || !again.AsSpan().SequenceEqual(bytes))
            {
                return Omitted(fileName, GitWorkspaceInstructionOmission.ContentIdentityMismatch);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Omitted(fileName, GitWorkspaceInstructionOmission.Unreadable);
        }

        var local = UntrackedFilePreviewReader.GitBlobSha1(bytes);
        if (gitIdentity is null
            || !string.Equals(local, gitIdentity, StringComparison.OrdinalIgnoreCase)
            || (untracked && !string.Equals(local, checkpointIdentity, StringComparison.OrdinalIgnoreCase)))
        {
            return Omitted(fileName, GitWorkspaceInstructionOmission.ContentIdentityMismatch);
        }

        // From here the bytes are the complete, identity-verified file, so their length and SHA-256 are established
        // facts even when the content turns out not to be admissible text.
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return OmittedVerified(fileName, GitWorkspaceInstructionOmission.Binary, bytes.Length, sha256);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return OmittedVerified(fileName, GitWorkspaceInstructionOmission.InvalidUtf8, bytes.Length, sha256);
        }

        return new GitWorkspaceInstructionFile(fileName, GitWorkspaceInstructionStatus.Complete, null, bytes.Length, sha256, text);
    }

    /// <summary>The fixed result when a file cannot be examined at all (for example a host without a containment proof).</summary>
    internal static GitWorkspaceInstructionFile Omitted(string fileName, GitWorkspaceInstructionOmission omission) =>
        new(fileName, GitWorkspaceInstructionStatus.Omitted, omission, null, null, null);

    /// <summary>The open handle must be exactly the fixed name below the resolved root and a plain single-name regular file.</summary>
    private static GitWorkspaceInstructionFile? Prove(SafeFileHandle handle, string rootFinalPath, string fileName)
    {
        // Ordinal, not ignore-case: the root and the handle are resolved by the same operating-system call, so the same
        // directory always spells identically, while a case-insensitive spelling of the name (or a differently named
        // link target) is exactly the substitution this check exists to refuse.
        var finalPath = WindowsFinalPathResolver.TryResolveFinalPath(handle);
        var belowRoot = finalPath is not null && finalPath.StartsWith(rootFinalPath + "\\", StringComparison.Ordinal);
        var exactName = belowRoot && string.Equals(finalPath![(rootFinalPath.Length + 1)..], fileName, StringComparison.Ordinal);
        if (!exactName)
        {
            return Omitted(fileName, GitWorkspaceInstructionOmission.ContainmentUnproven);
        }

        var facts = WindowsHandleFileFacts.TryGet(handle);
        if (facts is null || facts.Value.LinkCount != 1 || facts.Value.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return Omitted(fileName, GitWorkspaceInstructionOmission.ContainmentUnproven);
        }

        return facts.Value.Attributes.HasFlag(FileAttributes.Directory) || facts.Value.Attributes.HasFlag(FileAttributes.Device)
            ? Omitted(fileName, GitWorkspaceInstructionOmission.NotRegularFile)
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

    private static GitWorkspaceInstructionFile OmittedVerified(
        string fileName, GitWorkspaceInstructionOmission omission, long length, string sha256) =>
        new(fileName, GitWorkspaceInstructionStatus.Omitted, omission, length, sha256, null);
}
