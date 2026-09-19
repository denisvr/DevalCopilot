using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

/// <summary>Already-sealed, already-hashed evidence for one Codex implementation-review attempt
/// artifact. Sealing and hashing always happen before this record exists — never inside the
/// database transaction. Mirrors <c>SealedChallengeResolutionArtifact</c> exactly.</summary>
public sealed record SealedImplementationReviewArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
