using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

/// <summary>Already-sealed, already-hashed evidence for one Codex challenge-resolution attempt
/// artifact. Sealing and hashing always happen before this record exists — never inside the
/// database transaction. Mirrors <c>SealedCriticalReviewArtifact</c> exactly.</summary>
public sealed record SealedChallengeResolutionArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
