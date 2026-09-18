using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Processes.Ports;

/// <summary>
/// Owns the filesystem lifecycle of process-output artifacts beneath an application-owned
/// artifact root — never an arbitrary project path. The host is the only writer of a capture
/// file; a child process only ever owns its own stdout/stderr pipes, never this sink.
///
/// Lifecycle: a capture is written to a deterministic "<c>.partial</c>" path while active. Once
/// the host has finished writing (normal completion, or restart reconciliation recovering a
/// crash), it is sealed: renamed, once, to a deterministic "<c>.sealed</c>" path and hashed. A
/// sealed file is immutable and is the file an <see cref="Artifact"/> row references permanently
/// — it is never renamed again, including after the database commit that records it.
/// </summary>
public interface IArtifactStore
{
    /// <summary>Absolute path of the in-progress capture file for one attempt/purpose. Callers
    /// write to this path only while capture is active.</summary>
    string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>The path an <see cref="Artifact"/> row stores — relative to the artifact root,
    /// deterministic from the same three identifiers. Never derived from user or agent input.</summary>
    string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>
    /// Seals the partial file (renames it to its deterministic sealed path) and computes its
    /// final length and SHA-256 hash — entirely outside any database transaction, and before
    /// the short transaction that records the resulting <see cref="Artifact"/> row. Returns
    /// <see langword="null"/> if no partial file exists, or if the seal itself fails (never
    /// throws for either case — the caller decides how to log and proceed).
    /// </summary>
    Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken);

    /// <summary>True if a sealed file already exists at the deterministic location — used by
    /// restart recovery to detect a seal that completed but was never recorded.</summary>
    bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>True if a partial (not yet sealed) file exists.</summary>
    bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>
    /// Computes the length and hash of an already-sealed file at its deterministic location,
    /// without renaming it again. Used by restart recovery to import a file that was sealed in
    /// a prior host session but never recorded.
    /// </summary>
    Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an orphaned partial file — the one narrow cleanup this store performs. Callers
    /// must only invoke this for a partial file proven to be non-evidence (its owning attempt is
    /// already terminal through some other path and no artifact will ever reference it); this
    /// method itself performs no such check.
    /// </summary>
    void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>
    /// Deletes an orphaned <em>sealed</em> file — for the narrow case where sealing itself
    /// already succeeded but the database write that was meant to record it lost a race (e.g. a
    /// concurrency conflict on a unique constraint) and will never happen. Callers must only
    /// invoke this when certain no <see cref="Artifact"/> row will ever reference this file; this
    /// method itself performs no such check. Best-effort: a failed delete leaves harmless
    /// orphaned bytes behind, never data loss, since nothing durable ever references this file.
    /// </summary>
    void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose);

    /// <summary>
    /// Reads a bounded window from the in-progress (not yet sealed) capture file, tolerant of
    /// the host concurrently appending to it. Used only while the owning attempt is still
    /// <c>Running</c>.
    /// </summary>
    Task<PartialReadWindow> ReadPartialAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves <paramref name="relativeStoragePath"/> under the artifact root with a canonical
    /// containment check — the database value is never trusted blindly — then verifies the
    /// file's actual length and SHA-256 against <paramref name="expectedByteLength"/> and
    /// <paramref name="expectedContentHash"/> before returning any bytes. A mismatch or missing
    /// file returns an explicit non-<see cref="SealedReadStatus.Ok"/> status; it never serves
    /// content that failed verification.
    /// </summary>
    Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath,
        long expectedByteLength,
        string expectedContentHash,
        long fromOffset,
        int maxBytes,
        CancellationToken cancellationToken);
}

/// <summary>The outcome of sealing or describing a sealed file: its stored-relative path, final
/// byte length, and content hash.</summary>
public sealed record SealedOutputFile(string RelativeStoragePath, long ByteLength, string ContentHash);

/// <summary>One bounded read from an in-progress (unsealed) capture file.</summary>
public sealed record PartialReadWindow(string Text, long NextOffset, long TotalLengthSoFar);

public enum SealedReadStatus
{
    Ok,
    Missing,
    IntegrityMismatch,
}

/// <summary>One bounded, verified read from a sealed artifact file.</summary>
public sealed record SealedReadWindow(SealedReadStatus Status, string Text, long NextOffset, long TotalLengthSoFar);
