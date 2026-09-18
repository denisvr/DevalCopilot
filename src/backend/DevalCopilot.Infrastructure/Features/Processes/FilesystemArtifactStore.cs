using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Owns process-output capture files beneath a fixed, application-owned artifact root — never a
/// project path. A capture is written to a deterministic "<c>.partial</c>" path while active;
/// once the host is done writing it, <see cref="SealAsync"/> renames it, once, to a deterministic
/// "<c>.sealed</c>" path and that rename is never repeated — a sealed file is immutable for the
/// rest of its life. Every persisted relative path is resolved back under the root with an
/// explicit containment check; nothing read from the database is ever combined with the root
/// blindly.
/// </summary>
public sealed class FilesystemArtifactStore : IArtifactStore, IVerificationOutputArtifactStore
{
    private readonly string _root;

    public FilesystemArtifactStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "artifacts"))
    {
    }

    /// <summary>Test-only seam: a caller-chosen root instead of the real application data
    /// directory, so tests never write real artifacts to a developer's actual machine state.</summary>
    public FilesystemArtifactStore(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        Path.Combine(_root, RelativeDirectory(runId, attemptId), $"{FileStem(purpose)}.partial");

    public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        Path.Combine(RelativeDirectory(runId, attemptId), $"{FileStem(purpose)}.sealed");

    public string GetPartialPath(Guid verificationExecutionId, VerificationOutputPurpose purpose) =>
        Path.Combine(_root, VerificationRelativeDirectory(verificationExecutionId), $"{VerificationFileStem(purpose)}.partial");

    public void DeletePartialFile(Guid verificationExecutionId, VerificationOutputPurpose purpose)
    {
        try
        {
            File.Delete(GetPartialPath(verificationExecutionId, purpose));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<SealedVerificationOutputFile?> SealAsync(
        Guid verificationExecutionId,
        VerificationOutputPurpose purpose,
        CancellationToken cancellationToken)
    {
        var partialPath = GetPartialPath(verificationExecutionId, purpose);
        if (!File.Exists(partialPath))
        {
            return null;
        }

        var relativePath = Path.Combine(VerificationRelativeDirectory(verificationExecutionId), $"{VerificationFileStem(purpose)}.sealed");
        var sealedPath = Path.Combine(_root, relativePath);
        try
        {
            File.Move(partialPath, sealedPath);
            var (length, hash) = await ComputeLengthAndHashAsync(sealedPath, cancellationToken).ConfigureAwait(false);
            return new SealedVerificationOutputFile(relativePath, length, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<SealedVerificationOutputFile?> DescribeSealedFileAsync(
        Guid verificationExecutionId,
        VerificationOutputPurpose purpose,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.Combine(
            VerificationRelativeDirectory(verificationExecutionId),
            $"{VerificationFileStem(purpose)}.sealed");
        var sealedPath = Path.Combine(_root, relativePath);
        if (!File.Exists(sealedPath))
        {
            return null;
        }

        try
        {
            var (length, hash) = await ComputeLengthAndHashAsync(sealedPath, cancellationToken).ConfigureAwait(false);
            return new SealedVerificationOutputFile(relativePath, length, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        File.Exists(Path.Combine(_root, GetSealedRelativePath(runId, attemptId, purpose)));

    public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        File.Exists(GetPartialPath(runId, attemptId, purpose));

    public async Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
    {
        var partialPath = GetPartialPath(runId, attemptId, purpose);
        if (!File.Exists(partialPath))
        {
            return null;
        }

        var relativeSealedPath = GetSealedRelativePath(runId, attemptId, purpose);
        var sealedPath = Path.Combine(_root, relativeSealedPath);

        try
        {
            File.Move(partialPath, sealedPath);
            var (length, hash) = await ComputeLengthAndHashAsync(sealedPath, cancellationToken).ConfigureAwait(false);
            return new SealedOutputFile(relativeSealedPath, length, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<SealedOutputFile?> DescribeSealedFileAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
    {
        var relativeSealedPath = GetSealedRelativePath(runId, attemptId, purpose);
        var sealedPath = Path.Combine(_root, relativeSealedPath);

        if (!File.Exists(sealedPath))
        {
            return null;
        }

        try
        {
            var (length, hash) = await ComputeLengthAndHashAsync(sealedPath, cancellationToken).ConfigureAwait(false);
            return new SealedOutputFile(relativeSealedPath, length, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
    {
        var partialPath = GetPartialPath(runId, attemptId, purpose);

        try
        {
            File.Delete(partialPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed cleanup leaves harmless orphaned bytes behind, never data
            // loss — nothing durable ever referenced this file.
        }
    }

    public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
    {
        var sealedPath = Path.Combine(_root, GetSealedRelativePath(runId, attemptId, purpose));

        try
        {
            File.Delete(sealedPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed cleanup leaves harmless orphaned bytes behind, never data
            // loss — nothing durable ever referenced this file.
        }
    }

    public async Task<PartialReadWindow> ReadPartialAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken)
    {
        var partialPath = GetPartialPath(runId, attemptId, purpose);

        try
        {
            // FileShare.Delete matters here: this is a live reader of a file the host may still
            // be appending to and will eventually rename (seal). Without Delete in this share
            // mode, this open handle could block that rename on Windows.
            await using var stream = new FileStream(
                partialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // A partial file may still grow: an incomplete trailing codepoint here could be
            // completed by bytes the host hasn't written yet, so it is always held back.
            return await ReadWindowAsync(stream, fromOffset, maxBytes, mayGrowFurther: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // Nothing captured yet for this stream — including the case where no directory was
            // ever created for this attempt at all.
            return new PartialReadWindow(string.Empty, fromOffset, 0);
        }
    }

    public async Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath,
        long expectedByteLength,
        string expectedContentHash,
        long fromOffset,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveWithinRoot(relativeStoragePath);
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0);
        }

        var (actualLength, actualHash) = await ComputeLengthAndHashAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
        if (actualLength != expectedByteLength || !string.Equals(actualHash, expectedContentHash, StringComparison.Ordinal))
        {
            return new SealedReadWindow(SealedReadStatus.IntegrityMismatch, string.Empty, fromOffset, actualLength);
        }

        await using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // A sealed file is immutable and will never grow again: an incomplete trailing
        // codepoint only at the file's true end is genuinely malformed input, not a boundary
        // artifact, and is decoded leniently as such (mayGrowFurther: false). One still short
        // of the true end — because maxBytes cut this particular window early — is still held
        // back and deferred to the next read, same as the partial-file case.
        var window = await ReadWindowAsync(stream, fromOffset, maxBytes, mayGrowFurther: false, cancellationToken).ConfigureAwait(false);
        return new SealedReadWindow(SealedReadStatus.Ok, window.Text, window.NextOffset, window.TotalLengthSoFar);
    }

    /// <summary>
    /// Resolves a database-supplied relative path under the artifact root with a canonical
    /// containment check, rejecting anything that would escape it (an absolute path, or one
    /// whose ".." segments resolve outside the root) rather than trusting the stored text.
    /// </summary>
    private string? ResolveWithinRoot(string relativeStoragePath)
    {
        if (string.IsNullOrWhiteSpace(relativeStoragePath) || Path.IsPathRooted(relativeStoragePath))
        {
            return null;
        }

        var canonicalRoot = Path.TrimEndingDirectorySeparator(_root);
        string canonicalCandidate;
        try
        {
            canonicalCandidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativeStoragePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var rootWithSeparator = canonicalRoot.EndsWith(Path.DirectorySeparatorChar) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? canonicalCandidate : null;
    }

    private static async Task<(long Length, string Hash)> ComputeLengthAndHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return (stream.Length, "sha256:" + Convert.ToHexStringLower(hashBytes));
    }

    /// <summary>
    /// Reads a bounded window and never returns a byte range that splits a valid multi-byte
    /// UTF-8 codepoint across two poll responses. <paramref name="maxBytes"/> is a byte cap, not
    /// a character cap, so it can land exactly inside one; when it does, the incomplete trailing
    /// bytes are held back (excluded from this response's text and from its advertised
    /// <c>NextOffset</c>) so the very next read starts at the beginning of that same codepoint
    /// and reconstructs it whole — the underlying bytes are never skipped, only deferred by one
    /// read. <paramref name="mayGrowFurther"/> distinguishes a live, still-growing partial file
    /// (where even a trailing incomplete sequence at today's known end might still be completed
    /// by tomorrow's bytes, so it is always held back) from an immutable sealed file (where an
    /// incomplete sequence only at its true, final end is genuinely malformed input, not a
    /// boundary artifact, and is decoded leniently with the standard replacement character).
    /// </summary>
    private static async Task<PartialReadWindow> ReadWindowAsync(
        FileStream stream, long fromOffset, int maxBytes, bool mayGrowFurther, CancellationToken cancellationToken)
    {
        var totalLength = stream.Length;
        if (fromOffset >= totalLength)
        {
            return new PartialReadWindow(string.Empty, fromOffset, totalLength);
        }

        stream.Seek(fromOffset, SeekOrigin.Begin);
        var toRead = (int)Math.Min(maxBytes, totalLength - fromOffset);
        var buffer = new byte[toRead];
        var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);

        var reachedTrueEnd = !mayGrowFurther && fromOffset + read >= totalLength;
        var decodableLength = reachedTrueEnd ? read : Utf8Boundary.FindDecodableLength(buffer.AsSpan(0, read));

        return new PartialReadWindow(Encoding.UTF8.GetString(buffer, 0, decodableLength), fromOffset + decodableLength, totalLength);
    }

    private static string RelativeDirectory(Guid runId, Guid attemptId) =>
        Path.Combine("runs", runId.ToString(), "attempts", attemptId.ToString());

    private static string VerificationRelativeDirectory(Guid verificationExecutionId) =>
        Path.Combine("verifications", verificationExecutionId.ToString());

    private static string FileStem(ArtifactPurpose purpose) => purpose switch
    {
        ArtifactPurpose.ProcessStandardOutput => "stdout",
        ArtifactPurpose.ProcessStandardError => "stderr",
        ArtifactPurpose.AgentContextManifest => "context-manifest",
        ArtifactPurpose.AgentStandardOutput => "stdout",
        ArtifactPurpose.AgentStandardError => "stderr",
        ArtifactPurpose.AgentFinalResponse => "final-response",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };

    private static string VerificationFileStem(VerificationOutputPurpose purpose) => purpose switch
    {
        VerificationOutputPurpose.StandardOutput => "stdout",
        VerificationOutputPurpose.StandardError => "stderr",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };
}
