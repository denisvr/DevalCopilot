using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

/// <summary>Already-sealed, already-hashed evidence for one Claude critical-review attempt
/// artifact. Sealing and hashing always happen before this record exists — never inside the
/// database transaction. Mirrors <c>SealedAgentArtifact</c> exactly.</summary>
public sealed record SealedCriticalReviewArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
