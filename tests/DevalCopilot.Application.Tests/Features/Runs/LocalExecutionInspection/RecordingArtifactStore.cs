using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;

/// <summary>Records the sealed paths requested and delegates to the scene's artifact store, so a test proves exactly which stream a
/// window read and that metadata reads and refused windows reached the store zero times.</summary>
internal sealed class RecordingArtifactStore(IArtifactStore inner) : IArtifactStore
{
    public List<string> Reads { get; } = [];

    public Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
        CancellationToken cancellationToken)
    {
        Reads.Add(relativeStoragePath);
        return inner.VerifyAndReadSealedAsync(
            relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
    }

    public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public Task<SealedOutputFile?> DescribeSealedFileAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => throw new NotSupportedException();

    public Task<PartialReadWindow> ReadPartialAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
