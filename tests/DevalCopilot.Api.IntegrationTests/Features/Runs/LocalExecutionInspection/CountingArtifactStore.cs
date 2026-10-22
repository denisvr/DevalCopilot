using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionInspection;

internal sealed class CountingArtifactStore(IArtifactStore inner) : IArtifactStore
{
    public List<string> Paths { get; } = [];

    public int Calls
    {
        get
        {
            lock (Paths)
            {
                return Paths.Count;
            }
        }
    }

    public Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
        CancellationToken cancellationToken)
    {
        lock (Paths)
        {
            Paths.Add(relativeStoragePath);
        }

        return inner.VerifyAndReadSealedAsync(
            relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
    }

    public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.GetPartialPath(runId, attemptId, purpose);

    public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        inner.GetSealedRelativePath(runId, attemptId, purpose);

    public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
        inner.SealAsync(runId, attemptId, purpose, cancellationToken);

    public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasSealedFile(runId, attemptId, purpose);

    public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasPartialFile(runId, attemptId, purpose);

    public Task<SealedOutputFile?> DescribeSealedFileAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
        inner.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken);

    public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        inner.DeleteOrphanedPartialFile(runId, attemptId, purpose);

    public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        inner.DeleteOrphanedSealedFile(runId, attemptId, purpose);

    public Task<PartialReadWindow> ReadPartialAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
        inner.ReadPartialAsync(runId, attemptId, purpose, fromOffset, maxBytes, cancellationToken);
}
