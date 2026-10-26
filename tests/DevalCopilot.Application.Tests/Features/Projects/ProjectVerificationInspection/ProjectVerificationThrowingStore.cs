using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

internal sealed class ProjectVerificationThrowingStore(Exception exception) : IArtifactStore
{
    public Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
        CancellationToken cancellationToken) => throw exception;

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
