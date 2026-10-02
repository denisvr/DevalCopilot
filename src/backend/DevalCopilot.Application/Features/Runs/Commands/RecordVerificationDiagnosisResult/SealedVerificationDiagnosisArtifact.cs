using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>One already-sealed output artifact (stdout, stderr, or final response) of a verification-diagnosis attempt.
/// Sealing and hashing always happen before this record exists — never inside the database transaction.</summary>
public sealed record SealedVerificationDiagnosisArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
