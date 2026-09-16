using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>Filesystem lifecycle for redacted verification output beneath the application-owned
/// artifact root. The child process never receives a filesystem path or write capability.</summary>
public interface IVerificationOutputArtifactStore
{
    string GetPartialPath(Guid verificationExecutionId, VerificationOutputPurpose purpose);

    void DeletePartialFile(Guid verificationExecutionId, VerificationOutputPurpose purpose);

    Task<SealedVerificationOutputFile?> SealAsync(
        Guid verificationExecutionId,
        VerificationOutputPurpose purpose,
        CancellationToken cancellationToken);

    Task<SealedVerificationOutputFile?> DescribeSealedFileAsync(
        Guid verificationExecutionId,
        VerificationOutputPurpose purpose,
        CancellationToken cancellationToken);
}

public sealed record SealedVerificationOutputFile(string RelativeStoragePath, long ByteLength, string ContentHash);
