using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;

public sealed record SealedVerificationOutputArtifact(
    VerificationOutputPurpose Purpose,
    string RelativeStoragePath,
    long ByteLength,
    string ContentHash,
    bool Truncated);
