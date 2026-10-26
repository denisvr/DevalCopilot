using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Delegates the sealed read to the real store and records every call, so a test proves which calls reach the store at all. Every
/// other member of the port is unsupported: a window may use only the sealed verification.</summary>
internal sealed class ProjectVerificationRecordingStore(IArtifactStore inner) : IArtifactStore
{
    public List<StoreCall> Calls { get; } = [];

    public Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
        CancellationToken cancellationToken)
    {
        Calls.Add(new StoreCall(relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes));
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
